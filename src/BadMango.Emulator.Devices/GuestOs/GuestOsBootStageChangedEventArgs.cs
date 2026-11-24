// <copyright file="GuestOsBootStageChangedEventArgs.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// The boot monitor moved <see cref="IBootSequenceMonitor.Stage"/>.
/// </summary>
public sealed class GuestOsBootStageChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsBootStageChangedEventArgs"/> class.
    /// </summary>
    /// <param name="previous">The stage before the change.</param>
    /// <param name="current">The stage after the change.</param>
    public GuestOsBootStageChangedEventArgs(GuestOsBootStage previous, GuestOsBootStage current)
    {
        this.Previous = previous;
        this.Current = current;
    }

    /// <summary>Gets the stage before the change.</summary>
    /// <value>The previous stage.</value>
    public GuestOsBootStage Previous { get; }

    /// <summary>Gets the stage after the change.</summary>
    /// <value>The new stage.</value>
    public GuestOsBootStage Current { get; }
}