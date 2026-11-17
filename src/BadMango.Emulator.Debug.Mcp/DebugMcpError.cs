// <copyright file="DebugMcpError.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// The payload of an emudbg envelope returned when a tool call fails.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="Faults">Bus faults formatted with the shared fault-kind formatter, when the failure is a bus fault.</param>
public sealed record DebugMcpError(string Message, IReadOnlyList<MemoryFaultLine>? Faults = null);