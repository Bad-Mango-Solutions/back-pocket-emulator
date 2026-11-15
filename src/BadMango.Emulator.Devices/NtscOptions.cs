// <copyright file="NtscOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Options for an NTSC monitor. The property initializers are the exact v1 defaults.
/// </summary>
public sealed record NtscOptions : FrameProviderOptions
{
    /// <summary>
    /// Gets the NTSC decoder.
    /// </summary>
    /// <value>The exact default is <see cref="NtscDecoder.FourDotWindow"/>.</value>
    public NtscDecoder Decoder { get; init; } = NtscDecoder.FourDotWindow;

    /// <summary>
    /// Gets how many lines the color killer lags behind a burst change.
    /// </summary>
    /// <value>Zero is the exact default.</value>
    public int ColorKillerLagLines { get; init; }
}