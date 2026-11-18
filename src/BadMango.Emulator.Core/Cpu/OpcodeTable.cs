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
/// <para>
/// This class encapsulates the opcode table, providing O(1) opcode dispatch.
/// The table maps 8-bit opcodes (0x00-0xFF) to instruction handlers.
/// Handlers receive the CPU instance for accessing state and memory through the bus.
/// </para>
/// <para>
/// Each slot is an <see cref="OpcodeEntry"/>. <see cref="Execute"/> always calls
/// <see cref="OpcodeEntry.Fn"/> and passes that same entry by readonly reference.
/// Every slot stores its own <see cref="OpcodeDispatch"/> delegate. Nothing is stored
/// in a mutable static field.
/// </para>
/// </remarks>
public class OpcodeTable
{
    private readonly OpcodeEntry[] entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpcodeTable"/> class.
    /// </summary>
    /// <param name="dispatchTargets">The per-opcode dispatch delegates. Must be exactly 256 elements, none null.</param>
    /// <param name="metadata">The per-opcode metadata array. Must be exactly 256 elements.</param>
    /// <remarks>
    /// The metadata array is copied into each entry. Later changes to the caller's array do not
    /// affect the table. The table is built once, so the copy is not on the dispatch path.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when dispatch targets or metadata is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when either array is not exactly 256 elements, or when a dispatch slot is null.
    /// </exception>
    public OpcodeTable(OpcodeDispatch[] dispatchTargets, OpcodeInfo[] metadata)
    {
        ArgumentNullException.ThrowIfNull(dispatchTargets);
        ArgumentNullException.ThrowIfNull(metadata);

        if (dispatchTargets.Length != 256)
        {
            throw new ArgumentException("Opcode dispatch table must have exactly 256 entries.", nameof(dispatchTargets));
        }

        if (metadata.Length != 256)
        {
            throw new ArgumentException("Opcode metadata table must have exactly 256 entries.", nameof(metadata));
        }

        entries = new OpcodeEntry[256];
        for (int i = 0; i < dispatchTargets.Length; i++)
        {
            if (dispatchTargets[i] is null)
            {
                throw new ArgumentException("Every opcode slot requires a dispatch delegate.", nameof(dispatchTargets));
            }

            entries[i] = new(dispatchTargets[i], metadata[i]);
        }
    }

    /// <summary>
    /// Executes the handler for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to execute (0x00-0xFF).</param>
    /// <param name="cpu">The CPU instance providing state and memory access.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Execute(byte opcode, ICpu cpu)
    {
        ref readonly OpcodeEntry entry = ref entries[opcode];
        entry.Fn(cpu, in entry);
    }

    /// <summary>
    /// Gets the dispatch delegate for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to get the delegate for (0x00-0xFF).</param>
    /// <returns>The cached dispatch delegate for the specified opcode.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpcodeDispatch GetDispatch(byte opcode)
    {
        return entries[opcode].Fn;
    }

    /// <summary>
    /// Gets metadata for the specified opcode.
    /// </summary>
    /// <param name="opcode">The opcode to get metadata for (0x00-0xFF).</param>
    /// <returns>The metadata for the specified opcode.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OpcodeInfo GetMetadata(byte opcode)
    {
        return entries[opcode].Metadata;
    }
}