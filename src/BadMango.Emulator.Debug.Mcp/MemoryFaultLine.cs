// <copyright file="MemoryFaultLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// One bus fault reported by a memory tool.
/// </summary>
/// <param name="Address">The faulting address.</param>
/// <param name="Kind">The fault kind from <c>DebugOutputRenderer.FormatFault</c> or <c>FormatFaultBits</c>.</param>
public sealed record MemoryFaultLine(uint Address, string Kind);