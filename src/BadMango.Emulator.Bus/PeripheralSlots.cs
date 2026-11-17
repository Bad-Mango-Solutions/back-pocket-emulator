// <copyright file="PeripheralSlots.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Apple II peripheral slot numbers managed by the slot manager.
/// </summary>
/// <remarks>
/// Slot 0 is the language card and is not a peripheral slot. Expansion cards occupy slots
/// <see cref="Minimum"/> through <see cref="Maximum"/>.
/// </remarks>
public static class PeripheralSlots
{
    /// <summary>
    /// The first peripheral slot number.
    /// </summary>
    public const int Minimum = 1;

    /// <summary>
    /// The last peripheral slot number.
    /// </summary>
    public const int Maximum = 7;
}