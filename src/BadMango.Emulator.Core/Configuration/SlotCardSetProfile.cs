// <copyright file="SlotCardSetProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json.Serialization;

/// <summary>
/// Defines a named slot-card set in a machine profile.
/// </summary>
public sealed class SlotCardSetProfile
{
    /// <summary>
    /// Gets or sets an optional human-readable description for this set.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the slot card overrides for this set.
    /// </summary>
    /// <remarks>
    /// Entries are overlaid on top of <c>devices.slots.cards</c>. Set an entry's
    /// <c>type</c> to <see langword="null"/> to clear that slot for this set.
    /// </remarks>
    [JsonPropertyName("cards")]
    public List<SlotSetCardProfile>? Cards { get; set; }
}