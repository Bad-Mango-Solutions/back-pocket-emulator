// <copyright file="IDebugOutputRenderer.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Writes one command result as either human text or an <c>emudbg/v1</c> JSON envelope.
/// </summary>
public interface IDebugOutputRenderer
{
    /// <summary>
    /// Gets a value indicating whether results are written as JSON envelopes.
    /// </summary>
    bool JsonOutput { get; }

    /// <summary>
    /// Gets the process <c>--max-cycles</c> budget, when one was supplied.
    /// </summary>
    ulong? DefaultMaxCycles { get; }

    /// <summary>
    /// Writes a register snapshot.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="snapshot">The registers to write.</param>
    void WriteRegisters(TextWriter output, RegisterSnapshot snapshot);

    /// <summary>
    /// Writes a memory block.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="block">The bytes to write.</param>
    void WriteMemory(TextWriter output, MemoryBlock block);

    /// <summary>
    /// Writes disassembly lines.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="lines">The lines to write.</param>
    void WriteDisassembly(TextWriter output, IReadOnlyList<DisassemblyLine> lines);

    /// <summary>
    /// Writes a run result.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="result">The run that finished.</param>
    void WriteRun(TextWriter output, RunResult result);

    /// <summary>
    /// Writes a step receipt.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="receipt">The step that finished.</param>
    void WriteStep(TextWriter output, StepReceipt receipt);

    /// <summary>
    /// Writes the current breakpoints.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="breakpoints">The breakpoints to write.</param>
    /// <param name="text">The human-readable listing used when JSON is off.</param>
    void WriteBreakpoints(TextWriter output, IReadOnlyList<Breakpoint> breakpoints, string text);

    /// <summary>
    /// Writes one breakpoint.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="breakpoint">The breakpoint to write.</param>
    /// <param name="text">The human-readable line used when JSON is off.</param>
    void WriteBreakpoint(TextWriter output, Breakpoint breakpoint, string text);

    /// <summary>
    /// Writes the current watchpoints.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="watchpoints">The watchpoints to write.</param>
    /// <param name="text">The human-readable listing used when JSON is off.</param>
    void WriteWatchpoints(TextWriter output, IReadOnlyList<Watchpoint> watchpoints, string text);

    /// <summary>
    /// Writes one watchpoint.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="watchpoint">The watchpoint to write.</param>
    /// <param name="text">The human-readable line used when JSON is off.</param>
    void WriteWatchpoint(TextWriter output, Watchpoint watchpoint, string text);

    /// <summary>
    /// Writes a reset receipt.
    /// </summary>
    /// <param name="output">The writer that receives the result.</param>
    /// <param name="receipt">The reset that finished.</param>
    /// <param name="text">The human-readable lines used when JSON is off.</param>
    void WriteReset(TextWriter output, ResetReceipt receipt, string text);
}