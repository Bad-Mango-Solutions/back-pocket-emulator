// <copyright file="ProfileSlotRomSelection.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Initial internal-slot-ROM selection declared by <c>devices.slots</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompositeIOTarget"/> receives this selection when the composite
/// handler is created. The extended 80-column card uses the same values as its
/// power-on and reset state for INTCXROM and SLOTC3ROM, so device initialization
/// does not replace the profile.
/// </para>
/// </remarks>
public sealed class ProfileSlotRomSelection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSlotRomSelection"/> class.
    /// </summary>
    /// <param name="c3RomEnabled">
    /// <see langword="true"/> to overlay internal ROM at $C300-$C3FF.
    /// </param>
    /// <param name="cxRomEnabled">
    /// <see langword="true"/> to overlay internal ROM at $C100-$CFFF.
    /// </param>
    public ProfileSlotRomSelection(bool c3RomEnabled, bool cxRomEnabled)
    {
        InternalC3Rom = c3RomEnabled;
        InternalCxRom = cxRomEnabled;
    }

    /// <summary>
    /// Gets a value indicating whether internal ROM overlays $C300-$C3FF.
    /// </summary>
    public bool InternalC3Rom { get; }

    /// <summary>
    /// Gets a value indicating whether internal ROM overlays $C100-$CFFF.
    /// </summary>
    public bool InternalCxRom { get; }
}