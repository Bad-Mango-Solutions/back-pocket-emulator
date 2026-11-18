// <copyright file="IMappingTraceRing.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Read-only view of the fixed mapping-trace ring.
/// </summary>
/// <remarks>
/// The debugger reads this the same way it reads <see cref="IBusFaultRing"/>.
/// Appends do not allocate. The CPU fast path does not touch the ring.
/// </remarks>
public interface IMappingTraceRing
{
    /// <summary>
    /// Gets the number of records the buffer retains.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets how many records are currently stored, never more than <see cref="Capacity"/>.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets how many records have been appended, including ones overwritten by wrap.
    /// </summary>
    ulong TotalRecords { get; }

    /// <summary>
    /// Copies the buffer in chronological order, oldest first.
    /// </summary>
    /// <returns>A new array. Empty when the ring is empty.</returns>
    MappingTraceRecord[] Snapshot();

    /// <summary>
    /// Discards the buffered records and zeroes <see cref="TotalRecords"/>.
    /// </summary>
    void Clear();
}