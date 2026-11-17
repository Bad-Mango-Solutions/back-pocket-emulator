// <copyright file="DebugMcpOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// Configuration for the debug MCP host.
/// </summary>
/// <remarks>
/// Bind this type to the <c>DebugMcp</c> configuration section. Part 3 uses
/// <see cref="SandboxRoots"/> to confine disk mount and read-file paths.
/// </remarks>
public sealed class DebugMcpOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "DebugMcp";

    /// <summary>
    /// Gets or sets the absolute directories sandboxed file tools may resolve under.
    /// </summary>
    /// <remarks>
    /// An empty list denies sandboxed file access. Each entry must be an absolute path.
    /// </remarks>
    public List<string> SandboxRoots { get; set; } = [];
}