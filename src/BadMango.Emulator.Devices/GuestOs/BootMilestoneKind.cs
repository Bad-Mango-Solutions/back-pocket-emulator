// <copyright file="BootMilestoneKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// A point in the DOS 3.3 or ProDOS 8 boot that the boot monitor records.
/// </summary>
/// <remarks>
/// The monitor that records these milestones is #685. The names are fixed here so
/// that monitor and the status row can be built against one set.
/// </remarks>
public enum BootMilestoneKind
{
    /// <summary>Slot boot firmware was entered at <c>$Cn00</c>, for any controller.</summary>
    /// <remarks>
    /// The milestone detail names the slot and the card, for example "slot 6: Disk II" or "slot 7: SmartPort".
    /// </remarks>
    BootRomEntered = 0,

    /// <summary>The first-stage loader at <c>$0801</c> ran and was classified.</summary>
    BootSectorLoaded = 1,

    /// <summary>DOS 3.3 Boot 2 was entered at <c>$B700</c> or <c>$3700</c>.</summary>
    Dos33Boot2Entered = 2,

    /// <summary>DOS 3.3 cold start was entered at <c>$9D84</c>.</summary>
    Dos33ColdStart = 3,

    /// <summary>The ProDOS loader was entered at <c>$2000</c>.</summary>
    ProDosLoaderEntered = 4,

    /// <summary>The ProDOS kernel is in place and the system program has started.</summary>
    ProDosKernelReady = 5,

    /// <summary>The ProDOS system program started.</summary>
    SystemProgramStarted = 6,

    /// <summary>The first MLI call at <c>$BF00</c> after the kernel was ready.</summary>
    FirstMliCall = 7,
}