// <copyright file="GuestOsSessionTrap.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One call trap a boot profile installs for the life of its guest OS session.
/// </summary>
/// <param name="Address">Guest address of the trap, such as the DOS 3.3 file manager.</param>
/// <param name="Name">Human-readable name stored on the trap.</param>
/// <remarks>
/// The monitor registers these with <see cref="BadMango.Emulator.Bus.TrapScope.GuestOsSession"/> when the boot
/// reaches <see cref="GuestOsBootStage.Loader"/> or <see cref="GuestOsBootStage.Ready"/>.
/// <see cref="BadMango.Emulator.Bus.Interfaces.ITrapRegistry.UnregisterScope"/> removes them when the session ends.
/// A driver such as the slotless ProDOS clock registers its own trap with that same scope.
/// </remarks>
public readonly record struct GuestOsSessionTrap(ushort Address, string Name);