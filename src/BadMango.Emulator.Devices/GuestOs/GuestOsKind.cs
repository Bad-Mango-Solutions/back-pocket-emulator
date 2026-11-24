// <copyright file="GuestOsKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Guest operating system a memory probe can report.
/// </summary>
/// <remarks>
/// <see cref="None"/> means the probes found no DOS 3.3 or ProDOS 8 signature.
/// <see cref="Unknown"/> means a ProDOS-shaped global page is present but
/// <c>KVERSION</c> is not one of the versions through 2.4.3.
/// </remarks>
public enum GuestOsKind
{
    /// <summary>A ProDOS-shaped global page whose <c>KVERSION</c> is not recognized.</summary>
    Unknown = 0,

    /// <summary>No DOS 3.3 or ProDOS 8 signature in memory.</summary>
    None = 1,

    /// <summary>DOS 3.3, or a DOS 3.3-compatible page 3 vector table.</summary>
    Dos33 = 2,

    /// <summary>ProDOS 8 through 2.4.3.</summary>
    ProDos8 = 3,
}