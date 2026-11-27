// <copyright file="TrapScope.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Who owns a trap, and therefore which unload removes it.
/// </summary>
/// <remarks>
/// <para>
/// Scope is metadata. <see cref="TrapRegistry.ContainsAddress"/> and trap execution do not read it,
/// so the instruction loop does not branch on scope and does not allocate to honor it.
/// </para>
/// <para>
/// <see cref="GuestOsSession"/> is removed as a group by
/// <see cref="Interfaces.ITrapRegistry.UnregisterScope"/> when that guest OS session ends.
/// <see cref="Card"/> traps keep their slot lifetime
/// (<see cref="Interfaces.ITrapRegistry.UnregisterSlotTraps"/>).
/// <see cref="Machine"/> traps, including observe-only boot traps, stay across that session end.
/// </para>
/// </remarks>
public enum TrapScope
{
    /// <summary>
    /// Lives with the machine. A guest OS reboot does not remove it.
    /// </summary>
    Machine = 0,

    /// <summary>
    /// Lives with a peripheral card. Removed with that card's slot, not with the guest OS.
    /// </summary>
    Card = 1,

    /// <summary>
    /// Lives for one guest OS session. Unregistered when that session ends.
    /// </summary>
    GuestOsSession = 2,
}