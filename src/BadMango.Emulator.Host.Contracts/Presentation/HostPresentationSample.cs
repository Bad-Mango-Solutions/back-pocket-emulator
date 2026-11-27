// <copyright file="HostPresentationSample.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The latest presentation sample for one machine. Latest-wins: a slow client does not block the publisher.
/// </summary>
/// <param name="Kind">The output kind. Must match the machine.</param>
/// <param name="Sequence">The sample sequence. Latest is the last publish, not the greatest sequence.</param>
/// <param name="Cycle">The machine cycle.</param>
/// <param name="Frame">
/// Finished BGRA. Required for <see cref="HostVideoKind.Rgb"/>. Optional for composite when the host also decoded a default frame.
/// </param>
/// <param name="Signal">
/// The raw composite signal. Present only for <see cref="HostVideoKind.Composite"/>, and only when the client may decode.
/// </param>
public sealed record HostPresentationSample(
    HostVideoKind Kind,
    ulong Sequence,
    ulong Cycle,
    HostBgraFrame? Frame,
    HostCompositeSignal? Signal);