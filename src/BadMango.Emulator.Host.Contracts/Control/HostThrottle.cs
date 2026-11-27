// <copyright file="HostThrottle.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// The speed throttle shared by the machine chrome and the instance details.
/// </summary>
/// <remarks>
/// This sets the host throttle. It is not the measured speed. Half, Normal, and Double
/// scale the machine's nominal clock. <see cref="Max"/> disables throttling, which is a
/// target clock of 0 Hz.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HostThrottle>))]
public enum HostThrottle
{
    /// <summary>
    /// Half the nominal clock.
    /// </summary>
    Half,

    /// <summary>
    /// The nominal clock.
    /// </summary>
    Normal,

    /// <summary>
    /// Twice the nominal clock.
    /// </summary>
    Double,

    /// <summary>
    /// Run as fast as the host allows. The effective clock is 0.
    /// </summary>
    Max,
}