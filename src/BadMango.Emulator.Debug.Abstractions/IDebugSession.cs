// <copyright file="IDebugSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using BadMango.Emulator.Core.Cpu;

/// <summary>
/// Defines the core contract for a debug session that can query and control emulator state.
/// </summary>
/// <remarks>
/// GitHub issue #337 calls <see cref="IDebugSession"/> for inspection and control.
/// That host does not call <c>CpuBase.Read8</c> and does not issue its own <c>TryRead8</c>.
/// Memory reads stay on <see cref="ReadMemory"/>, which keeps the existing fault mask.
/// </remarks>
public interface IDebugSession
{
    /// <summary>
    /// Gets or sets how the attached 65C02 treats <c>SignalLine.Abort</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The setter throws when the attached CPU is not a 65C02 and the new value is
    /// <see cref="AbortReaction.HostBreak"/>. A 65816 uses its abort vector instead of a host stop.
    /// </exception>
    /// <remarks>
    /// The getter returns <see cref="AbortReaction.NoOp"/> when the CPU is not a 65C02.
    /// Setting <see cref="AbortReaction.NoOp"/> on that CPU does not throw. A machine reset
    /// does not change the value. This property is the only debugger path that selects
    /// <see cref="AbortReaction.HostBreak"/>; it does not pulse the abort line.
    /// </remarks>
    AbortReaction AbortReaction { get; set; }

    /// <summary>
    /// Gets the latched bus fault status.
    /// </summary>
    /// <value>
    /// The numeric <c>FaultKind</c>. Zero means no fault is latched. The low byte is the
    /// section 10 mask stored by <see cref="ReadMemory"/>.
    /// </value>
    /// <remarks>
    /// A debug read does not change this value. A successful access leaves a latched value
    /// in place. <see cref="ClearFaultStatus"/> writes zero and clears the fault address.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No bus is attached.</exception>
    ushort FaultStatus { get; }

    /// <summary>
    /// Gets the address of the byte that latched <see cref="FaultStatus"/>.
    /// </summary>
    /// <value>The faulting address, or zero when <see cref="FaultStatus"/> is zero.</value>
    /// <exception cref="InvalidOperationException">No bus is attached.</exception>
    uint FaultAddress { get; }

    /// <summary>
    /// Clears the latched bus fault status.
    /// </summary>
    /// <remarks>
    /// Writes zero into the status and the address, clears the bus's private latch, and
    /// deasserts <c>SignalLine.Abort</c> when this bus asserted it. No emulated instruction
    /// calls this method.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No bus is attached.</exception>
    void ClearFaultStatus();

    /// <summary>
    /// Gets the current register snapshot for the active machine.
    /// </summary>
    /// <returns>The register snapshot.</returns>
    RegisterSnapshot GetRegisters();

    /// <summary>
    /// Reads a memory block from the machine.
    /// </summary>
    /// <param name="address">The memory address to read from.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>
    /// A memory block containing the requested bytes. Unreadable addresses stay in the block:
    /// <see cref="MemoryBlock.Faults"/> marks them and the corresponding data byte is 0.
    /// </returns>
    MemoryBlock ReadMemory(uint address, int length);

    /// <summary>
    /// Writes a memory block into the machine.
    /// </summary>
    /// <param name="address">The memory address to write to.</param>
    /// <param name="data">The bytes to write.</param>
    void WriteMemory(uint address, ReadOnlySpan<byte> data);

    /// <summary>
    /// Disassembles a sequence of instructions from the current machine state.
    /// </summary>
    /// <param name="address">The starting execution address.</param>
    /// <param name="count">The number of lines to disassemble.</param>
    /// <returns>The disassembly lines.</returns>
    IReadOnlyList<DisassemblyLine> Disassemble(uint address, int count);

    /// <summary>
    /// Steps the CPU for a number of instructions.
    /// </summary>
    /// <param name="count">The number of instructions to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="maxCycles">
    /// Optional cycle budget for this call. <see langword="null"/> keeps the session default.
    /// The step stops before an instruction that would begin at or beyond the budget.
    /// </param>
    /// <returns>The number of instructions processed.</returns>
    ulong Step(int count = 1, CancellationToken cancellationToken = default, ulong? maxCycles = null);

    /// <summary>
    /// Runs the machine until the provided condition is met or the budget is exhausted.
    /// </summary>
    /// <param name="condition">The completion condition.</param>
    /// <param name="budget">The execution budget.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution result.</returns>
    RunResult RunUntil(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a background run and returns its identifier.
    /// </summary>
    /// <param name="condition">The completion condition.</param>
    /// <param name="budget">The execution budget.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The background run identifier.</returns>
    Guid StartRun(RunCondition condition, RunBudget budget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a background run.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <returns>The completed run result, or <see langword="null"/> while still running or when unknown.</returns>
    RunResult? GetRunStatus(Guid runId);

    /// <summary>
    /// Requests cancellation for a background run and waits until that run has finished.
    /// </summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="timeout">
    /// How long to wait for the run to finish. <see langword="null"/> uses the implementation default.
    /// </param>
    /// <returns><see langword="true"/> if the run was found; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// A finished run is a no-op. An active run is cancelled, and the method does not return
    /// until the run has left the in-flight table or the wait times out. Callers can write
    /// memory immediately after a successful return.
    /// </remarks>
    bool StopRun(Guid runId, TimeSpan? timeout = null);

    /// <summary>
    /// Pauses the running machine.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resets the machine by the supplied reset type.
    /// </summary>
    /// <param name="resetKind">The reset mode.</param>
    void Reset(ResetKind resetKind);

    /// <summary>
    /// Adds a breakpoint at the supplied address.
    /// </summary>
    /// <param name="address">The breakpoint address.</param>
    /// <param name="label">An optional label.</param>
    void AddBreakpoint(uint address, string? label = null);

    /// <summary>
    /// Removes a breakpoint.
    /// </summary>
    /// <param name="address">The breakpoint address.</param>
    /// <returns>True when the breakpoint existed and was removed.</returns>
    bool RemoveBreakpoint(uint address);

    /// <summary>
    /// Gets all breakpoints currently configured.
    /// </summary>
    /// <returns>The current breakpoint list.</returns>
    IReadOnlyList<Breakpoint> GetBreakpoints();

    /// <summary>
    /// Clears all breakpoints.
    /// </summary>
    void ClearBreakpoints();

    /// <summary>
    /// Adds a memory watchpoint.
    /// </summary>
    /// <param name="address">The watch address.</param>
    /// <param name="access">The access type to watch.</param>
    /// <param name="stopOnHit">Whether execution should stop on hit.</param>
    /// <param name="label">An optional label.</param>
    void AddWatchpoint(uint address, WatchAccess access = WatchAccess.ReadWrite, bool stopOnHit = false, string? label = null);

    /// <summary>
    /// Removes a watchpoint.
    /// </summary>
    /// <param name="address">The watchpoint address.</param>
    /// <returns>True when the watchpoint existed and was removed.</returns>
    bool RemoveWatchpoint(uint address);

    /// <summary>
    /// Gets all currently configured watchpoints.
    /// </summary>
    /// <returns>The current watchpoint list.</returns>
    IReadOnlyList<Watchpoint> GetWatchpoints();

    /// <summary>
    /// Clears all watchpoints.
    /// </summary>
    void ClearWatchpoints();

    /// <summary>
    /// Gets the metadata describing the current machine.
    /// </summary>
    /// <returns>The machine information.</returns>
    MachineInfo GetMachineInfo();
}