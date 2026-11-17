// <copyright file="MemoryWriteException.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Bus;

/// <summary>
/// A debug memory write stopped on a bus fault.
/// </summary>
public sealed class MemoryWriteException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryWriteException"/> class.
    /// </summary>
    /// <param name="address">The address that faulted.</param>
    /// <param name="fault">The bus fault returned by the write.</param>
    public MemoryWriteException(uint address, BusFault fault)
        : base($"Failed to write memory at ${address:X8}: {DebugOutputRenderer.FormatFault(fault)}.")
    {
        this.Address = address;
        this.Kind = DebugOutputRenderer.FormatFault(fault);
    }

    /// <summary>
    /// Gets the address that faulted.
    /// </summary>
    public uint Address { get; }

    /// <summary>
    /// Gets the fault formatted with <see cref="DebugOutputRenderer.FormatFault"/>.
    /// </summary>
    public string Kind { get; }
}