// <copyright file="MachineDebugSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Diagnostics;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;

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
/// The attached machine is read from the debug context on every call. A null machine throws
/// <see cref="InvalidOperationException"/>. Replacing the machine while a background run is active
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
/// </remarks>
public sealed class MachineDebugSession : IDebugSession
{
    private const int ExecutionLockPollMilliseconds = 15;
    private const string NoMachineMessage = "No machine is attached to the debug context.";

    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock executionLock = new();
    private readonly Lock backgroundRunLock = new();
    private readonly IDebugContext debugContext;
    private readonly Dictionary<Guid, BackgroundRunState> backgroundRuns = [];
    private readonly Dictionary<Guid, RunResult> completedRuns = [];
    private Guid? activeRunId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MachineDebugSession"/> class.
    /// </summary>
    /// <param name="debugContext">The attached debug context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="debugContext"/> is <see langword="null"/>.</exception>
    public MachineDebugSession(IDebugContext debugContext)
    {
        ArgumentNullException.ThrowIfNull(debugContext);
        this.debugContext = debugContext;
    }

    private IMachine Machine => debugContext.Machine ?? throw new InvalidOperationException(NoMachineMessage);

    /// <inheritdoc />
    public RegisterSnapshot GetRegisters()
    {
        var registers = Machine.Cpu.GetRegisters();
        return new RegisterSnapshot(
            CpuType: debugContext.MachineInfo?.CpuType ?? Machine.Cpu.GetType().Name,
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
    public MemoryBlock ReadMemory(uint address, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        byte[] data = new byte[length];
        for (int offset = 0; offset < length; offset++)
        {
            var access = CreateDebugReadAccess(address + (uint)offset);
            var read = Machine.Bus.TryRead8(access);
            if (read.Failed)
            {
                throw new InvalidOperationException($"Failed to read memory at ${access.Address:X8}: {read.Fault.Kind}.");
            }

            data[offset] = read.Value;
        }

        return new(address, length, data);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Serialized with execution. Throws <see cref="InvalidOperationException"/> when a background run is active.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A background run is active.</exception>
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
                    throw new InvalidOperationException($"Failed to write memory at ${access.Address:X8}: {write.Fault.Kind}.");
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
    public ulong Step(int count = 1, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Step count must be greater than zero.");
        }

        ThrowIfBackgroundRunActive();
        IMachine bound = Machine;
        ulong executed = 0;
        lock (executionLock)
        {
            ThrowIfBackgroundRunActive();
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureStillAttached(bound);
                if (bound.Cpu.Halted)
                {
                    break;
                }

                bound.Step();
                executed++;
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

                if (bound.Cpu.Halted)
                {
                    return Finish(bound, StopReason.Halted, instructionCount, cycleCount, stopwatch.Elapsed, "CPU halted.", stopMachine: false);
                }

                try
                {
                    var stepResult = bound.Step();
                    instructionCount++;
                    cycleCount += stepResult.CyclesConsumed.Value;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !IsCriticalException(ex))
                {
                    return Finish(bound, StopReason.Fault, instructionCount, cycleCount, stopwatch.Elapsed, ex.Message, stopMachine: true);
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

    private static bool IsCriticalException(Exception ex) =>
        ex is OutOfMemoryException or AccessViolationException or InvalidProgramException or BadImageFormatException;

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
            case RunConditionKind.Breakpoint when condition.BreakpointAddress is null && condition.Address is null:
                throw new ArgumentException("Breakpoint condition requires BreakpointAddress or Address.", nameof(condition));
            case RunConditionKind.Watchpoint when condition.WatchAddress is null && condition.Address is null:
                throw new ArgumentException("Watchpoint condition requires WatchAddress or Address.", nameof(condition));
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