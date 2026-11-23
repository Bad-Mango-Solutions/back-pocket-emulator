// <copyright file="DebugExportRoot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

/// <summary>
/// An <see cref="IDebugExportRoot"/> taken from emudbg startup options.
/// </summary>
public sealed class DebugExportRoot : IDebugExportRoot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DebugExportRoot"/> class.
    /// </summary>
    /// <param name="directory">The output root, or <see langword="null"/> when <c>--export-root</c> was omitted.</param>
    public DebugExportRoot(string? directory)
    {
        this.Directory = directory;
    }

    /// <inheritdoc/>
    public string? Directory { get; }
}