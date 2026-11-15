// <copyright file="IDisplaySwitchLog.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// Emulator-thread log of display-switch changes during a frame.
/// </summary>
/// <remarks>
/// Declaration only. The implementation, and the devices that call it, arrive with the
/// hot-side capture work. Nothing in v1 of this step installs a log.
/// </remarks>
public interface IDisplaySwitchLog
{
    /// <summary>
    /// Appends one switch change. Emulator thread only.
    /// </summary>
    /// <param name="which">The switch that changed.</param>
    /// <param name="value">The value just written.</param>
    /// <remarks>
    /// No allocation and no lock. Only an actual change is recorded; writing the same
    /// value again is not logged. The call is one bounds check and one store into the
    /// back slot's preallocated array.
    /// </remarks>
    void Record(DisplaySwitch which, bool value);
}