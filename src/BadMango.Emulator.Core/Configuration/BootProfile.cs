// <copyright file="BootProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json.Serialization;

/// <summary>
/// Defines boot configuration for a machine profile.
/// </summary>
/// <remarks>
/// <para>
/// The boot section controls how the machine starts up, including whether
/// to automatically start the machine after loading and which slot to boot from.
/// </para>
/// </remarks>
public sealed class BootProfile
{
    /// <summary>
    /// Gets or sets the power-on memory configuration.
    /// </summary>
    [JsonPropertyName("powerOnMemory")]
    public PowerOnMemoryProfile? PowerOnMemory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically start the machine after loading.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/>, the machine begins execution immediately after
    /// configuration is complete. Defaults to <see langword="false"/>.
    /// </remarks>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    /// <summary>
    /// Gets or sets the slot to boot from.
    /// </summary>
    /// <remarks>
    /// Retained so an existing document that still contains <c>startupSlot</c> deserializes.
    /// Profile loading, machine startup, and boot do not read it.
    /// Disk autostart is guest ROM code reached through the reset vector.
    /// </remarks>
    [JsonPropertyName("startupSlot")]
    public int? StartupSlot { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically open the video window on boot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see langword="true"/>, the video display window is opened automatically
    /// when the machine boots. This provides an immediate visual display of the
    /// emulated system.
    /// </para>
    /// <para>
    /// Defaults to <see langword="false"/>. The video window can always be opened
    /// manually using the <c>video open</c> REPL command.
    /// </para>
    /// </remarks>
    [JsonPropertyName("autoVideoWindowOpen")]
    public bool AutoVideoWindowOpen { get; set; }
}