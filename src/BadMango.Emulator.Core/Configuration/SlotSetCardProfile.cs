// <copyright file="SlotSetCardProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Defines a slot-card entry used by a named slot set.
/// </summary>
public sealed class SlotSetCardProfile
{
    /// <summary>
    /// Gets or sets the slot number (1-7).
    /// </summary>
    [JsonPropertyName("slot")]
    public required int Slot { get; set; }

    /// <summary>
    /// Gets or sets the card type identifier, or <see langword="null"/> to clear the slot.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the preset configuration name.
    /// </summary>
    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    /// <summary>
    /// Gets or sets card-specific configuration.
    /// </summary>
    [JsonPropertyName("config")]
    public JsonElement? Config { get; set; }
}