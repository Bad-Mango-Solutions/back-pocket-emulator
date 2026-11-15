// <copyright file="OpcodeEntry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

using Interfaces.Cpu;

/// <summary>
/// Represents a single opcode dispatch entry in the CPU opcode table.
/// </summary>
/// <param name="fn">The function pointer used by the dispatch call site.</param>
/// <param name="legacy">The legacy delegate implementation for the opcode.</param>
/// <param name="metadata">The opcode metadata used by analysis and disassembly paths.</param>
public readonly unsafe struct OpcodeEntry(delegate*<ICpu, void> fn, OpcodeHandler? legacy, OpcodeInfo metadata)
{
    /// <summary>
    /// Gets the function pointer used at dispatch time.
    /// </summary>
    public delegate*<ICpu, void> Fn { get; } = fn;

    /// <summary>
    /// Gets the legacy delegate implementation for this opcode.
    /// </summary>
    public OpcodeHandler? Legacy { get; } = legacy;

    /// <summary>
    /// Gets metadata associated with this opcode.
    /// </summary>
    public OpcodeInfo Metadata { get; } = metadata;
}