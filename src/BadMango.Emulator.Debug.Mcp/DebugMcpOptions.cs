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
    /// The default maximum number of bytes <c>disk_read_file</c> returns.
    /// </summary>
    public const int DefaultMaxDiskFileBytes = 1024 * 1024;

    /// <summary>
    /// Gets or sets the absolute directories sandboxed file tools may resolve under.
    /// </summary>
    /// <remarks>
    /// An empty list denies sandboxed file access. Each entry must be an absolute path.
    /// A relative tool path is resolved against the first entry.
    /// </remarks>
    public List<string> SandboxRoots { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of bytes <c>disk_read_file</c> will return.
    /// </summary>
    /// <remarks>
    /// A larger file returns an error envelope. The default is 1 MiB.
    /// </remarks>
    public int MaxDiskFileBytes { get; set; } = DefaultMaxDiskFileBytes;
}