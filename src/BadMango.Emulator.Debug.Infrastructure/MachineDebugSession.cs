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
public sealed class MachineDebugSession : IDebugSession
{
    private readonly Lock executionLock = new();
    private readonly Lock backgroundRunLock = new();
    private readonly IDebugContext debugContext;
    private readonly IMachine machine;
    private readonly Dictionary<Guid, BackgroundRunState> backgroundRuns = [];
    private Guid? activeRunId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MachineDebugSession"/> class.
    /// </summary>
    /// <param name="debugContext">The attached debug context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="debugContext"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the context is missing required machine components.</exception>
    public MachineDebugSession(IDebugContext debugContext)
    {
        ArgumentNullException.ThrowIfNull(debugContext);
        this.debugContext = debugContext;
        machine = debugContext.Machine ?? throw new InvalidOperationException("No machine is attached to the debug context.");
    }

    /// <inheritdoc />
    public RegisterSnapshot GetRegisters()
    {
        var registers = machine.Cpu.GetRegisters();
        return new RegisterSnapshot(
            CpuType: debugContext.MachineInfo?.CpuType ?? machine.Cpu.GetType().Name,
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
            var read = machine.Bus.TryRead8(access);
            if (read.Failed)
            {
                throw new InvalidOperationException($"Failed to read memory at ${access.Address:X8}: {read.Fault.Kind}.");
            }

            data[offset] = read.Value;
        }

        return new(address, length, data);
    }

    /// <inheritdoc />
    public void WriteMemory(uint address, ReadOnlySpan<byte> data)
    {
        for (int offset = 0; offset < data.Length; offset++)
        {
            var access = CreateDebugWriteAccess(address + (uint)offset, data[offset]);
            var write = machine.Bus.TryWrite8(access, data[offset]);
            if (write.Failed)
            {
                throw new InvalidOperationException($"Failed to write memory at ${access.Address:X8}: {write.Fault.Kind}.");
            }
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
    public ulong Step(int count = 1, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Step count must be greater than zero.");
        }

        ulong executed = 0;
        lock (executionLock)
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (machine.Cpu.Halted)
                {
                    break;
                }

                machine.Step();
                executed++;
            }
        }

        return executed;
    }

    /// <inheritdoc />
    public RunResult RunUntil(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(budget);
        ValidateCondition(condition);

        lock (executionLock)
        {
            var stopwatch = Stopwatch.StartNew();
            ulong instructionCount = 0;
            ulong cycleCount = 0;

            debugContext.Breakpoints.ResetLastHit();
            debugContext.Watchpoints.ResetLastHit();

            if (TryResolveStop(condition, cycleCount, out StopReason initialStopReason, out string initialMessage))
            {
                return CreateResult(initialStopReason, instructionCount, cycleCount, stopwatch.Elapsed, initialMessage);
            }

            while (true)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    machine.Pause();
                    return CreateResult(StopReason.Cancelled, instructionCount, cycleCount, stopwatch.Elapsed, "Run cancelled.");
                }

                if (budget.WallClock is TimeSpan wallClock && stopwatch.Elapsed >= wallClock)
                {
                    machine.Pause();
                    return CreateResult(StopReason.WallClockLimit, instructionCount, cycleCount, stopwatch.Elapsed, "Wall-clock budget exhausted.");
                }

                if (cycleCount >= budget.MaxCycles)
                {
                    machine.Pause();
                    return CreateResult(StopReason.Budget, instructionCount, cycleCount, stopwatch.Elapsed, "Cycle budget exhausted.");
                }

                if (machine.Cpu.Halted)
                {
                    machine.Pause();
                    return CreateResult(StopReason.Halted, instructionCount, cycleCount, stopwatch.Elapsed, "CPU halted.");
                }

                try
                {
                    var stepResult = machine.Step();
                    instructionCount++;
                    cycleCount += stepResult.CyclesConsumed.Value;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    machine.Stop();
                    return CreateResult(StopReason.Fault, instructionCount, cycleCount, stopwatch.Elapsed, ex.Message);
                }

                if (TryResolveStop(condition, cycleCount, out StopReason stopReason, out string message))
                {
                    machine.Pause();
                    return CreateResult(stopReason, instructionCount, cycleCount, stopwatch.Elapsed, message);
                }
            }
        }
    }

    /// <inheritdoc />
    public Guid StartRun(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(budget);

        lock (backgroundRunLock)
        {
            if (activeRunId is Guid activeId && backgroundRuns.TryGetValue(activeId, out var activeState) && !activeState.Completed)
            {
                throw new InvalidOperationException($"Background run '{activeId}' is already active.");
            }

            Guid runId = Guid.NewGuid();
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var state = new BackgroundRunState(linkedTokenSource);
            backgroundRuns[runId] = state;
            activeRunId = runId;

            state.Task = Task.Run(
                () =>
                {
                    RunResult result = RunUntil(condition, budget, linkedTokenSource.Token);
                    lock (backgroundRunLock)
                    {
                        state.Result = result;
                        state.CancellationTokenSource.Dispose();
                        if (activeRunId == runId)
                        {
                            activeRunId = null;
                        }
                    }
                },
                CancellationToken.None);

            return runId;
        }
    }

    /// <inheritdoc />
    public RunResult? GetRunStatus(Guid runId)
    {
        lock (backgroundRunLock)
        {
            return backgroundRuns.TryGetValue(runId, out var state)
                ? state.Result
                : null;
        }
    }

    /// <inheritdoc />
    public bool StopRun(Guid runId)
    {
        BackgroundRunState? state;
        lock (backgroundRunLock)
        {
            if (!backgroundRuns.TryGetValue(runId, out state))
            {
                return false;
            }

            state.CancellationTokenSource.Cancel();
        }

        machine.Pause();
        return true;
    }

    /// <inheritdoc />
    public void Pause() => machine.Pause();

    /// <inheritdoc />
    public void Reset(ResetKind resetKind)
    {
        machine.Reset(resetKind is ResetKind.Hard or ResetKind.PowerOn);
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
                CpuType: machine.Cpu.GetType().Name,
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
            Flags: AccessFlags.None);

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

    private bool TryResolveStop(RunCondition condition, ulong cycleCount, out StopReason stopReason, out string message)
    {
        stopReason = StopReason.Paused;
        message = string.Empty;
        switch (condition.Kind)
        {
            case RunConditionKind.ProgramCounter:
            {
                uint expected = condition.Address.GetValueOrDefault();
                if (machine.Cpu.GetPC() != expected)
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
                    var read = machine.Bus.TryRead8(access);
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

                stopReason = StopReason.Budget;
                message = $"Cycle condition reached {targetCycles:N0} cycles.";
                return true;
            }

            default:
                return false;
        }
    }

    private RunResult CreateResult(StopReason stopReason, ulong instructions, ulong cycles, TimeSpan elapsed, string message) =>
        new(
            StopReason: stopReason,
            InstructionCount: instructions,
            CycleCount: cycles,
            Elapsed: elapsed,
            ProgramCounter: machine.Cpu.GetPC(),
            Message: message);

    private sealed class BackgroundRunState(CancellationTokenSource cancellationTokenSource)
    {
        public CancellationTokenSource CancellationTokenSource { get; } = cancellationTokenSource;

        public Task? Task { get; set; }

        public RunResult? Result { get; set; }

        public bool Completed => Result is not null;
    }
}