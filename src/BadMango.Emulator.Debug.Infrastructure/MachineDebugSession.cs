// <copyright file="MachineDebugSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Diagnostics;
using System.Text.RegularExpressions;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.Interfaces;
using BadMango.Emulator.Rendering;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Media;

using Serilog;

using InfrastructureWatchAccess = WatchAccess;
using SessionWatchAccess = BadMango.Emulator.Debug.Abstractions.WatchAccess;

/// <summary>
/// Implements <see cref="IDebugSession"/> over a live <see cref="IMachine"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Step"/>, <see cref="RunUntil"/>, <see cref="WriteMemory"/>, and <see cref="Reset"/>
/// share one <see cref="System.Threading.Lock"/> so those calls cannot interleave.
/// All four throw <see cref="InvalidOperationException"/> when a background run is active
/// instead of waiting for it. That run holds the execution lock until it stops.
/// Stop the background run first.
/// </para>
/// <para>
/// The attached machine is read from the debug context on every call. Run, reset, and background
/// runs throw <see cref="InvalidOperationException"/> when no machine is attached. Register, memory,
/// and single-step calls fall back to the context CPU and bus so a CPU-only debug context still works.
/// Replacing the machine while a background run is active
/// cancels that run without further calls on the detached instance. Calls made before the run
/// observes the replacement still see an active background run.
/// </para>
/// <para>
/// <see cref="GetRegisters"/>, <see cref="ReadMemory"/>, <see cref="Disassemble"/>,
/// <see cref="GetRunStatus"/>, <see cref="GetBreakpoints"/>, <see cref="GetWatchpoints"/>,
/// and <see cref="GetMachineInfo"/> do not take the execution lock. They are peeks and may
/// observe a torn snapshot while a run is active.
/// </para>
/// <para>
/// The in-flight run table drops a background run when its task ends, including after
/// <see cref="StopRun(Guid, TimeSpan?)"/>. That method waits, up to a bounded timeout, until the
/// run has finished. <see cref="GetRunStatus"/> keeps the result until the next <see cref="StartRun"/>.
/// <see cref="StopRun(Guid, TimeSpan?)"/> on a finished run does not cancel a disposed token.
/// </para>
/// <para>
/// GitHub issue #337 calls <see cref="IDebugSession"/>. This type is that session.
/// It does not add another memory-read shape beside <see cref="ReadMemory"/>.
/// </para>
/// </remarks>
public sealed class MachineDebugSession : IDebugSession, IMachineIoSession
{
    /// <summary>
    /// Cycles between screen checks. One Apple II frame is 65 cycles by 262 lines.
    /// </summary>
    public const ulong ScreenTextPollIntervalCycles = 17_030;

    private const int ExecutionLockPollMilliseconds = 15;
    private const string NoMachineMessage = "No machine is attached to the debug context.";

    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScreenTextMatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Lock executionLock = new();
    private readonly Lock backgroundRunLock = new();
    private readonly IDebugContext debugContext;
    private readonly ILogger logger;
    private readonly ulong? defaultCycleBudget;
    private readonly Dictionary<Guid, BackgroundRunState> backgroundRuns = [];
    private readonly Dictionary<Guid, RunResult> completedRuns = [];
    private Guid? activeRunId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MachineDebugSession"/> class.
    /// </summary>
    /// <param name="debugContext">The attached debug context.</param>
    /// <param name="logger">The logger for mount rollback and unmount failures. A silent logger is used when omitted.</param>
    /// <param name="defaultCycleBudget">
    /// Optional cycle cap applied by <see cref="Step"/>. <see langword="null"/> does not cap stepping.
    /// This is the process <c>--max-cycles</c> budget.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="debugContext"/> is <see langword="null"/>.</exception>
    public MachineDebugSession(IDebugContext debugContext, ILogger? logger = null, ulong? defaultCycleBudget = null)
    {
        ArgumentNullException.ThrowIfNull(debugContext);
        this.debugContext = debugContext;
        this.logger = logger ?? Serilog.Core.Logger.None;
        this.defaultCycleBudget = defaultCycleBudget;
    }

    /// <inheritdoc />
    public AbortReaction AbortReaction
    {
        get => debugContext.Cpu is BadMango.Emulator.Emulation.Cpu.Cpu65C02 cpu
            ? cpu.AbortReaction
            : BadMango.Emulator.Core.Cpu.AbortReaction.NoOp;

        set
        {
            if (debugContext.Cpu is BadMango.Emulator.Emulation.Cpu.Cpu65C02 cpu)
            {
                cpu.AbortReaction = value;
                return;
            }

            if (value == BadMango.Emulator.Core.Cpu.AbortReaction.HostBreak)
            {
                throw new InvalidOperationException(
                    "HostBreak is only valid when the attached CPU is a 65C02.");
            }
        }
    }

    /// <inheritdoc />
    public ushort FaultStatus => (ushort)this.RequireBus().FaultStatus;

    /// <inheritdoc />
    public uint FaultAddress => this.RequireBus().FaultAddress;

    /// <inheritdoc />
    public bool MappingTraceEnabled
    {
        get => this.RequireBus().MappingTraceEnabled;
        set => this.RequireBus().MappingTraceEnabled = value;
    }

    private IMachine Machine => debugContext.Machine ?? throw new InvalidOperationException(NoMachineMessage);

