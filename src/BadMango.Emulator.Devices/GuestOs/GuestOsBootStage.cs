// <copyright file="GuestOsBootStage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Where a guest boot is, from reset through a ready operating system.
/// </summary>
/// <remarks>
/// The boot monitor (#685) moves this value. The status monitor (#688) displays it.
/// This assembly defines the stages and does not advance them.
/// </remarks>
public enum GuestOsBootStage
{
    /// <summary>Power-on or reset. The boot ROM has not been entered.</summary>
    None = 0,

    /// <summary>The Disk II boot ROM at <c>$Cn00</c> is running.</summary>
    BootRom = 1,

    /// <summary>The boot sector is running at <c>$0801</c>.</summary>
    Boot1 = 2,

    /// <summary>DOS 3.3 Boot 2, or the ProDOS loader, including language-card relocation.</summary>
    Loader = 3,

    /// <summary>The operating system has booted and is accepting commands.</summary>
    Ready = 4,

    /// <summary>The boot sector was not recognized, or the boot left the known path.</summary>
    Unknown = 5,
}