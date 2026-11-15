// <copyright file="VideoFrameInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Identity and geometry of a leased BGRA8888 frame. Pixels are always BGRA8888.
/// </summary>
/// <param name="Sequence">The frame sequence. Published frames start at 1; 0 means none yet.</param>
/// <param name="Cycle">The machine cycle the frame was captured at.</param>
/// <param name="Width">The frame width in pixels.</param>
/// <param name="Height">The frame height in pixels.</param>
/// <param name="Stride">The row stride in pixels.</param>
public readonly record struct VideoFrameInfo(ulong Sequence, ulong Cycle, int Width, int Height, int Stride);