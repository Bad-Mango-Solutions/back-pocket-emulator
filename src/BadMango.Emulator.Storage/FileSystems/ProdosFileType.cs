// <copyright file="ProdosFileType.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS file-type byte stored in a directory entry. This is not the storage-type nibble.
/// </summary>
public enum ProdosFileType : byte
{
    /// <summary>Text. Value <c>$04</c>.</summary>
    Text = 0x04,

    /// <summary>Binary. Value <c>$06</c>.</summary>
    Binary = 0x06,

    /// <summary>Directory. Value <c>$0F</c>.</summary>
    Directory = 0x0F,

    /// <summary>Applesoft BASIC. Value <c>$FC</c>.</summary>
    Applesoft = 0xFC,

    /// <summary>ProDOS system file. Value <c>$FF</c>.</summary>
    Sys = 0xFF,
}