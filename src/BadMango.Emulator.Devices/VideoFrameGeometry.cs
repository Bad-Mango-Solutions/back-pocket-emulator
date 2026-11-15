// <copyright file="VideoFrameGeometry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Pixel geometry a frame provider writes. Stride is in pixels, not bytes.
/// </summary>
/// <param name="Width">The number of pixels in one row.</param>
/// <param name="Height">The number of rows.</param>
/// <param name="Stride">The number of pixels from one row to the next.</param>
public readonly record struct VideoFrameGeometry(int Width, int Height, int Stride);