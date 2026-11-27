// <copyright file="IPowerOnReset.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Drops host-side state on a power-on (hard) reset.
/// </summary>
/// <remarks>
/// Soft RESET notifies <see cref="IResetReceiver"/> and leaves physically held keys alone.
/// A power reset also calls <see cref="PowerOnReset"/> so an input layer forgets keys it still
/// considers held, matching <see cref="IPeripheral.Reset"/> on the device.
/// </remarks>
public interface IPowerOnReset
{
    /// <summary>
    /// Returns the component to its power-on state.
    /// </summary>
    void PowerOnReset();
}