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
/// <see cref="Logical"/> is <see cref="Raw"/> after the DOS header is removed and the
/// payload is trimmed. <see cref="Dos33Layout.LogicalHeaderLength"/> is 4 for a binary
/// file, 2 for Applesoft or Integer BASIC, and 0 otherwise. For those three types the
/// length kept is <see cref="Dos33Layout.LogicalPayloadLength"/>: the minimum of the
/// header's length word and the bytes that remain after the header. Sector padding past
/// that length is dropped. A DOS file shorter than its header is
/// <see cref="FileAccessStatus.CorruptStructure"/>. ProDOS and Pascal logical reads are
/// identical to raw reads.
/// </para>
/// </remarks>
public enum FileReadView
{
    /// <summary>On-disk bytes trimmed to end-of-file, including DOS A/I/B headers.</summary>
    Raw = 0,

    /// <summary>Raw bytes with the DOS A/I/B header removed and the payload trimmed to its length word.</summary>
    Logical = 1,
}