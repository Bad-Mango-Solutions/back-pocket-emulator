// <copyright file="PowerOnMemoryProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Configuration;

using System.Text.Json.Serialization;

/// <summary>
/// Configures the memory pattern applied at power-on.
/// </summary>
public sealed class PowerOnMemoryProfile
{
    /// <summary>
    /// Gets or sets the pattern name.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "apple-iie";

    /// <summary>
    /// Gets or sets the optional deterministic seed.
    /// </summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}