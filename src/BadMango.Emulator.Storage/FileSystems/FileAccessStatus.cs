// <copyright file="FileAccessStatus.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Outcome of a read-only directory list or file read.
/// </summary>
/// <remarks>
/// These are normal results, not exceptions. A missing path, a ProDOS storage type
/// the reader will not decode, or a catalog loop comes back as one of these values.
/// Failures of the underlying <see cref="IBlockReader"/> or <see cref="ISectorReader"/>
/// propagate and are not rewritten into a status.
/// </remarks>
public enum FileAccessStatus
{
    /// <summary>The list or read completed.</summary>
    Success = 0,

    /// <summary>The path does not name a file or directory on the volume.</summary>
    NotFound = 1,

    /// <summary>The path names a file. <see cref="IAppleFileSystem.ListDirectory"/> cannot list it.</summary>
    NotADirectory = 2,

    /// <summary>The path names a directory. <see cref="IAppleFileSystem.TryReadFile"/> cannot read it as bytes.</summary>
    IsDirectory = 3,

    /// <summary>
    /// The entry exists but its storage is not decoded. ProDOS types <c>$5</c> (extended)
    /// and <c>$4</c> (Pascal area) use this. The result contains no invented bytes.
    /// </summary>
    UnsupportedStorage = 4,

    /// <summary>
    /// The on-disk structure is not traversable. A DOS track/sector list that loops,
    /// or a file shorter than its A/I/B header, uses this.
    /// </summary>
    CorruptStructure = 5,

    /// <summary>The path contains an empty segment, <c>.</c>, or <c>..</c>.</summary>
    InvalidPath = 6,
}