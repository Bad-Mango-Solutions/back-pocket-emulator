// <copyright file="FileStorageKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// How a <see cref="FileEntry"/>'s bytes are stored on the volume.
/// </summary>
/// <remarks>
/// <para>
/// The file-type byte and the storage kind are different fields. DOS keeps the
/// catalog type (T/I/A/B) separate from the track/sector list. ProDOS keeps the
/// file type (<see cref="ProdosFileType"/>) separate from the
/// <see cref="ProdosStorageType"/> nibble. Pascal's <see cref="PascalFileKind"/> is
/// the file type; its bytes are one contiguous run of blocks.
/// </para>
/// <para>
/// <see cref="ProdosExtended"/> and <see cref="ProdosPascalArea"/> are recognized
/// catalog entries. <see cref="IAppleFileSystem.TryReadFile"/> returns
/// <see cref="FileAccessStatus.UnsupportedStorage"/> for them and does not invent bytes.
/// </para>
/// </remarks>
public enum FileStorageKind
{
    /// <summary>No storage, or a header/deleted slot that is not a file.</summary>
    None = 0,

    /// <summary>DOS 3.3 track/sector list. Sparse pairs are holes, not the end of the list.</summary>
    DosTrackSectorList = 1,

    /// <summary>ProDOS seedling. The key block is the only data block.</summary>
    ProdosSeedling = 2,

    /// <summary>ProDOS sapling. The key block is a low-half/high-half index.</summary>
    ProdosSapling = 3,

    /// <summary>ProDOS tree. The key block is a master index of sapling indexes.</summary>
    ProdosTree = 4,

    /// <summary>ProDOS subdirectory (storage type <c>$D</c>). The key block is the subdirectory.</summary>
    ProdosSubdirectory = 5,

    /// <summary>ProDOS/GS/OS extended file (storage type <c>$5</c>). Not readable.</summary>
    ProdosExtended = 6,

    /// <summary>ProDOS Pascal area (storage type <c>$4</c>). Not readable.</summary>
    ProdosPascalArea = 7,

    /// <summary>Apple Pascal file stored as a contiguous block run.</summary>
    PascalContiguous = 8,
}