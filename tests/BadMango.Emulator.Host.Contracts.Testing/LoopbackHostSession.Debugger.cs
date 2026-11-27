// <copyright file="LoopbackHostSession.Debugger.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using System.Globalization;

using BadMango.Emulator.Host.Contracts;

/// <content>
/// Debugger-plane members of <see cref="LoopbackHostSession"/>.
/// </content>
public sealed partial class LoopbackHostSession
{
    /// <inheritdoc />
    public bool IsAttached(MachineId machineId)
    {
        bool attached = false;
        Update(machineId, machine => attached = machine.DebuggerAttached);
        return attached;
    }

    /// <inheritdoc />
    public HostMemoryBlock ReadMemory(MachineId machineId, uint address, int length)
    {
        byte[] copy = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, length);
            copy = machine.Memory.AsSpan((int)address, length).ToArray();
        });

        return new HostMemoryBlock(address, copy);
    }

    /// <inheritdoc />
    public void WriteMemory(MachineId machineId, uint address, ReadOnlyMemory<byte> data)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, data.Length);
            data.Span.CopyTo(machine.Memory.AsSpan((int)address, data.Length));
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<HostDisassemblyLine> Disassemble(MachineId machineId, uint address, int count)
    {
        if (count is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Disassembly count must be 1 through 256.");
        }

        HostDisassemblyLine[] lines = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, count);
            lines = new HostDisassemblyLine[count];
            for (int i = 0; i < count; i++)
            {
                uint lineAddress = address + (uint)i;
                byte value = machine.Memory[lineAddress];
                string hex = value.ToString("X2", CultureInfo.InvariantCulture);
                lines[i] = new HostDisassemblyLine(lineAddress, 1, hex, "DB", "$" + hex);
            }
        });

        return lines;
    }

    /// <inheritdoc />
    public HostRegisters GetRegisters(MachineId machineId)
    {
        Update(machineId, RequireDebugger);
        return new HostRegisters(0, 0, 0, 0, 0, 0);
    }

    /// <inheritdoc />
    public void AddBreakpoint(MachineId machineId, uint address, string? label = null)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            HostBreakpoint breakpoint = new(address, label);
            int index = machine.Breakpoints.FindIndex(existing => existing.Address == address);
            if (index >= 0)
            {
                machine.Breakpoints[index] = breakpoint;
            }
            else
            {
                machine.Breakpoints.Add(breakpoint);
            }
        });
    }

    /// <inheritdoc />
    public bool RemoveBreakpoint(MachineId machineId, uint address)
    {
        bool removed = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            removed = machine.Breakpoints.RemoveAll(existing => existing.Address == address) > 0;
        });

        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostBreakpoint> GetBreakpoints(MachineId machineId)
    {
        HostBreakpoint[] breakpoints = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            breakpoints = machine.Breakpoints.ToArray();
        });

        return breakpoints;
    }

    /// <inheritdoc />
    public void ClearBreakpoints(MachineId machineId)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            machine.Breakpoints.Clear();
        });
    }

    /// <inheritdoc />
    public void AddWatch(MachineId machineId, uint address, HostWatchAccess access = HostWatchAccess.ReadWrite, bool stopOnHit = false, string? label = null)
    {
        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "The watch access is not defined.");
        }

        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            HostWatch watch = new(address, access, stopOnHit, label);
            int index = machine.Watches.FindIndex(existing => existing.Address == address);
            if (index >= 0)
            {
                machine.Watches[index] = watch;
            }
            else
            {
                machine.Watches.Add(watch);
            }
        });
    }

    /// <inheritdoc />
    public bool RemoveWatch(MachineId machineId, uint address)
    {
        bool removed = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            removed = machine.Watches.RemoveAll(existing => existing.Address == address) > 0;
        });

        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostWatch> GetWatches(MachineId machineId)
    {
        HostWatch[] watches = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            watches = machine.Watches.ToArray();
        });

        return watches;
    }

    /// <inheritdoc />
    public void ClearWatches(MachineId machineId)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            machine.Watches.Clear();
        });
    }

    /// <inheritdoc />
    public void RaiseBreakpointHit(MachineId machineId, uint address)
    {
        bool hit = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            if (!machine.Breakpoints.Exists(breakpoint => breakpoint.Address == address))
            {
                throw new InvalidOperationException($"No breakpoint is set at ${address:X}.");
            }

            hit = true;
        });

        if (hit)
        {
            Notify(sink => sink.OnBreakpointHit(new HostBreakpointNotice(machineId, address)));
        }
    }

    private static void RequireDebugger(LoopbackMachine machine)
    {
        if (!machine.DebuggerAttached)
        {
            throw new InvalidOperationException("The debugger is not attached.");
        }
    }

    private static void RequireRange(uint address, int length)
    {
        if (length <= 0 || address >= LoopbackMachine.MemorySize || length > LoopbackMachine.MemorySize - address)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The range is outside the 64 KiB stub memory.");
        }
    }
}