// <copyright file="DebugMcpLimits.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// Bounds for inspection tool arguments.
/// </summary>
public static class DebugMcpLimits
{
    /// <summary>
    /// The default number of bytes for <c>read_memory</c>.
    /// </summary>
    public const int DefaultReadLength = 16;

    /// <summary>
    /// The maximum number of bytes <c>read_memory</c> will return.
    /// </summary>
    public const int MaxReadLength = 4096;

    /// <summary>
    /// The maximum number of bytes <c>write_memory</c> will accept.
    /// </summary>
    public const int MaxWriteLength = 256;

    /// <summary>
    /// The default number of instructions for <c>disassemble</c>.
    /// </summary>
    public const int DefaultDisassemblyCount = 16;

    /// <summary>
    /// The maximum number of instructions <c>disassemble</c> will return.
    /// </summary>
    public const int MaxDisassemblyCount = 256;

    /// <summary>
    /// The maximum length of a breakpoint or watchpoint label.
    /// </summary>
    public const int MaxLabelLength = 256;
}