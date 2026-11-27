// <copyright file="HostMonitorChoice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// The display choice for one window. Where the choice is decoded is issue #738.
/// </summary>
/// <remarks>
/// Composite accepts Mono, Green, Amber, and NTSC. RGB accepts only <see cref="Rgb"/>.
/// Two subscriptions on one composite source may hold different choices. The choice is
/// per window. It does not change the machine.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HostMonitorChoice>))]
public enum HostMonitorChoice
{
    /// <summary>
    /// Greyscale composite monitor.
    /// </summary>
    Mono,

    /// <summary>
    /// Green composite monitor.
    /// </summary>
    Green,

    /// <summary>
    /// Amber composite monitor.
    /// </summary>
    Amber,

    /// <summary>
    /// NTSC composite monitor.
    /// </summary>
    Ntsc,

    /// <summary>
    /// RGB pass-through. Legal only for an RGB output.
    /// </summary>
    Rgb,
}