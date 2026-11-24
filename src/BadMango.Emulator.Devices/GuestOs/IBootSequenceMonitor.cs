// <copyright file="IBootSequenceMonitor.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Boot stage and milestone log. Defined here; implemented by the boot monitor (#685).
/// </summary>
/// <remarks>
/// <para>
/// #688 reads <see cref="Stage"/> for the status-monitor row and can be built beside #685.
/// This contract installs nothing. When the feature is off, the host does not register
/// an implementation, and the instruction loop does not gain a boot trap.
/// </para>
/// <para>
/// Milestone traps, when #685 arms them, return not-handled and are removed once the
/// operating system is ready. <see cref="Milestones"/> is that monitor's log.
/// The monitor arms <c>$Cn00</c> for every occupied slot whose card has boot firmware,
/// not just Disk II, and falls back to <see cref="IGuestOsDetector"/> when no milestone was seen.
/// </para>
/// </remarks>
public interface IBootSequenceMonitor
{
    /// <summary>Raised when <see cref="Stage"/> changes.</summary>
    event EventHandler<GuestOsBootStageChangedEventArgs>? StageChanged;

    /// <summary>Raised when a boot milestone is recorded.</summary>
    event EventHandler<BootMilestone>? MilestoneReached;

    /// <summary>Gets the current boot stage.</summary>
    /// <value><see cref="GuestOsBootStage.None"/> before any boot firmware is entered.</value>
    GuestOsBootStage Stage { get; }

    /// <summary>Gets the milestones recorded for this boot.</summary>
    /// <value>The log, oldest first. The monitor bounds it.</value>
    IReadOnlyList<BootMilestone> Milestones { get; }
}