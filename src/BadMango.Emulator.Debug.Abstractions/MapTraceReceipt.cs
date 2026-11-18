// <copyright file="MapTraceReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// The <c>maptrace</c> result: whether trace is on, the optional range, and the ring.
/// </summary>
/// <param name="Enabled"><see langword="true"/> when remap and slow-path logging is on.</param>
/// <param name="RangeStart">The first address of the range, or <see langword="null"/> when the range is empty.</param>
/// <param name="RangeEnd">The last address of the range, or <see langword="null"/> when the range is empty.</param>
/// <param name="Records">The ring in chronological order.</param>
public sealed record MapTraceReceipt(
    bool Enabled,
    uint? RangeStart,
    uint? RangeEnd,
    IReadOnlyList<MapTraceLine> Records);