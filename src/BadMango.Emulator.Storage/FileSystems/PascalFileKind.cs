// <copyright file="PascalFileKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Apple Pascal file kind, bits 0–3 of the directory kind word.
/// </summary>
/// <remarks>
/// Bit 15 of that word is <see cref="PascalLayout.FilerWildcardBit"/> and is not part of the kind.
/// </remarks>
public enum PascalFileKind : byte
{
    /// <summary>Untyped, and the volume-header kind. Value 0.</summary>
    Untyped = 0,

    /// <summary>Bad blocks. Value 1.</summary>
    Bad = 1,

    /// <summary>Code file. Value 2.</summary>
    Code = 2,

    /// <summary>Text file. Value 3.</summary>
    Text = 3,

    /// <summary>Info file. Value 4.</summary>
    Info = 4,

    /// <summary>Data file. Value 5.</summary>
    Data = 5,

    /// <summary>Graf file. Value 6.</summary>
    Graf = 6,

    /// <summary>Foto file. Value 7.</summary>
    Foto = 7,

    /// <summary>Secure directory. Value 8.</summary>
    Secure = 8,
}