    /// <inheritdoc />
    public void ClearFaultStatus() => this.RequireBus().ClearFaultStatus();

    /// <inheritdoc />
    public void SetMappingTraceRange(uint start, uint endInclusive) =>
        this.RequireBus().SetMappingTraceRange(start, endInclusive);

    /// <inheritdoc />
    public void ClearMappingTraceRange() => this.RequireBus().ClearMappingTraceRange();

    /// <inheritdoc />
    public bool TryGetMappingTraceRange(out uint start, out uint endInclusive)
    {
        IMemoryBus bus = this.RequireBus();
        if (!bus.HasMappingTraceRange)
        {
            start = 0;
            endInclusive = 0;
            return false;
        }

        start = bus.MappingTraceRangeStart;
        endInclusive = bus.MappingTraceRangeEnd;
        return true;
    }

    /// <inheritdoc />
    public RegisterSnapshot GetRegisters()
    {
        var cpu = this.RequireCpu();
        var registers = cpu.GetRegisters();
        return new RegisterSnapshot(
            CpuType: debugContext.MachineInfo?.CpuType ?? cpu.GetType().Name,
            ProgramCounter: registers.PC.GetAddr(),
            Accumulator: registers.A.GetDWord(),
            XRegister: registers.X.GetDWord(),
            YRegister: registers.Y.GetDWord(),
            StackPointer: registers.SP.GetDWord(),
            StatusRegister: (byte)registers.P,
            DirectPage: registers.D.GetAddr(),
            DataBank: registers.DBR,
            ExtraBank: registers.PBR,
            KRegister: null,
            BRegister: null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each byte is a <c>TryRead8</c> debug read. A successful read carries a zero
    /// <see cref="BusFault"/>: <see cref="FaultKind"/> is <see cref="FaultKind.None"/> and the
    /// address, device, and region stay at their defaults. The session does not read those
    /// fields from a success. A nonzero low byte of <see cref="FaultKind"/> is stored in
    /// <see cref="MemoryBlock.Faults"/> and that data byte stays 0. The mask is omitted when
    /// every byte is <c>00</c>. A zero low byte keeps the data byte, including a high-byte-only
    /// kind. The read continues. The bus reports the fault as a result, so a large unmapped span
    /// does not throw once per byte. A debug read does not latch fault status and does not
    /// enter the fault ring.
    /// </remarks>
    public MemoryBlock ReadMemory(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var bus = this.RequireBus();
        byte[] data = new byte[length];
        byte[] faults = new byte[length];
        bool anyFault = false;
        for (int offset = 0; offset < length; offset++)
        {
            var access = CreateDebugReadAccess(address + (uint)offset);
            var read = bus.TryRead8(access);
            byte mask = (byte)read.Fault.Kind;
            if (mask != 0)
            {
                faults[offset] = mask;
                anyFault = true;
                continue;
            }

            data[offset] = read.Value;
        }

        return new(address, length, data, Faults: anyFault ? faults : null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Serialized with execution. Throws <see cref="InvalidOperationException"/> when a background run is active.
    /// A bus fault throws <see cref="MemoryWriteException"/>, whose message uses
    /// <see cref="DebugOutputRenderer.FormatFault"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A background run is active.</exception>
    /// <exception cref="MemoryWriteException">A byte could not be written.</exception>
    public void WriteMemory(uint address, ReadOnlySpan<byte> data)
    {
        EnterExecutionUnlessBackgroundRunActive();
        try
        {
            ThrowIfBackgroundRunActive();
            for (int offset = 0; offset < data.Length; offset++)
            {
                var access = CreateDebugWriteAccess(address + (uint)offset, data[offset]);
                var write = Machine.Bus.TryWrite8(access, data[offset]);
                if (write.Failed)
                {
                    throw new MemoryWriteException(access.Address, write.Fault);
                }
            }
        }
        finally
        {
            executionLock.Exit();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DisassemblyLine> Disassemble(uint address, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (debugContext.Disassembler is null)
        {
            throw new InvalidOperationException("No disassembler is attached to the debug context.");
        }

        return debugContext.Disassembler
            .DisassembleInstructions(address, count)
            .Select(static instruction =>
                new DisassemblyLine(
                    Address: instruction.Address,
                    Mnemonic: instruction.Instruction.ToString(),
                    Operand: instruction.FormatOperand(),
                    Size: instruction.TotalLength,
                    Bytes: instruction.FormatBytes()))
            .ToList();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A background run is active, or the attached machine was replaced.</exception>
    public ulong Step(int count = 1, CancellationToken cancellationToken = default, ulong? maxCycles = null)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Step count must be greater than zero.");
        }

        ThrowIfBackgroundRunActive();
        if (debugContext.Machine is not IMachine bound)
        {
            return this.StepCpu(count, cancellationToken, maxCycles);
        }

        ulong executed = 0;
        ulong cycles = 0;
        lock (executionLock)
        {
            ThrowIfBackgroundRunActive();
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureStillAttached(bound);
                if (this.CycleBudgetExhausted(cycles, maxCycles) || this.StopForHalt(bound.Cpu))
                {
                    break;
                }

                var stepped = bound.Step();
                executed++;
                cycles += stepped.CyclesConsumed.Value;
                if (stepped.State == CpuRunState.WaitingForInterrupt)
                {
                    if (bound.Scheduler.JumpToNextEventAndDispatch())
                    {
                        continue;
                    }

                    break;
                }

                if (stepped.State is CpuRunState.Halted or CpuRunState.Stopped)
                {
                    break;
                }
            }
        }

        return executed;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">A background run is active.</exception>
    public RunResult RunUntil(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateCondition(condition);
        ThrowIfBackgroundRunActive();

        IMachine bound = Machine;
        lock (executionLock)
        {
            ThrowIfBackgroundRunActive();
            var stopwatch = Stopwatch.StartNew();
            ulong instructionCount = 0;
            ulong cycleCount = 0;

            debugContext.Breakpoints.ResetLastHit();
            debugContext.Watchpoints.ResetLastHit();

            if (TryResolveStop(bound, condition, cycleCount, out StopReason initialStopReason, out string initialMessage))
            {
                return CreateResult(bound, initialStopReason, instructionCount, cycleCount, stopwatch.Elapsed, initialMessage);
            }

            while (true)
            {
                if (Replaced(bound, instructionCount, cycleCount, stopwatch.Elapsed) is RunResult replaced)
                {
                    return replaced;
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    return Finish(bound, StopReason.Cancelled, instructionCount, cycleCount, stopwatch.Elapsed, "Run cancelled.", stopMachine: false);
                }

                if (budget.WallClock is TimeSpan wallClock && stopwatch.Elapsed >= wallClock)
                {
                    return Finish(bound, StopReason.WallClockLimit, instructionCount, cycleCount, stopwatch.Elapsed, "Wall-clock budget exhausted.", stopMachine: false);
                }

                if (cycleCount >= budget.MaxCycles)
                {
                    return Finish(bound, StopReason.Budget, instructionCount, cycleCount, stopwatch.Elapsed, "Cycle budget exhausted.", stopMachine: false);
                }

                if (this.StopForHalt(bound.Cpu))
                {
                    return Finish(bound, StopReason.Halted, instructionCount, cycleCount, stopwatch.Elapsed, $"CPU halted ({bound.Cpu.HaltReason}).", stopMachine: false);
                }

                CpuStepResult stepResult;
                try
                {
                    stepResult = bound.Step();
                    instructionCount++;
                    cycleCount += stepResult.CyclesConsumed.Value;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !IsCriticalException(ex))
                {
                    return Finish(bound, StopReason.Fault, instructionCount, cycleCount, stopwatch.Elapsed, ex.Message, stopMachine: true);
                }

                if (stepResult.State == CpuRunState.WaitingForInterrupt)
                {
                    if (bound.Scheduler.JumpToNextEventAndDispatch())
                    {
                        continue;
                    }

                    return Finish(bound, StopReason.Halted, instructionCount, cycleCount, stopwatch.Elapsed, "CPU waiting for interrupt.", stopMachine: false);
                }

                if (stepResult.State is CpuRunState.Halted or CpuRunState.Stopped)
                {
                    return Finish(bound, StopReason.Halted, instructionCount, cycleCount, stopwatch.Elapsed, $"CPU halted ({bound.Cpu.HaltReason}).", stopMachine: false);
                }

                if (Replaced(bound, instructionCount, cycleCount, stopwatch.Elapsed) is RunResult replacedAfterStep)
                {
                    return replacedAfterStep;
                }

                if (TryResolveStop(bound, condition, cycleCount, out StopReason stopReason, out string message))
                {
                    return Finish(bound, stopReason, instructionCount, cycleCount, stopwatch.Elapsed, message, stopMachine: false);
                }
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The run condition is missing a required field.</exception>
    public Guid StartRun(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateCondition(condition);

        lock (backgroundRunLock)
        {
            if (activeRunId is Guid activeId && backgroundRuns.ContainsKey(activeId))
            {
                throw new InvalidOperationException($"Background run '{activeId}' is already active.");
            }

            // Only the latest completed result is retained, so finished runs cannot accumulate.
            completedRuns.Clear();

            Guid runId = Guid.NewGuid();
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var state = new BackgroundRunState(linkedTokenSource);
            backgroundRuns[runId] = state;
            activeRunId = runId;

            state.Task = Task.Run(() => CompleteBackgroundRun(runId, state, condition, budget), CancellationToken.None);
            return runId;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A completed result stays available until the next <see cref="StartRun"/>.
    /// </remarks>
    public RunResult? GetRunStatus(Guid runId)
    {
        lock (backgroundRunLock)
        {
            if (backgroundRuns.ContainsKey(runId))
            {
                return null;
            }

            return completedRuns.TryGetValue(runId, out RunResult? result) ? result : null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Stopping a run that has already finished is a no-op and does not throw.
    /// An active run is cancelled and this method waits until that run has left the
    /// in-flight table, so a following mutation is not rejected as still running.
    /// <see langword="null"/> waits up to five seconds.
    /// </remarks>
    /// <exception cref="TimeoutException">The run did not finish within <paramref name="timeout"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is negative.</exception>
    public bool StopRun(Guid runId, TimeSpan? timeout = null)
    {
        if (timeout is { } requested && requested < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Stop timeout must be zero or greater.");
        }

        Task? task;
        lock (backgroundRunLock)
        {
            if (backgroundRuns.TryGetValue(runId, out BackgroundRunState? state))
            {
                // Cancel under the same lock that disposes the source when the task ends.
                state.TryCancel();
                task = state.Task;
            }
            else if (completedRuns.ContainsKey(runId))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        if (debugContext.Machine is IMachine attached)
        {
            attached.Pause();
        }

        TimeSpan wait = timeout ?? DefaultStopTimeout;
        if (task is not null && !task.Wait(wait))
        {
            throw new TimeoutException($"Background run '{runId}' did not stop within {wait.TotalSeconds:0.###} seconds.");
        }

        return true;
    }

    /// <inheritdoc />
    public void Pause() => Machine.Pause();

    /// <inheritdoc />
    /// <remarks>
    /// Serialized with execution. Throws <see cref="InvalidOperationException"/> when a background run is active.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A background run is active.</exception>
    public void Reset(ResetKind resetKind)
    {
        EnterExecutionUnlessBackgroundRunActive();
        try
        {
            ThrowIfBackgroundRunActive();
            Machine.Reset(resetKind is ResetKind.Hard or ResetKind.PowerOn);
        }
        finally
        {
            executionLock.Exit();
        }
    }

    /// <inheritdoc />
    public void AddBreakpoint(uint address, string? label = null) => debugContext.Breakpoints.Add(address, label);

    /// <inheritdoc />
    public bool RemoveBreakpoint(uint address) => debugContext.Breakpoints.Remove(address);

    /// <inheritdoc />
    public IReadOnlyList<Breakpoint> GetBreakpoints()
    {
        return debugContext.Breakpoints.GetAll()
            .Select(static item =>
                new Breakpoint(
                    Address: item.Address,
                    Label: item.Label,
                    Enabled: item.Enabled,
                    HitCount: item.Hits > int.MaxValue ? int.MaxValue : (int)item.Hits))
            .ToList();
    }

    /// <inheritdoc />
    public void ClearBreakpoints() => debugContext.Breakpoints.Clear();

    /// <inheritdoc />
    public void AddWatchpoint(uint address, SessionWatchAccess access = SessionWatchAccess.ReadWrite, bool stopOnHit = false, string? label = null) =>
        debugContext.Watchpoints.Add(address, ToInfrastructureWatchAccess(access), stopOnHit, label);

    /// <inheritdoc />
    public bool RemoveWatchpoint(uint address) => debugContext.Watchpoints.Remove(address);

    /// <inheritdoc />
    public IReadOnlyList<Watchpoint> GetWatchpoints()
    {
        return debugContext.Watchpoints.GetAll()
            .Select(static item =>
                new Watchpoint(
                    Address: item.Address,
                    Access: ToSessionWatchAccess(item.Access),
                    StopOnHit: item.StopOnHit,
                    Enabled: item.Enabled,
                    HitCount: item.Hits > int.MaxValue ? int.MaxValue : (int)item.Hits,
                    Label: item.Label))
            .ToList();
    }

    /// <inheritdoc />
    public void ClearWatchpoints() => debugContext.Watchpoints.Clear();

    /// <inheritdoc />
    public BadMango.Emulator.Debug.Abstractions.MachineInfo GetMachineInfo()
    {
        BadMango.Emulator.Core.Configuration.MachineInfo? info = debugContext.MachineInfo;
        if (info is null)
        {
            return new(
                Name: "unknown",
                DisplayName: "Unknown Machine",
                CpuType: Machine.Cpu.GetType().Name,
                MemorySize: 0,
                Description: null);
        }

        return new(
            Name: info.Name,
            DisplayName: info.DisplayName,
            CpuType: info.CpuType,
            MemorySize: info.MemorySize,
            Description: info.Description);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When the machine has no main-memory provider, each text byte is a debug <c>TryRead8</c>.
    /// That read does not latch fault status and does not enter the fault ring.
    /// </remarks>
    public ScreenTextSnapshot GetScreenText() => GetScreenText(Machine);

    /// <inheritdoc />
    public VideoStateSnapshot GetVideoState()
    {
        IMachine machine = Machine;
        IVideoDevice video = machine.GetComponent<IVideoDevice>()
            ?? throw new InvalidOperationException("No video device found in the attached machine.");
        return new(
            Mode: video.CurrentMode.ToString(),
            IsTextMode: video.IsTextMode,
            IsMixedMode: video.IsMixedMode,
            IsPage2: video.IsPage2,
            IsHiRes: video.IsHiRes,
            Is80Column: video.Is80Column,
            IsDoubleHiRes: video.IsDoubleHiRes,
            IsVerticalBlanking: video.IsVerticalBlanking,
            Annunciators: [.. video.Annunciators]);
    }

    /// <inheritdoc />
    public TypeKeysReceipt TypeKeys(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        IMachine machine = Machine;
        IKeyboardInput input = machine.GetComponent<IKeyboardInput>()
            ?? throw new InvalidOperationException("No keyboard input found in the attached machine.");
        BadMango.Emulator.Devices.TypeTextReceipt receipt = input.TypeText(text);
        return new TypeKeysReceipt(receipt.Accepted, [.. receipt.Skipped], receipt.Rejected);
    }

    /// <inheritdoc />
    public void MountDisk(int slot, int drive, string path, bool writeProtect = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, PeripheralSlots.Minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, PeripheralSlots.Maximum);
        ArgumentOutOfRangeException.ThrowIfLessThan(drive, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IMachine machine = Machine;
        IDiskController controller = ResolveController(machine, slot, drive, out int driveIndex);
        IDebugPathResolver? resolver = debugContext.PathResolver;
        string resolvedPath = path;
        if (resolver is not null)
        {
            if (!resolver.TryResolve(path, out string? resolved))
            {
                throw new InvalidOperationException($"Cannot resolve path: '{path}'.");
            }

            resolvedPath = resolved!;
        }

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Disk image file not found.", resolvedPath);
        }

        DiskImageFactory factory = debugContext.DiskImageFactory
            ?? throw new InvalidOperationException("DiskImageFactory is not available on the debug context.");
        DiskImageOpenResult open = factory.Open(resolvedPath, forceReadOnly: writeProtect);

        I525Media? media = open switch
        {
            Image525AndBlockResult both => both.TrackMedia,
            Image525Result trackOnly => trackOnly.Media,
            _ => null,
        };

        if (media is null)
        {
            open.Dispose();
            throw new InvalidOperationException(
                $"Image '{resolvedPath}' has no 5.25\" track view and cannot be mounted on slot {slot}.");
        }

        try
        {
            controller.Mount(driveIndex, media, resolvedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            open.Dispose();
            throw new InvalidOperationException($"Mount rejected: {ex.Message}", ex);
        }

        if (debugContext.MountedDisks.IsDisposed)
        {
            RollbackMount(controller, slot, drive, driveIndex, open, disposed: null);
        }

        try
        {
            debugContext.MountedDisks.Track(slot, driveIndex, open);
        }
        catch (ObjectDisposedException ex)
        {
            RollbackMount(controller, slot, drive, driveIndex, open, ex);
        }
    }

    /// <inheritdoc />
    public void EjectDisk(int slot, int drive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, PeripheralSlots.Minimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, PeripheralSlots.Maximum);
        ArgumentOutOfRangeException.ThrowIfLessThan(drive, 1);

        IMachine machine = Machine;
        IDiskController controller = ResolveController(machine, slot, drive, out int driveIndex);
        bool ejected = controller.Eject(driveIndex);
        if (!ejected)
        {
            DriveSnapshot snapshot = controller.GetDriveSnapshot(driveIndex);
            if (snapshot.HasMedia)
            {
                logger.Error(
                    "Unmount of slot {Slot} drive {Drive} failed because pending writes could not be flushed. The image remains mounted.",
                    slot,
                    drive);
                throw new InvalidOperationException(
                    $"Eject of slot {slot} drive {drive} failed because pending writes could not be flushed. The image remains mounted.");
            }

            throw new InvalidOperationException($"Slot {slot} drive {drive} is already empty.");
        }

        if (!debugContext.MountedDisks.IsDisposed)
        {
            try
            {
                debugContext.MountedDisks.Release(slot, driveIndex);
            }
            catch (ObjectDisposedException ex)
            {
                logger.Warning(
                    ex,
                    "Mounted-disk registry was disposed while releasing the image for slot {Slot} drive {Drive}. The drive was already unmounted.",
                    slot,
                    drive);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DiskControllerSnapshot> ListDisks()
    {
        IMachine machine = Machine;
        ISlotManager slotManager = machine.GetComponent<ISlotManager>()
            ?? throw new InvalidOperationException("No slot manager available on the attached machine.");

        var controllers = new List<DiskControllerSnapshot>();
        for (int slot = PeripheralSlots.Minimum; slot <= PeripheralSlots.Maximum; slot++)
        {
            ISlotCard? card = slotManager.GetCard(slot);
            if (card is not IDiskController controller)
            {
                continue;
            }

            var drives = new List<BadMango.Emulator.Debug.Abstractions.DiskDriveSnapshot>(controller.DriveCount);
            for (int i = 0; i < controller.DriveCount; i++)
            {
                try
                {
                    DriveSnapshot snapshot = controller.GetDriveSnapshot(i);
                    drives.Add(ToDriveSnapshot(i + 1, snapshot));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    logger.Warning(ex, "Drive {Drive} on slot {Slot} could not be read.", i + 1, slot);
                    drives.Add(UnreadableDrive(i + 1, ex.Message));
                }
            }

            controllers.Add(new DiskControllerSnapshot(slot, card.Name, controller.DriveCount, drives));
        }

        return controllers;
    }

    /// <inheritdoc />
    public ScreenTextWaitResult WaitForScreenText(string regexPattern, RunBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(regexPattern);
        ArgumentNullException.ThrowIfNull(budget);
        var regex = new Regex(regexPattern, RegexOptions.CultureInvariant, ScreenTextMatchTimeout);

        EnterExecutionUnlessBackgroundRunActive();
        try
        {
            ThrowIfBackgroundRunActive();
            IMachine bound = Machine;
            var stopwatch = Stopwatch.StartNew();
            ulong cycleCount = 0;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ScreenTextWaitResult.Cancelled(cycleCount);
                }

                EnsureStillAttached(bound);

                try
                {
                    if (regex.IsMatch(GetScreenText(bound).CombinedText))
                    {
                        return ScreenTextWaitResult.Match(cycleCount);
                    }
                }
                catch (RegexMatchTimeoutException ex)
                {
                    return ScreenTextWaitResult.Fault(cycleCount, ex);
                }

                if (budget.WallClock is TimeSpan wallClock && stopwatch.Elapsed >= wallClock)
                {
                    return ScreenTextWaitResult.WallClockTimeout(cycleCount);
                }

                if (bound.Cpu.Halted)
                {
                    return ScreenTextWaitResult.Halted(cycleCount);
                }

                if (cycleCount >= budget.MaxCycles)
                {
                    return ScreenTextWaitResult.BudgetExhausted(cycleCount);
                }

                ulong frameBudget = ScreenTextPollIntervalCycles;
                ulong remaining = budget.MaxCycles - cycleCount;
                if (frameBudget > remaining)
                {
                    frameBudget = remaining;
                }

                try
                {
                    while (frameBudget > 0)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return ScreenTextWaitResult.Cancelled(cycleCount);
                        }

                        if (bound.Cpu.Halted)
                        {
                            break;
                        }

                        var stepResult = bound.Step();
                        ulong consumed = stepResult.CyclesConsumed.Value;
                        if (consumed == 0)
                        {
                            consumed = 1;
                        }

                        cycleCount += consumed;
                        frameBudget = frameBudget > consumed ? frameBudget - consumed : 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    return ScreenTextWaitResult.Cancelled(cycleCount);
                }
                catch (Exception ex) when (!IsCriticalException(ex))
                {
                    return ScreenTextWaitResult.Fault(cycleCount, ex);
                }
            }
        }
        finally
        {
            executionLock.Exit();
        }
    }

    private static bool IsCriticalException(Exception ex) =>
        ex is OutOfMemoryException or AccessViolationException or InvalidProgramException or BadImageFormatException;

    private static BadMango.Emulator.Debug.Abstractions.DiskDriveSnapshot ToDriveSnapshot(int drive, DriveSnapshot snapshot)
    {
        DiskGeometrySnapshot? geometry = snapshot.Geometry is null
            ? null
            : new DiskGeometrySnapshot(
                snapshot.Geometry.Value.TrackCount,
                snapshot.Geometry.Value.SectorsPerTrack,
                snapshot.Geometry.Value.BytesPerSector,
                snapshot.Geometry.Value.SectorOrder.ToString(),
                snapshot.Geometry.Value.TotalBytes);
        return new(
            Drive: drive,
            Selected: snapshot.Selected,
            MotorOn: snapshot.MotorOn,
            PhaseLatch: snapshot.PhaseLatch,
            QuarterTrack: snapshot.QuarterTrack,
            WriteProtect: snapshot.WriteProtect,
            HasMedia: snapshot.HasMedia,
            MountedImagePath: snapshot.MountedImagePath,
            Geometry: geometry);
    }

    private static BadMango.Emulator.Debug.Abstractions.DiskDriveSnapshot UnreadableDrive(int drive, string error) =>
        new(
            Drive: drive,
            Selected: false,
            MotorOn: false,
            PhaseLatch: 0,
            QuarterTrack: 0,
            WriteProtect: false,
            HasMedia: false,
            MountedImagePath: null,
            Geometry: null,
            Error: error);

    private static RunResult CreateResult(IMachine bound, StopReason stopReason, ulong instructions, ulong cycles, TimeSpan elapsed, string message) =>
        new(
            StopReason: stopReason,
            InstructionCount: instructions,
            CycleCount: cycles,
            Elapsed: elapsed,
            ProgramCounter: bound.Cpu.GetPC(),
            Message: message);

    private static BusAccess CreateDebugReadAccess(uint address) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects);

    private static BusAccess CreateDebugWriteAccess(uint address, byte value) =>
        new(
            Address: address,
            Value: value,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugWrite,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects);

    private static InfrastructureWatchAccess ToInfrastructureWatchAccess(SessionWatchAccess access)
    {
        return access switch
        {
            SessionWatchAccess.Read => InfrastructureWatchAccess.Read,
            SessionWatchAccess.Write => InfrastructureWatchAccess.Write,
            _ => InfrastructureWatchAccess.ReadWrite,
        };
    }

    private static SessionWatchAccess ToSessionWatchAccess(InfrastructureWatchAccess access)
    {
        return access switch
        {
            InfrastructureWatchAccess.Read => SessionWatchAccess.Read,
            InfrastructureWatchAccess.Write => SessionWatchAccess.Write,
            _ => SessionWatchAccess.ReadWrite,
        };
    }

    private static void ValidateCondition(RunCondition condition)
    {
        switch (condition.Kind)
        {
            case RunConditionKind.ProgramCounter when condition.Address is null:
                throw new ArgumentException("ProgramCounter condition requires Address.", nameof(condition));
            case RunConditionKind.MemoryEquals when condition.MemoryAddress is null || condition.MemoryValue is null:
                throw new ArgumentException("MemoryEquals condition requires MemoryAddress and MemoryValue.", nameof(condition));
            case RunConditionKind.CycleCount when condition.CycleLimit is null:
                throw new ArgumentException("CycleCount condition requires CycleLimit.", nameof(condition));
        }
    }

    // backgroundRunLock is never held while waiting for executionLock. The background task
    // takes backgroundRunLock only after RunUntil releases execution. An active background
    // run is rejected instead of waited on, so this cannot stall until that run finishes.
    private void EnterExecutionUnlessBackgroundRunActive()
    {
        while (true)
        {
            ThrowIfBackgroundRunActive();
            if (executionLock.TryEnter(TimeSpan.FromMilliseconds(ExecutionLockPollMilliseconds)))
            {
                return;
            }
        }
    }

    private void ThrowIfBackgroundRunActive()
    {
        lock (backgroundRunLock)
        {
            if (activeRunId is Guid activeId
                && backgroundRuns.TryGetValue(activeId, out BackgroundRunState? state)
                && state.Task?.Id != Task.CurrentId)
            {
                throw new InvalidOperationException(
                    "A background run is active. Stop the run before stepping, running, writing memory, or resetting the machine.");
            }
        }
    }

    private IDiskController ResolveController(IMachine machine, int slot, int drive, out int driveIndex)
    {
        ISlotManager slotManager = machine.GetComponent<ISlotManager>()
            ?? throw new InvalidOperationException("No slot manager available on the attached machine.");
        if (slotManager.GetCard(slot) is not IDiskController controller)
        {
            throw new InvalidOperationException($"Slot {slot} does not contain a disk controller.");
        }

        if (drive > controller.DriveCount)
        {
            throw new ArgumentOutOfRangeException(nameof(drive), $"Drive must be in 1..{controller.DriveCount} for slot {slot}.");
        }

        driveIndex = drive - 1;
        return controller;
    }

    private void RollbackMount(
        IDiskController controller,
        int slot,
        int drive,
        int driveIndex,
        DiskImageOpenResult open,
        ObjectDisposedException? disposed)
    {
        if (!TryUnmountForRollback(controller, slot, drive, driveIndex))
        {
            throw new InvalidOperationException(
                "Mount tracking is unavailable because the debug context has been disposed, and rolling back the mount failed. The image remains mounted.",
                disposed);
        }

        open.Dispose();
        throw new InvalidOperationException(
            "Mount tracking is unavailable because the debug context has been disposed.",
            disposed);
    }

    private bool TryUnmountForRollback(IDiskController controller, int slot, int drive, int driveIndex)
    {
        try
        {
            if (controller.Eject(driveIndex))
            {
                return true;
            }

            logger.Error(
                "Rollback unmount of slot {Slot} drive {Drive} failed because pending writes could not be flushed. The image remains mounted and will not be disposed.",
                slot,
                drive);
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.Error(
                ex,
                "Rollback unmount of slot {Slot} drive {Drive} failed. The image remains mounted and will not be disposed.",
                slot,
                drive);
            return false;
        }
    }

    private ScreenTextSnapshot GetScreenText(IMachine machine)
    {
        IVideoDevice video = machine.GetComponent<IVideoDevice>()
            ?? throw new InvalidOperationException("No video device found in the attached machine.");
        IMainMemoryProvider? mainMemory = machine.GetComponent<IMainMemoryProvider>();
        IMemoryBus bus = machine.Bus;
        IExtended80ColumnDevice? aux = machine.GetComponent<IExtended80ColumnDevice>();

        Func<ushort, byte> readMain = mainMemory is null
            ? address =>
            {
                BusResult<byte> read = bus.TryRead8(CreateDebugReadAccess(address));
                return read.Ok ? read.Value : (byte)0;
            }
            : mainMemory.ReadMainRam;

        bool is80Column = video.Is80Column;
        string[] lines = TextScreen.ReadLines(
            readMain,
            aux is null ? null : aux.ReadAuxRam,
            is80Column,
            video.IsPage2);

        return new ScreenTextSnapshot(
            Rows: lines.Length,
            Columns: lines.Length == 0 ? TextScreen.Columns40 : lines[0].Length,
            IsTextMode: video.IsTextMode,
            IsMixedMode: video.IsMixedMode,
            Is80Column: is80Column,
            IsPage2: video.IsPage2,
            Lines: lines);
    }

    private void CompleteBackgroundRun(Guid runId, BackgroundRunState state, RunCondition condition, RunBudget budget)
    {
        RunResult? result = null;
        Exception? critical = null;
        try
        {
            result = RunUntil(condition, budget, state.CancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            result = new RunResult(StopReason.Cancelled, 0, 0, TimeSpan.Zero, TryGetProgramCounter(), "Run cancelled.");
        }
        catch (Exception ex) when (!IsCriticalException(ex))
        {
            result = new RunResult(StopReason.Fault, 0, 0, TimeSpan.Zero, TryGetProgramCounter(), ex.Message);
        }
        catch (Exception ex)
        {
            critical = ex;
        }

        lock (backgroundRunLock)
        {
            if (result is not null)
            {
                completedRuns[runId] = result;
            }

            backgroundRuns.Remove(runId);
            if (activeRunId == runId)
            {
                activeRunId = null;
            }

            state.DisposeCancellationSource();
        }

        if (critical is not null)
        {
            throw critical;
        }
    }

    private uint TryGetProgramCounter()
    {
        try
        {
            IMachine? attached = debugContext.Machine;
            if (attached is null)
            {
                return 0;
            }

            return attached.Cpu.GetPC();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or NullReferenceException or InvalidOperationException)
        {
            return 0;
        }
    }

    private void EnsureStillAttached(IMachine bound)
    {
        if (!ReferenceEquals(debugContext.Machine, bound))
        {
            throw new InvalidOperationException("The attached machine was replaced.");
        }
    }

    private RunResult? Replaced(IMachine bound, ulong instructions, ulong cycles, TimeSpan elapsed)
    {
        if (ReferenceEquals(debugContext.Machine, bound))
        {
            return null;
        }

        return new RunResult(StopReason.Cancelled, instructions, cycles, elapsed, 0, "The attached machine was replaced.");
    }

    private RunResult Finish(IMachine bound, StopReason stopReason, ulong instructions, ulong cycles, TimeSpan elapsed, string message, bool stopMachine)
    {
        if (Replaced(bound, instructions, cycles, elapsed) is RunResult replaced)
        {
            return replaced;
        }

        if (stopMachine)
        {
            bound.Stop();
        }
        else
        {
            bound.Pause();
        }

        return CreateResult(bound, stopReason, instructions, cycles, elapsed, message);
    }

    private bool TryResolveStop(IMachine bound, RunCondition condition, ulong cycleCount, out StopReason stopReason, out string message)
    {
        stopReason = StopReason.Paused;
        message = string.Empty;
        switch (condition.Kind)
        {
            case RunConditionKind.ProgramCounter:
            {
                uint expected = condition.Address.GetValueOrDefault();
                if (bound.Cpu.GetPC() != expected)
                {
                    return false;
                }

                stopReason = StopReason.Breakpoint;
                message = $"Program counter reached ${expected:X8}.";
                return true;
            }

            case RunConditionKind.Breakpoint:
            {
                uint? target = condition.BreakpointAddress ?? condition.Address;
                uint? hit = debugContext.Breakpoints.LastHitAddress;
                if (hit is null || (target is not null && hit.Value != target.Value))
                {
                    return false;
                }

                stopReason = StopReason.Breakpoint;
                message = $"Breakpoint hit at ${hit.Value:X8}.";
                return true;
            }

            case RunConditionKind.Watchpoint:
            {
                uint? target = condition.WatchAddress ?? condition.Address;
                uint? hit = debugContext.Watchpoints.LastHitAddress;
                if (hit is null || (target is not null && hit.Value != target.Value))
                {
                    return false;
                }

                stopReason = StopReason.Watchpoint;
                message = $"Watchpoint hit at ${hit.Value:X8}.";
                return true;
            }

            case RunConditionKind.MemoryEquals:
            {
                uint memoryAddress = condition.MemoryAddress.GetValueOrDefault();
                var expected = condition.MemoryValue!;
                for (int i = 0; i < expected.Count; i++)
                {
                    var access = CreateDebugReadAccess(memoryAddress + (uint)i);
                    var read = bound.Bus.TryRead8(access);
                    if (read.Failed || read.Value != expected[i])
                    {
                        return false;
                    }
                }

                stopReason = StopReason.MemoryCondition;
                message = $"Memory matched at ${memoryAddress:X8}.";
                return true;
            }

            case RunConditionKind.CycleCount:
            {
                ulong targetCycles = condition.CycleLimit.GetValueOrDefault();
                if (cycleCount < targetCycles)
                {
                    return false;
                }

                stopReason = StopReason.CycleCount;
                message = $"Cycle condition reached {targetCycles:N0} cycles.";
                return true;
            }

            default:
                return false;
        }
    }

    private ICpu RequireCpu()
    {
        if (debugContext.Machine?.Cpu is ICpu machineCpu)
        {
            return machineCpu;
        }

        // Only a real debug context may be read without a machine. A mock that
        // has detached the machine must still throw, without touching other members.
        if (debugContext is Commands.DebugContext concrete && concrete.Cpu is ICpu cpu)
        {
            return cpu;
        }

        throw new InvalidOperationException("No CPU attached to debug context.");
    }

    private IMemoryBus RequireBus()
    {
        if (debugContext.Machine?.Bus is IMemoryBus machineBus)
        {
            return machineBus;
        }

        if (debugContext is Commands.DebugContext concrete && concrete.Bus is IMemoryBus bus)
        {
            return bus;
        }

        throw new InvalidOperationException(NoMachineMessage);
    }

    private bool CycleBudgetExhausted(ulong cycles, ulong? maxCycles = null)
    {
        ulong? budget = maxCycles ?? this.defaultCycleBudget;
        return budget is ulong limit && cycles >= limit;
    }

    private bool StopForHalt(ICpu cpu) =>
        cpu.Halted && !cpu.IsWaitingForInterrupt;

    private ulong StepCpu(int count, CancellationToken cancellationToken, ulong? maxCycles = null)
    {
        var cpu = this.RequireCpu();
        ulong executed = 0;
        ulong cycles = 0;
        lock (executionLock)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (this.CycleBudgetExhausted(cycles, maxCycles) || this.StopForHalt(cpu))
                {
                    break;
                }

                var stepped = cpu.Step();
                executed++;
                cycles += stepped.CyclesConsumed.Value;
                if (stepped.State is CpuRunState.Halted or CpuRunState.Stopped or CpuRunState.WaitingForInterrupt)
                {
                    break;
                }
            }
        }

        return executed;
    }

    private sealed class BackgroundRunState(CancellationTokenSource cancellationTokenSource)
    {
        private bool cancellationSourceDisposed;

        public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;

        public Task? Task { get; set; }

        public bool TryCancel()
        {
            if (cancellationSourceDisposed)
            {
                return false;
            }

            CancellationTokenSource.Cancel();
            return true;
        }

        public void DisposeCancellationSource()
        {
            if (cancellationSourceDisposed)
            {
                return;
            }

            cancellationSourceDisposed = true;
            CancellationTokenSource.Dispose();
        }
    }
}