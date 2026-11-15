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
/// <remarks>
/// <para>
/// <see cref="Fn"/> is <c>delegate* managed&lt;ICpu, in OpcodeEntry, void&gt;</c>.
/// The call site is always <c>entry.Fn(cpu, in entry)</c>. Unconverted opcodes share one
/// trampoline that reads <see cref="Legacy"/> from that entry. A converted opcode is a
/// static method with the same signature that does not read the entry.
/// </para>
/// <para>
/// The extra argument is the entry the caller already has, passed by readonly reference.
/// Passing <see cref="OpcodeHandler"/> instead would make every call, including a converted
/// opcode, load and pass the delegate. The managed calling convention is spelled out;
/// C# function pointers are managed unless <c>unmanaged</c> is written.
/// </para>
/// </remarks>
public readonly unsafe struct OpcodeEntry(
    delegate* managed<ICpu, in OpcodeEntry, void> fn,
    OpcodeHandler? legacy,
    OpcodeInfo metadata)
{
    /// <summary>
    /// Gets the function pointer used at dispatch time.
    /// </summary>
    public delegate* managed<ICpu, in OpcodeEntry, void> Fn { get; } = fn;

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