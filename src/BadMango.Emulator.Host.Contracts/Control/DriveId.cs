// <copyright file="DriveId.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Addresses one drive on a machine. Slot is 1–7. Drive is 1-based.
/// </summary>
/// <param name="Slot">The slot number.</param>
/// <param name="Drive">The 1-based drive number.</param>
public readonly record struct DriveId(int Slot, int Drive)
{
    /// <summary>
    /// Gets the label a drive button shows, such as <c>S6 D1</c>.
    /// </summary>
    public string Label => $"S{Slot} D{Drive}";
}