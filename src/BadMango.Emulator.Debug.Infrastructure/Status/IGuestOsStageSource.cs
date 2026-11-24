// <copyright file="IGuestOsStageSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Status;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Supplies the boot stage shown by the status monitor.
/// </summary>
/// <remarks>
/// <para>
/// The boot monitor (#685) owns traps and milestone logging. This contract is only the
/// stage value the status row reads, so that row can be built before the monitor lands.
/// Nothing here arms a trap or names a card.
/// </para>
/// <para>
/// <see cref="NoneGuestOsStageSource"/> is the no-op. Its stage is <see cref="GuestOsBootStage.None"/>.
/// When a host does not inject a source, the status row uses
/// <see cref="IBootSequenceMonitor.Stage"/> when that monitor is on the machine.
/// With neither a source nor a monitor, a detected operating system is
/// <see cref="GuestOsBootStage.Ready"/>; otherwise the stage is <see cref="GuestOsBootStage.None"/>.
/// </para>
/// </remarks>
public interface IGuestOsStageSource
{
    /// <summary>Gets the boot stage to display.</summary>
    /// <value><see cref="GuestOsBootStage.None"/> before any boot firmware is entered.</value>
    GuestOsBootStage Stage { get; }
}