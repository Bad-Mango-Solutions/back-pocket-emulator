// <copyright file="GuestOsChangedEventArgs.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// The guest OS reported by <see cref="IGuestOsDetector"/> changed.
/// </summary>
public sealed class GuestOsChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsChangedEventArgs"/> class.
    /// </summary>
    /// <param name="previous">The value of <see cref="IGuestOsDetector.Current"/> before this detection.</param>
    /// <param name="current">The value <see cref="IGuestOsDetector.Detect"/> just published.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="previous"/> or <paramref name="current"/> is <see langword="null"/>.</exception>
    public GuestOsChangedEventArgs(GuestOsInfo previous, GuestOsInfo current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        this.Previous = previous;
        this.Current = current;
    }

    /// <summary>Gets the guest OS that was current before this detection.</summary>
    /// <value>The previous info.</value>
    public GuestOsInfo Previous { get; }

    /// <summary>Gets the guest OS this detection published.</summary>
    /// <value>The new info. <see cref="IGuestOsDetector.Current"/> is already this value when the event is raised.</value>
    public GuestOsInfo Current { get; }
}