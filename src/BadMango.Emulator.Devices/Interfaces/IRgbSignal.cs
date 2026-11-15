// <copyright file="IRgbSignal.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Digital RGB video signal. The output has already mapped hardware colors to pixels.
/// </summary>
/// <remarks>
/// Pixels are finished BGRA8888 with alpha <c>0xFF</c>. Memory taken from
/// <see cref="Pixels"/> is valid only until the lease that produced the signal is disposed.
/// Width, height, and stride stay <see cref="int"/>, as specified.
/// </remarks>
public interface IRgbSignal : IVideoSignal
{
    /// <summary>
    /// Gets the frame width in pixels.
    /// </summary>
    /// <value>The number of pixels in one row.</value>
    int Width { get; }

    /// <summary>
    /// Gets the frame height in pixels.
    /// </summary>
    /// <value>The number of rows.</value>
    int Height { get; }

    /// <summary>
    /// Gets the row stride in pixels.
    /// </summary>
    /// <value>The number of pixels from one row to the next.</value>
    int Stride { get; }

    /// <summary>
    /// Gets the finished BGRA8888 pixels. The output has already applied its palette.
    /// </summary>
    /// <value>The pixel buffer. Valid only while the producing lease is held.</value>
    ReadOnlyMemory<uint> Pixels { get; }
}