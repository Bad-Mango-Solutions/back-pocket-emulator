// <copyright file="RgbOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Options for an RGB monitor. The property initializer is the exact v1 default.
/// </summary>
public sealed record RgbOptions : FrameProviderOptions
{
    /// <summary>
    /// Gets the brightness scale applied to an otherwise finished RGB frame.
    /// </summary>
    /// <value><c>1.0</c> is an exact pass-through.</value>
    public float Brightness { get; init; } = 1.0f;
}