// <copyright file="OpcodeEntry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

/// <summary>
/// Represents a single opcode dispatch entry in the CPU opcode table.
/// </summary>
/// <param name="fn">The cached dispatch delegate used by the call site.</param>
/// <param name="metadata">The opcode metadata used by analysis and disassembly paths.</param>
/// <remarks>
/// <see cref="Fn"/> is an <see cref="OpcodeDispatch"/>. The call site is always
/// <c>entry.Fn(cpu, in entry)</c>. Each opcode stores its own static readonly delegate.
/// A handler may ignore the entry argument.
/// </remarks>
public readonly struct OpcodeEntry(OpcodeDispatch fn, OpcodeInfo metadata)
{
    /// <summary>
    /// Gets the dispatch delegate used at dispatch time.
    /// </summary>
    public OpcodeDispatch Fn { get; } = fn;

    /// <summary>
    /// Gets metadata associated with this opcode.
    /// </summary>
    public OpcodeInfo Metadata { get; } = metadata;
}