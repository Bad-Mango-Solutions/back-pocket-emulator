// <copyright file="BootMilestone.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Core;

/// <summary>
/// One observe-only boot milestone. Also the argument of <see cref="IBootSequenceMonitor.MilestoneReached"/>.
/// </summary>
/// <remarks>
/// This is a class rather than a record so it can be the <see cref="EventArgs"/> payload
/// <c>EventHandler&lt;BootMilestone&gt;</c> requires. Records cannot inherit <see cref="EventArgs"/>.
/// </remarks>
public sealed class BootMilestone : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BootMilestone"/> class.
    /// </summary>
    /// <param name="at">The machine cycle when the milestone was observed.</param>
    /// <param name="kind">Which boot point this is.</param>
    /// <param name="pc">The program counter at the milestone.</param>
    /// <param name="detail">What the monitor saw. Not empty.</param>
    /// <param name="os">The operating system this milestone belongs to, or <see langword="null"/> when it is shared.</param>
    /// <exception cref="ArgumentException">If <paramref name="detail"/> is empty.</exception>
    public BootMilestone(Cycle at, BootMilestoneKind kind, Addr pc, string detail, GuestOsId? os = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        this.At = at;
        this.Kind = kind;
        this.Pc = pc;
        this.Detail = detail;
        this.Os = os;
    }

    /// <summary>Gets the machine cycle when the milestone was observed.</summary>
    /// <value>The cycle stamp.</value>
    public Cycle At { get; }

    /// <summary>Gets which boot point this is.</summary>
    /// <value>The milestone kind.</value>
    public BootMilestoneKind Kind { get; }

    /// <summary>Gets the program counter at the milestone.</summary>
    /// <value>The guest address.</value>
    public Addr Pc { get; }

    /// <summary>Gets the operating system this milestone belongs to.</summary>
    /// <value><see langword="null"/> for firmware and for a loader that is not yet classified.</value>
    public GuestOsId? Os { get; }

    /// <summary>Gets what the monitor saw.</summary>
    /// <value>
    /// A non-empty detail string. <see cref="BootMilestoneKind.BootFirmwareEntered"/> names the slot and the card,
    /// for example "slot 6: Disk II" or "slot 7: SmartPort".
    /// <see cref="BootMilestoneKind.CustomBootLoaderEntered"/> includes the first bytes of the boot sector.
    /// Later milestones name the OS step, for example "DOS 3.3 Boot 2 at $B700" or "MLI $C8 OPEN".
    /// </value>
    public string Detail { get; }
}