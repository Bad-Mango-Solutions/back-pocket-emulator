// <copyright file="FrameProviderOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Options shared by every frame provider. Value equality lets a hub share one provided
/// frame between consumers whose options compare equal.
/// </summary>
/// <remarks>
/// Reserved for monitor-fidelity work. v1 accepts only the exact defaults; non-exact
/// values are a later step. This step declares the records and their defaults only.
/// </remarks>
public abstract record FrameProviderOptions
{
    /// <summary>
    /// Gets a value indicating whether cosmetic scanline gaps are drawn.
    /// </summary>
    /// <value><see langword="false"/> is the exact default.</value>
    public bool ScanlineGaps { get; init; }
}