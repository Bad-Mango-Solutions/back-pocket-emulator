// <copyright file="GuestOsProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json.Serialization;

/// <summary>
/// Guest-OS detection, boot-log, and OS-call-log switches. All are off unless the profile says otherwise.
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

    /// <summary>
    /// Gets or sets which OS calls are logged: <c>off</c>, <c>mli</c>, <c>dos</c>, or <c>all</c>.
    /// </summary>
    /// <remarks>
    /// <c>off</c> is the default and installs no call log. Any other accepted value also installs
    /// the boot monitor, because the log reads that monitor's observe-only hooks.
    /// <c>on</c> is accepted and means <c>all</c>.
    /// </remarks>
    [JsonPropertyName("callLog")]
    public string CallLog { get; set; } = "off";
}