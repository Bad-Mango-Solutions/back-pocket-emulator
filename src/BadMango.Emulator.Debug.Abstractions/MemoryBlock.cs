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
/// <c>00</c> means the data byte is valid. Any non-zero value is a bus fault mask whose low byte uses
/// bit flags: <c>0x0001</c> Unmapped, <c>0x0002</c> Permission, <c>0x0004</c> Nx,
/// <c>0x0008</c> Misaligned, <c>0x0010</c> DeviceFault.
/// The session fills this from the bus fault result and does not throw once per unreadable byte.
/// </param>
public sealed record MemoryBlock(
    uint Address,
    int Length,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte> Data,
    string? Label = null,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte>? Faults = null);