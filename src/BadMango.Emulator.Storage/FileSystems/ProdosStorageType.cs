// <copyright file="ProdosStorageType.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS storage-type nibble, the high four bits of a directory entry's first byte.
/// </summary>
public enum ProdosStorageType : byte
{
    /// <summary>Deleted or unused entry. Value <c>$0</c>.</summary>
    Deleted = 0x00,

    /// <summary>Seedling file. Value <c>$1</c>.</summary>
    Seedling = 0x01,

    /// <summary>Sapling file. Value <c>$2</c>.</summary>
    Sapling = 0x02,

    /// <summary>Tree file. Value <c>$3</c>.</summary>
    Tree = 0x03,

    /// <summary>Pascal area. Not a readable file. Value <c>$4</c>.</summary>
    PascalArea = 0x04,

    /// <summary>GS/OS extended file. Not a readable file. Value <c>$5</c>.</summary>
    Extended = 0x05,

    /// <summary>Subdirectory entry in its parent. Value <c>$D</c>.</summary>
    Subdirectory = 0x0D,

    /// <summary>Header inside a subdirectory key block. Not a file. Value <c>$E</c>.</summary>
    SubdirectoryHeader = 0x0E,

    /// <summary>Volume directory header. Not a file. Value <c>$F</c>.</summary>
    VolumeHeader = 0x0F,
}