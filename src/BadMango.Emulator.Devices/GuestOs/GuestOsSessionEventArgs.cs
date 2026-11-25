// <copyright file="GuestOsSessionEventArgs.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// A guest OS session started or ended.
/// </summary>
public sealed class GuestOsSessionEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsSessionEventArgs"/> class.
    /// </summary>
    /// <param name="id">The operating system whose session started or ended.</param>
    /// <param name="stage">The boot stage at the moment the event is raised.</param>
    public GuestOsSessionEventArgs(GuestOsId id, GuestOsBootStage stage)
    {
        this.Id = id;
        this.Stage = stage;
    }

    /// <summary>Gets the operating system whose session started or ended.</summary>
    /// <value>The guest OS id. For <see cref="IBootSequenceMonitor.GuestOsSessionEnded"/> this is the OS that was running.</value>
    public GuestOsId Id { get; }

    /// <summary>Gets the boot stage when the event was raised.</summary>
    /// <value>
    /// <see cref="GuestOsBootStage.Loader"/> or <see cref="GuestOsBootStage.Ready"/> when a session starts.
    /// <see cref="GuestOsBootStage.Init"/> when a cold start or a user reboot ends one.
    /// </value>
    public GuestOsBootStage Stage { get; }
}