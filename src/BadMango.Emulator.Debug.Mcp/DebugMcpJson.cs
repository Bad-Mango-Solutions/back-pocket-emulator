// <copyright file="DebugMcpJson.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON settings that keep MCP tool results in the emudbg envelope shape.
/// </summary>
public static class DebugMcpJson
{
    /// <summary>
    /// Gets the serializer used for tool arguments and envelope-shaped results.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerOptions.Default)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}