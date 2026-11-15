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
/// <param name="Data">The raw byte payload; serialized as an uppercase hex string.</param>
/// <param name="Label">An optional display label.</param>
public sealed record MemoryBlock(uint Address, int Length, [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte> Data, string? Label = null);