// <copyright file="PascalLayout.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Apple Pascal directory offsets the reader and the synthetic fixtures share.
/// </summary>
/// <remarks>
/// <para>
/// Blocks 0 and 1 are boot blocks. The directory is <see cref="DirectoryBytes"/> bytes
/// beginning at block <see cref="DirectoryStartBlock"/> and covering
/// <see cref="DirectoryBlockCount"/> blocks. Entries are <see cref="EntryLength"/> bytes
/// and are packed across block boundaries. Entry 0 is the volume header and is not a file.
/// Up to <see cref="MaxFiles"/> file entries follow. Words are little-endian.
/// </para>
/// <para>
/// A Pascal reader constructor rejects a volume whose first directory entry fails
/// <see cref="IsVolumeHeader"/>. That keeps an arbitrary non-ProDOS block image from
/// being reported as Pascal.
/// </para>
/// </remarks>
public static class PascalLayout
{
    /// <summary>Bytes in one Pascal block.</summary>
    public const int BlockSize = 512;

    /// <summary>First directory block.</summary>
    public const int DirectoryStartBlock = 2;

    /// <summary>Directory blocks. The directory is not aligned to a single block.</summary>
    public const int DirectoryBlockCount = 4;

    /// <summary>Bytes in the directory (four blocks).</summary>
    public const int DirectoryBytes = 2048;

    /// <summary>Bytes in one directory record, including the volume header.</summary>
    public const int EntryLength = 26;

    /// <summary>File entries that fit after the volume header.</summary>
    public const int MaxFiles = 77;

    /// <summary>First block after a standard directory. The volume header stores this value.</summary>
    public const int FirstDataBlock = 6;

    /// <summary>Maximum characters in a volume name, not counting the length byte.</summary>
    public const int VolumeNameCapacity = 7;

    /// <summary>Maximum characters in a file name, not counting the length byte.</summary>
    public const int FileNameCapacity = 15;

    /// <summary>Offset of the length-prefixed volume name in the header.</summary>
    public const int VolumeNameOffset = 6;

    /// <summary>Offset of the length-prefixed file name in a file entry.</summary>
    public const int FileNameOffset = 6;

    /// <summary>Bytes reserved for a file name, including the length byte.</summary>
    public const int FileNameFieldLength = 16;

    /// <summary>Header offset of the little-endian volume block count.</summary>
    public const int VolumeBlocksOffset = 0x0E;

    /// <summary>Header offset of the little-endian file count.</summary>
    public const int FileCountOffset = 0x10;

    /// <summary>File-entry offset of the little-endian bytes used in the last block.</summary>
    public const int FileLastBlockBytesOffset = 0x16;

    /// <summary>Volume-header kind, and the untyped file kind.</summary>
    public const byte KindUntyped = 0;

    /// <summary>Bad-block file kind.</summary>
    public const byte KindBad = 1;

    /// <summary>Code file kind.</summary>
    public const byte KindCode = 2;

    /// <summary>Text file kind.</summary>
    public const byte KindText = 3;

    /// <summary>Info file kind.</summary>
    public const byte KindInfo = 4;

    /// <summary>Data file kind.</summary>
    public const byte KindData = 5;

    /// <summary>Graf file kind.</summary>
    public const byte KindGraf = 6;

    /// <summary>Foto file kind.</summary>
    public const byte KindFoto = 7;

    /// <summary>Secure-directory file kind.</summary>
    public const byte KindSecure = 8;

    /// <summary>
    /// Returns the logical length of a Pascal file.
    /// </summary>
    /// <param name="startBlock">First block of the file.</param>
    /// <param name="endBlock">First block after the file.</param>
    /// <param name="bytesInLastBlock">Bytes used in the last block. A multiple of 512 is stored as 512, not 0.</param>
    /// <returns>Zero when <paramref name="endBlock"/> is not past <paramref name="startBlock"/>, otherwise the byte length.</returns>
    public static int FileLength(int startBlock, int endBlock, int bytesInLastBlock)
    {
        if (endBlock <= startBlock)
        {
            return 0;
        }

        return checked(((endBlock - startBlock - 1) * BlockSize) + bytesInLastBlock);
    }

    /// <summary>
    /// Returns whether the directory begins with an Apple Pascal volume header.
    /// </summary>
    /// <param name="directory">The start of the directory. A buffer shorter than one entry is not a header.</param>
    /// <param name="blockCount">Blocks the media exposes.</param>
    /// <returns>
    /// <see langword="true"/> when the first block is 0, the directory ends at block
    /// <see cref="FirstDataBlock"/>, the kind is 0, the name is 1–7 printable characters,
    /// the recorded block count matches the image, and the file count is at most <see cref="MaxFiles"/>.
    /// </returns>
    public static bool IsVolumeHeader(ReadOnlySpan<byte> directory, int blockCount)
    {
        if (directory.Length < EntryLength || blockCount < FirstDataBlock)
        {
            return false;
        }

        var first = ReadU16(directory, 0);
        var next = ReadU16(directory, 2);
        var kind = ReadU16(directory, 4);
        if (first != 0 || next != FirstDataBlock || kind != KindUntyped)
        {
            return false;
        }

        var nameLength = directory[VolumeNameOffset];
        if (nameLength is < 1 or > VolumeNameCapacity)
        {
            return false;
        }

        for (var i = 0; i < nameLength; i++)
        {
            var c = directory[VolumeNameOffset + 1 + i];
            if (c is < 0x21 or > 0x7E)
            {
                return false;
            }
        }

        var blocks = ReadU16(directory, VolumeBlocksOffset);
        var fileCount = ReadU16(directory, FileCountOffset);
        return blocks == blockCount && fileCount <= MaxFiles;
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset) => data[offset] | (data[offset + 1] << 8);
}