// <copyright file="ProdosFileType.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS file-type byte stored in a directory entry. This is not the storage-type nibble.
/// </summary>
public enum ProdosFileType : byte
{
    /// <summary>Bad blocks. Value <c>$01</c>.</summary>
    Bad = 0x01,

    /// <summary>Text. Value <c>$04</c>.</summary>
    Text = 0x04,

    /// <summary>Binary. Value <c>$06</c>.</summary>
    Binary = 0x06,

    /// <summary>Directory. Value <c>$0F</c>.</summary>
    Directory = 0x0F,

    /// <summary>AppleWorks database. Value <c>$19</c>.</summary>
    AppleWorksDatabase = 0x19,

    /// <summary>AppleWorks word processor. Value <c>$1A</c>.</summary>
    AppleWorksWordProcessor = 0x1A,

    /// <summary>AppleWorks spreadsheet. Value <c>$1B</c>.</summary>
    AppleWorksSpreadsheet = 0x1B,

    /// <summary>GS/OS system file. Value <c>$B3</c>.</summary>
    GsOsSystem = 0xB3,

    /// <summary>Pascal area. Value <c>$EF</c>.</summary>
    Pascal = 0xEF,

    /// <summary>ProDOS command file. Value <c>$F0</c>.</summary>
    Command = 0xF0,

    /// <summary>Integer BASIC. Value <c>$FA</c>.</summary>
    Integer = 0xFA,

    /// <summary>Integer BASIC variables. Value <c>$FB</c>.</summary>
    IntegerVariables = 0xFB,

    /// <summary>Applesoft BASIC. Value <c>$FC</c>.</summary>
    Applesoft = 0xFC,

    /// <summary>Applesoft variables. Value <c>$FD</c>.</summary>
    ApplesoftVariables = 0xFD,

    /// <summary>Relocatable object. Value <c>$FE</c>.</summary>
    Relocatable = 0xFE,

    /// <summary>ProDOS system file. Value <c>$FF</c>.</summary>
    Sys = 0xFF,
}