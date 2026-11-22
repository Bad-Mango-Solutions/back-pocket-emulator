// <copyright file="FileReadView.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Which bytes <see cref="IAppleFileSystem.TryReadFile"/> returns.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Raw"/> is the on-disk file trimmed to end-of-file, including a DOS
/// A/I/B header and including sparse holes as zeros.
/// </para>
/// <para>
/// <see cref="Logical"/> is <see cref="Raw"/> after DOS header stripping.
/// <see cref="Dos33Layout.LogicalHeaderLength"/> is the number of leading bytes removed:
/// 4 for a binary file, 2 for Applesoft or Integer BASIC, and 0 for every other DOS type.
/// ProDOS and Pascal logical reads are identical to raw reads. Stripping does not
/// interpret the length word inside a DOS header and does not drop trailing sector padding.
/// A DOS file shorter than its header is <see cref="FileAccessStatus.CorruptStructure"/>.
/// </para>
/// </remarks>
public enum FileReadView
{
    /// <summary>On-disk bytes trimmed to end-of-file, including DOS A/I/B headers.</summary>
    Raw = 0,

    /// <summary>Raw bytes with the DOS A/I/B header removed when the file type has one.</summary>
    Logical = 1,
}