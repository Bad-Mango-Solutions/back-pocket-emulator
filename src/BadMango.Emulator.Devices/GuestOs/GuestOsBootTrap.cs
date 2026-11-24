// <copyright file="GuestOsBootTrap.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One observe-only execution address a boot profile asks the monitor to arm, and the milestone it records.
/// </summary>
/// <param name="Address">Guest address of the trap, such as <c>$0801</c> or <c>$BF00</c>.</param>
/// <param name="Kind">The milestone recorded when that address runs.</param>
public readonly record struct GuestOsBootTrap(ushort Address, BootMilestoneKind Kind);