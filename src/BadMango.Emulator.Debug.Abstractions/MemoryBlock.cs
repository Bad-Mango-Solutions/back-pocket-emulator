// <copyright file="MemoryBlock.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json.Serialization;

/// <summary>
/// A contiguous memory block returned from the debug session.
/// </summary>
/// <param name="Address">The starting address of the block.</param>
/// <param name="Length">The number of bytes in the block.</param>
/// <param name="Data">
/// The raw byte payload, serialized as an uppercase hex string.
/// A byte whose <paramref name="Faults"/> entry is non-zero is a placeholder 0, not memory contents.
/// </param>
/// <param name="Label">An optional display label.</param>
/// <param name="Faults">
/// Parallel mask, one byte per <paramref name="Data"/> byte, serialized as an uppercase hex string
/// of the same length. Omitted when every byte was readable.
/// <c>00</c> means the data byte is valid. Any non-zero value is a bus fault mask; the session stores
/// the low byte of the fault bitmask into this byte array, so only the
/// low-byte bits <c>0x01</c> Unmapped, <c>0x02</c> Permission, <c>0x04</c> Nx, <c>0x08</c>
/// Misaligned, and <c>0x10</c> DeviceFault are representable here. High-byte fault bits are not
/// representable in the single-byte memory fault mask and are intentionally dropped from the serialized
/// byte output. The session fills this from the bus fault result and does not throw once per unreadable
/// byte.
/// </param>
public sealed record MemoryBlock(
    uint Address,
    int Length,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte> Data,
    string? Label = null,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte>? Faults = null);