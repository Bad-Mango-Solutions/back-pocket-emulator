// <copyright file="MemoryReadReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.Text.Json.Serialization;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// The payload for <c>read_memory</c>.
/// </summary>
/// <param name="Address">The starting address.</param>
/// <param name="Length">The number of bytes.</param>
/// <param name="Data">The bytes, as an uppercase hex string. A faulted byte is 0.</param>
/// <param name="Faults">
/// The low byte of each fault mask, as an uppercase hex string of the same length as <paramref name="Data"/>.
/// Omitted when every byte was readable.
/// </param>
/// <param name="ReportedFaults">Faults formatted with the shared <c>FaultKind</c> bitmask formatter.</param>
public sealed record MemoryReadReceipt(
    uint Address,
    int Length,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte> Data,
    [property: JsonConverter(typeof(HexBytesJsonConverter))] IReadOnlyList<byte>? Faults,
    IReadOnlyList<MemoryFaultLine> ReportedFaults);