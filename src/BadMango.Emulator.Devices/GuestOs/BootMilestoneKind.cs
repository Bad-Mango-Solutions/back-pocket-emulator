// <copyright file="BootMilestoneKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// A point in a guest boot that the boot monitor records.
/// </summary>
/// <remarks>
/// <para>
/// The kinds name addresses and roles, not an operating system. Which OS is in
/// <see cref="BootMilestone.Os"/>, and the human text is <see cref="BootMilestone.Detail"/>
/// (for example "DOS 3.3 Boot 2 at $B700" or "MLI $C8 OPEN").
/// </para>
/// <para>
/// The monitor that records these milestones is #685. It learns the OS-specific
/// addresses from <see cref="IGuestOsBootProfile"/>.
/// </para>
/// </remarks>
public enum BootMilestoneKind
{
    /// <summary>Slot boot firmware was entered at <c>$Cn00</c>, for any card.</summary>
    /// <remarks>
    /// The milestone detail names the slot and the card, for example "slot 6: Disk II" or "slot 7: SmartPort".
    /// </remarks>
    BootFirmwareEntered = 0,

    /// <summary>The first-stage loader at <c>$0801</c> ran and was classified.</summary>
    BootSectorLoaded = 1,

    /// <summary><c>$0801</c> ran with a loader that matches no registered boot profile.</summary>
    /// <remarks>
    /// The detail includes the first bytes of the boot sector.
    /// This is a normal terminal state for self-booting software, not an error.
    /// </remarks>
    CustomBootLoaderEntered = 2,

    /// <summary>The operating system's loader is running.</summary>
    /// <remarks>The detail names that loader, for example "DOS 3.3 Boot 2 at $B700".</remarks>
    LoaderEntered = 3,

    /// <summary>The operating system's kernel is in place.</summary>
    KernelReady = 4,

    /// <summary>The system program has started.</summary>
    SystemProgramStarted = 5,

    /// <summary>The first system call after the kernel was ready.</summary>
    /// <remarks>The detail names the call, for example "MLI $C8 OPEN".</remarks>
    FirstSystemCall = 6,
}