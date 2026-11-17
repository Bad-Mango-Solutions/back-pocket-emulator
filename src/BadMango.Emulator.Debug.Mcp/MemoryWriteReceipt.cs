// <copyright file="MemoryWriteReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// The payload for a successful <c>write_memory</c>.
/// </summary>
/// <param name="Address">The address of the first byte.</param>
/// <param name="Length">The number of bytes written.</param>
public sealed record MemoryWriteReceipt(uint Address, int Length);