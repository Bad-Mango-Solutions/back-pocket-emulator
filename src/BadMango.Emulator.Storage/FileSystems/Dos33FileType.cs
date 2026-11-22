// <copyright file="Dos33FileType.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// DOS 3.3 catalog file type, without the lock bit.
/// </summary>
/// <remarks>
/// The on-disk type byte is this value with <see cref="Dos33Layout.LockedMask"/> set when the
/// file is locked. <see cref="Dos33Layout.FileTypeMask"/> isolates the type.
/// </remarks>
public enum Dos33FileType : byte
{
    /// <summary>Text, catalog letter T. Value <c>$00</c>.</summary>
    Text = 0x00,

    /// <summary>Integer BASIC, catalog letter I. Value <c>$01</c>.</summary>
    Integer = 0x01,

    /// <summary>Applesoft BASIC, catalog letter A. Value <c>$02</c>.</summary>
    Applesoft = 0x02,

    /// <summary>Binary, catalog letter B. Value <c>$04</c>.</summary>
    Binary = 0x04,

    /// <summary>Type S. Value <c>$08</c>.</summary>
    S = 0x08,

    /// <summary>Relocatable, catalog letter R. Value <c>$10</c>.</summary>
    Relocatable = 0x10,

    /// <summary>Additional Applesoft, the second catalog letter A. Value <c>$20</c>.</summary>
    AdditionalApplesoft = 0x20,

    /// <summary>Additional binary, the second catalog letter B. Value <c>$40</c>.</summary>
    AdditionalBinary = 0x40,
}