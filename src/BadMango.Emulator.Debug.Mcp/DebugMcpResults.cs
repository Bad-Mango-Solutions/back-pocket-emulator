// <copyright file="DebugMcpResults.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.Text.Json;

using BadMango.Emulator.Debug.Abstractions;

using ModelContextProtocol.Protocol;

/// <summary>
/// Builds tool results whose structured content is an emudbg envelope.
/// </summary>
internal static class DebugMcpResults
{
    /// <summary>
    /// Builds an error result. The text content and the structured content are the same envelope.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="faults">Formatted bus faults, when the failure is a bus fault.</param>
    /// <returns>A tool result with <see cref="CallToolResult.IsError"/> set.</returns>
    public static CallToolResult Error(string message, IReadOnlyList<MemoryFaultLine>? faults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        DebugEnvelope<DebugMcpError> envelope = DebugEnvelope<DebugMcpError>.Create(new DebugMcpError(message, faults));
        JsonElement structured = JsonSerializer.SerializeToElement(envelope, DebugMcpJson.SerializerOptions);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = structured,
            Content =
            [
                new TextContentBlock { Text = structured.GetRawText() },
            ],
        };
    }
}