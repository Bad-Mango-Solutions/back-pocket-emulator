// <copyright file="HostBgraFrame.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A finished BGRA8888 frame. This is not a lease on <c>VideoFrame</c> and it does not reference the video device.
/// </summary>
/// <param name="Sequence">The frame sequence.</param>
/// <param name="Cycle">The machine cycle the frame was captured at.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="StrideBytes">The row stride in bytes. At least <paramref name="Width"/> times 4.</param>
/// <param name="Bgra">BGRA8888 pixels. The publisher must not mutate the buffer after publish.</param>
/// <remarks>
/// RGB outputs always publish this and do not publish a raw signal. Composite outputs may publish
/// this as the host's default decode, in addition to <see cref="HostCompositeSignal"/>. Issue #738
/// decides where decode runs and how the bytes are copied onto a wire. The loopback does not copy.
/// </remarks>
public sealed record HostBgraFrame(
    ulong Sequence,
    ulong Cycle,
    int Width,
    int Height,
    int StrideBytes,
    ReadOnlyMemory<byte> Bgra);