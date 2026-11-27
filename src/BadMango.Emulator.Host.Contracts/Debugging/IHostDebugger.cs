// <copyright file="IHostDebugger.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Debugger extension of the control plane. Breakpoints, watches, memory, and disassembly.
/// </summary>
/// <remarks>
/// <para>
/// Attach and detach live on <see cref="IHostControl"/>. These methods throw when the debugger
/// is not attached. The shell can power a machine without taking this interface.
/// </para>
/// <para>
/// Budgeted runs (<c>StartRun</c>, <c>StopRun</c>, <c>RunUntil</c>, <c>Step</c>), abort reaction,
/// bus fault status, and mapping trace stay on <c>IDebugSession</c>. They name the bus and the
/// REPL run table. This extension does not.
/// </para>
/// <para>
/// The loopback keeps a 64 KiB stub memory and reports each byte as a data line. It does not
/// decode opcodes and it does not run a CPU.
/// </para>
/// </remarks>
public interface IHostDebugger
{
    /// <summary>
    /// Gets whether the debugger is attached.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns><see langword="true"/> when it is attached.</returns>
    bool IsAttached(MachineId machineId);

    /// <summary>
    /// Reads stub memory.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The first address. The loopback address space is 64 KiB.</param>
    /// <param name="length">The number of bytes, at least 1, and inside the address space.</param>
    /// <returns>A copy of the bytes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the range is outside the stub memory.</exception>
    HostMemoryBlock ReadMemory(MachineId machineId, uint address, int length);

    /// <summary>
    /// Writes stub memory. The bytes are copied.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The first address.</param>
    /// <param name="data">The bytes to copy in.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the range is outside the stub memory or <paramref name="data"/> is empty.</exception>
    void WriteMemory(MachineId machineId, uint address, ReadOnlyMemory<byte> data);

    /// <summary>
    /// Disassembles stub bytes. The loopback emits one data line per byte.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The first address.</param>
    /// <param name="count">The line count, 1 through 256.</param>
    /// <returns>The lines.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is outside 1–256 or the range leaves the stub memory.</exception>
    IReadOnlyList<HostDisassemblyLine> Disassemble(MachineId machineId, uint address, int count);

    /// <summary>
    /// Gets the register snapshot. The loopback returns zeros.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The registers.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    HostRegisters GetRegisters(MachineId machineId);

    /// <summary>
    /// Adds or replaces a breakpoint.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The address.</param>
    /// <param name="label">An optional label.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    void AddBreakpoint(MachineId machineId, uint address, string? label = null);

    /// <summary>
    /// Removes a breakpoint.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when a breakpoint was removed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    bool RemoveBreakpoint(MachineId machineId, uint address);

    /// <summary>
    /// Lists breakpoints.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The breakpoints, in the order they were added.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    IReadOnlyList<HostBreakpoint> GetBreakpoints(MachineId machineId);

    /// <summary>
    /// Removes every breakpoint.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    void ClearBreakpoints(MachineId machineId);

    /// <summary>
    /// Adds or replaces a watch.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The address.</param>
    /// <param name="access">Which accesses are watched.</param>
    /// <param name="stopOnHit">Whether a hit should stop the machine.</param>
    /// <param name="label">An optional label.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    void AddWatch(MachineId machineId, uint address, HostWatchAccess access = HostWatchAccess.ReadWrite, bool stopOnHit = false, string? label = null);

    /// <summary>
    /// Removes a watch.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when a watch was removed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    bool RemoveWatch(MachineId machineId, uint address);

    /// <summary>
    /// Lists watches.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The watches, in the order they were added.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    IReadOnlyList<HostWatch> GetWatches(MachineId machineId);

    /// <summary>
    /// Removes every watch.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached.</exception>
    void ClearWatches(MachineId machineId);
}