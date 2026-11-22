// <copyright file="AppleFileSystemPathStyle.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// How <see cref="AppleFileSystemPaths.TryParse"/> splits a catalog path.
/// </summary>
public enum AppleFileSystemPathStyle
{
    /// <summary>
    /// ProDOS. <c>/</c> separates the volume prefix and each directory segment.
    /// </summary>
    Hierarchical = 0,

    /// <summary>
    /// DOS 3.3 and Apple Pascal. One optional leading <c>/</c> is removed and the
    /// rest of the text is a single file name, which may itself contain <c>/</c>.
    /// </summary>
    Flat = 1,
}