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
    /// <exception cref="ArgumentException">If <paramref name="detail"/> is empty.</exception>
    public BootMilestone(Cycle at, BootMilestoneKind kind, Addr pc, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        this.At = at;
        this.Kind = kind;
        this.Pc = pc;
        this.Detail = detail;
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

    /// <summary>Gets what the monitor saw.</summary>
    /// <value>A non-empty detail string.</value>
    public string Detail { get; }
}