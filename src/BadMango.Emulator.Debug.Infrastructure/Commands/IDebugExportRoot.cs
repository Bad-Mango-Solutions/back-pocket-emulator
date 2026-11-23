// <copyright file="IDebugExportRoot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

/// <summary>
/// The only directory <c>disk read --to</c> may write into.
/// </summary>
public interface IDebugExportRoot
{
    /// <summary>
    /// Gets the output root, or <see langword="null"/> when export is not configured.
    /// </summary>
    /// <value>An absolute or relative directory. <c>--to</c> refuses paths outside it.</value>
    string? Directory { get; }
}