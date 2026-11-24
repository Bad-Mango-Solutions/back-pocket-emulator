// <copyright file="ProDos8Details.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// ProDOS 8 global-page fields. The slotless clock reads the clock flag from this type.
/// </summary>
public sealed record ProDos8Details : IGuestOsDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProDos8Details"/> class.
    /// </summary>
    /// <param name="kVersion"><c>KVERSION</c> at <c>$BFFF</c>.</param>
    /// <param name="iVersion"><c>IVERSION</c> at <c>$BFFD</c>.</param>
    /// <param name="machId"><c>MACHID</c> at <c>$BF98</c>.</param>
    /// <param name="hasClock"><see langword="true"/> when <c>MACHID</c> bit 0 is set.</param>
    public ProDos8Details(byte kVersion, byte iVersion, byte machId, bool hasClock)
    {
        this.KVersion = kVersion;
        this.IVersion = iVersion;
        this.MachId = machId;
        this.HasClock = hasClock;
    }

    /// <summary>Gets <c>KVERSION</c>.</summary>
    /// <value>The byte at <c>$BFFF</c>.</value>
    public byte KVersion { get; }

    /// <summary>Gets <c>IVERSION</c>.</summary>
    /// <value>The byte at <c>$BFFD</c>.</value>
    public byte IVersion { get; }

    /// <summary>Gets <c>MACHID</c>.</summary>
    /// <value>The byte at <c>$BF98</c>.</value>
    public byte MachId { get; }

    /// <summary>Gets a value indicating whether <c>MACHID</c> bit 0 is set.</summary>
    /// <value><see langword="true"/> when the global page reports a recognizable clock.</value>
    public bool HasClock { get; }
}