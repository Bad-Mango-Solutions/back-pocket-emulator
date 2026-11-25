// <copyright file="IClockSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Host or fixed time shared by a clock card and the slotless ProDOS clock.
/// </summary>
/// <remarks>
/// <para>
/// A null <c>timezoneOffset</c> uses <see cref="TimeProvider.GetLocalNow"/>, including daylight
/// saving time. A set offset, including zero, is UTC plus that many minutes and does not follow
/// daylight saving time. Frozen time ignores the offset.
/// </para>
/// </remarks>
public interface IClockSource
{
    /// <summary>
    /// Gets the current time as seen by the emulated system.
    /// </summary>
    /// <value>
    /// The current <see cref="DateTime"/>, either from the host clock or the fixed time when
    /// <see cref="UseHostTime"/> is <see langword="false"/>.
    /// </value>
    DateTime CurrentTime { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to use host time or a fixed time.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to use the host clock;
    /// <see langword="false"/> to use the time set by <see cref="SetFixedTime"/>.
    /// </value>
    bool UseHostTime { get; set; }

    /// <summary>
    /// Sets a fixed time for <see cref="CurrentTime"/>.
    /// </summary>
    /// <param name="time">The fixed time to return.</param>
    /// <remarks>
    /// Setting a fixed time sets <see cref="UseHostTime"/> to <see langword="false"/>.
    /// </remarks>
    void SetFixedTime(DateTime time);
}