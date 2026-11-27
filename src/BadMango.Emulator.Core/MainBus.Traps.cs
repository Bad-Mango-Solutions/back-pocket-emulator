// <copyright file="MainBus.Traps.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Runtime.CompilerServices;

using BadMango.Emulator.Core.Interfaces.Cpu;

using Interfaces;

/// <content>
/// Data-trap dispatch for read and write accesses. Instruction fetch stays on the CPU.
/// </content>
public sealed partial class MainBus
{
    /// <summary>
    /// Registry consulted for <see cref="TrapOperation.Read"/> and <see cref="TrapOperation.Write"/>.
    /// </summary>
    private TrapRegistry? dataTrapRegistry;

    /// <summary>
    /// CPU passed to a data-trap handler.
    /// </summary>
    private ICpu? dataTrapCpu;

    /// <summary>
    /// Event context passed to a data-trap handler.
    /// </summary>
    private IEventContext? dataTrapContext;

    /// <summary>
    /// Non-zero while a data trap is running, so a handler that touches the bus cannot re-enter.
    /// </summary>
    private int dataTrapDepth;

    /// <summary>
    /// Attaches the machine registry so data reads and writes can run read and write traps.
    /// </summary>
    /// <param name="registry">The machine's trap registry.</param>
    /// <param name="cpu">The CPU passed to trap handlers.</param>
    /// <param name="context">The machine, used as the trap event context.</param>
    internal void AttachTrapDispatch(TrapRegistry registry, ICpu cpu, IEventContext context)
    {
        dataTrapRegistry = registry;
        dataTrapCpu = cpu;
        dataTrapContext = context;
    }

    /// <summary>
    /// Runs a read or write trap after a data access. Fetch and debug intents return immediately.
    /// </summary>
    /// <param name="access">The access that just completed.</param>
    /// <param name="operation"><see cref="TrapOperation.Read"/> or <see cref="TrapOperation.Write"/>.</param>
    /// <remarks>
    /// When no registry is attached, or no traps of that operation are registered, this is one
    /// null check or one counter check. The handler call is kept out of line.
    /// </remarks>
    private void DispatchDataTrap(in BusAccess access, TrapOperation operation)
    {
        TrapRegistry? registry = dataTrapRegistry;
        if (registry is null || dataTrapDepth != 0)
        {
            return;
        }

        if (operation == TrapOperation.Read)
        {
            if (access.Intent is not (AccessIntent.DataRead or AccessIntent.DmaRead)
                || !registry.HasReadTraps
                || !registry.ContainsOperation(access.Address, TrapOperation.Read))
            {
                return;
            }
        }
        else if (access.Intent is not (AccessIntent.DataWrite or AccessIntent.DmaWrite)
            || !registry.HasWriteTraps
            || !registry.ContainsOperation(access.Address, TrapOperation.Write))
        {
            return;
        }

        InvokeDataTrap(registry, access.Address, operation);
    }

    /// <summary>
    /// Invokes one data trap. Out of line so the bus read and write paths stay small.
    /// </summary>
    /// <param name="registry">The attached registry.</param>
    /// <param name="address">The address that was accessed.</param>
    /// <param name="operation">The data operation.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InvokeDataTrap(TrapRegistry registry, Addr address, TrapOperation operation)
    {
        dataTrapDepth++;
        try
        {
            registry.TryExecute(address, operation, dataTrapCpu!, this, dataTrapContext!);
        }
        finally
        {
            dataTrapDepth--;
        }
    }
}