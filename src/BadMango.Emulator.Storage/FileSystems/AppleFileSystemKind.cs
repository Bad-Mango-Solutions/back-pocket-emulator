// <copyright file="AppleFileSystemKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// On-disk file system a read-only reader recognizes.
/// </summary>
/// <remarks>
/// This is the volume type <see cref="IAppleFileSystem.Kind"/> reports.
/// Detection order is <see cref="AppleFileSystemDetector.RecommendedProbeOrder"/>.
/// </remarks>
public enum AppleFileSystemKind
{
    /// <summary>Apple DOS 3.3 catalog and track/sector lists.</summary>
    Dos33 = 1,

    /// <summary>ProDOS volume directory, seedlings, saplings, trees, and subdirectories.</summary>
    Prodos = 2,

    /// <summary>Apple Pascal (UCSD) directory of 26-byte entries.</summary>
    Pascal = 3,
}