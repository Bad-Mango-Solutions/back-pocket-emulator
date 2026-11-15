// <copyright file="OpcodeTable.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

using System.Runtime.CompilerServices;

using Interfaces.Cpu;

/// <summary>
/// Represents an opcode lookup table for 6502-family CPUs.
/// </summary>
/// <remarks>
/// This class encapsulates the opcode table, providing O(1) opcode dispatch.
/// The table maps 8-bit opcodes (0x00-0xFF) to instruction handlers.
/// Handlers receive the CPU instance for accessing state and memory through the bus.
/// </remarks>
public class OpcodeTable
{
    private readonly OpcodeHandler[] handlers;
    private readonly OpcodeInfo[] metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpcodeTable"/> class.
    /// </summary>
    /// <param name="handlers">The array of opcode handlers. Must be exactly 256 elements.</param>
    /// <param name="metadata">The per-opcode metadata array. Must be exactly 256 elements.</param>
    /// <remarks>
    /// The metadata array is copied. Later changes to the caller's array do not
    /// affect the table. The table is built once, so the copy is not on the dispatch path.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when handlers or metadata is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when handlers or metadata arrays are not exactly 256 elements.
    /// </exception>
    public OpcodeTable(OpcodeHandler[] handlers, OpcodeInfo[] metadata)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(metadata);

        if (handlers.Length != 256)
        {
            throw new ArgumentException("Opcode table must have exactly 256 entries.", nameof(handlers));
        }

        if (metadata.Length != 256)
        {
            throw new ArgumentException("Opcode metadata table must have exactly 256 entries.", nameof(metadata));
        }

        this.handlers = handlers;
        this.metadata = metadata.ToArray();
    }

    /// <summary>
    /// Executes the handler for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to execute (0x00-0xFF).</param>
    /// <param name="cpu">The CPU instance providing state and memory access.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(byte opcode, ICpu cpu)
    {
        handlers[opcode](cpu);
    }

    /// <summary>
    /// Gets the handler for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to get the handler for (0x00-0xFF).</param>
    /// <returns>The handler for the specified opcode.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpcodeHandler GetHandler(byte opcode)
    {
        return handlers[opcode];
    }

    /// <summary>
    /// Gets metadata for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to get metadata for (0x00-0xFF).</param>
    /// <returns>The metadata for the specified opcode.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpcodeInfo GetMetadata(byte opcode)
    {
        return metadata[opcode];
    }
}