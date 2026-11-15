// <copyright file="OpcodeEntry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

/// <summary>
/// Represents a single opcode dispatch entry in the CPU opcode table.
/// </summary>
/// <param name="fn">The cached dispatch delegate used by the call site.</param>
/// <param name="legacy">The legacy delegate implementation for the opcode.</param>
/// <param name="metadata">The opcode metadata used by analysis and disassembly paths.</param>
/// <remarks>
/// <see cref="Fn"/> is an <see cref="OpcodeDispatch"/>. The call site is always
/// <c>entry.Fn(cpu, in entry)</c>. Unconverted opcodes share one trampoline that
/// reads <see cref="Legacy"/> from that entry. A converted opcode is a static method
/// with the same signature, cached as its own static readonly delegate, that does
/// not read the entry.
/// </remarks>
public readonly struct OpcodeEntry(OpcodeDispatch fn, OpcodeHandler? legacy, OpcodeInfo metadata)
{
    /// <summary>
    /// Gets the dispatch delegate used at dispatch time.
    /// </summary>
    public OpcodeDispatch Fn { get; } = fn;

    /// <summary>
    /// Gets the legacy delegate implementation for this opcode.
    /// </summary>
    /// <value>
    /// The delegate invoked by the shared trampoline, or <see langword="null"/> when the
    /// slot has no handler.
    /// </value>
    public OpcodeHandler? Legacy { get; } = legacy;

    /// <summary>
    /// Gets metadata associated with this opcode.
    /// </summary>
    public OpcodeInfo Metadata { get; } = metadata;
}