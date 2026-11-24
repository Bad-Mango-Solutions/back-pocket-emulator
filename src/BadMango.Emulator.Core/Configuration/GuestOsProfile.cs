// <copyright file="GuestOsProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json.Serialization;

/// <summary>
/// Guest-OS detection and boot-log switches. Both are off unless the profile says otherwise.
/// </summary>
public sealed class GuestOsProfile
{
    /// <summary>
    /// Gets or sets a value indicating whether on-demand guest-OS detection is installed.
    /// </summary>
    /// <remarks>Off leaves the detector unregistered. Detection reads memory only when asked.</remarks>
    [JsonPropertyName("detect")]
    public bool Detect { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the boot-sequence monitor is installed.
    /// </summary>
    /// <remarks>
    /// Off registers no boot traps. On arms observe-only execution traps and records milestones.
    /// The debug console <c>os on</c> command can also turn the monitor on.
    /// </remarks>
    [JsonPropertyName("bootLog")]
    public bool BootLog { get; set; }
}