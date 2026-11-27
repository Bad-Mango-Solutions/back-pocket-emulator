// <copyright file="HostCompositeSignal.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A raw composite signal a client can decode. This is the in-process value, not the #738 wire encoding.
/// </summary>
/// <param name="Sequence">The frame sequence. Sequences start at 1 in the video pipeline. The loopback stores the value it is given.</param>
/// <param name="Cycle">The machine cycle the signal was captured at.</param>
/// <param name="Width">The dot width. The pipeline's composite output is 560.</param>
/// <param name="Height">The line count. The pipeline's composite output is 192.</param>
/// <param name="Dots">Packed dots. The publisher must not mutate the buffer after publish.</param>
/// <param name="LineFlags">One flag byte per line. The publisher must not mutate the buffer after publish.</param>
/// <remarks>
/// Issue #738 defines the shared-memory and network encoding of <c>Composite560Signal</c>.
/// Until that lands, a host publishes this value and the loopback hands the same instance back.
/// </remarks>
public sealed record HostCompositeSignal(
    ulong Sequence,
    ulong Cycle,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Dots,
    ReadOnlyMemory<byte> LineFlags);