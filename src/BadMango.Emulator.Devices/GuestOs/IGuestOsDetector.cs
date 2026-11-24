// <copyright file="IGuestOsDetector.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// On-demand guest-OS detection over a side-effect-free memory reader.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Detect"/> reads memory when something asks (the debugger, a state load,
/// or the boot monitor). It does not register execution traps and it is not called
/// from the instruction loop. When guest-OS detection is off, the host does not
/// register this service, so the hot loop costs nothing.
/// </para>
/// <para>
/// <see cref="Changed"/> is raised once for each transition. A second <see cref="Detect"/>
/// that reports the same <see cref="GuestOsInfo"/> does not raise it again.
/// </para>
/// </remarks>
public interface IGuestOsDetector
{
    /// <summary>
    /// Raised when <see cref="Detect"/> publishes a <see cref="GuestOsInfo"/> that differs from <see cref="Current"/>.
    /// </summary>
    event EventHandler<GuestOsChangedEventArgs>? Changed;

    /// <summary>Gets the last detection, or a not-yet-run <see cref="GuestOsId.None"/> before the first call.</summary>
    /// <value>The current guest OS.</value>
    GuestOsInfo Current { get; }

    /// <summary>
    /// Reads guest memory and publishes the recognized operating system.
    /// </summary>
    /// <returns>The same value <see cref="Current"/> has after this call.</returns>
    /// <exception cref="InvalidOperationException">If a probe returns an attempt that disagrees with its kind.</exception>
    GuestOsInfo Detect();
}