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
/// <see cref="DirectoryBlockCount"/> blocks. Records are <see cref="EntryLength"/> bytes
/// and are packed across block boundaries. Entry 0 is the volume header and is not a file.
/// Up to <see cref="MaxFiles"/> file entries follow. Words are little-endian.
/// </para>
/// <para>
/// The Pascal probe calls <see cref="IsVolumeHeader"/> and returns
/// <see cref="FileSystemProbeAttempt.Reject"/> when it is false. The probe does not catch.
/// The constructor throws <see cref="ArgumentException"/> only as a guard for a caller that
/// skips the check. A block-read <see cref="ArgumentOutOfRangeException"/> is not a rejection.
/// The recorded block count must equal the image size. A larger container is not a Pascal
/// volume, unlike a ProDOS image whose <c>total_blocks</c> may be smaller than the file.
/// The file kind is <see cref="KindMask"/> of the kind word. Bit 15
/// (<see cref="FilerWildcardBit"/>) is the Filer wildcard flag and is not part of the kind.
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

    /// <summary>Mask for bits 0–3 of the file-kind word.</summary>
    public const int KindMask = 0x000F;

    /// <summary>Bit 15 of the file-kind word. The Filer uses it as a wildcard flag.</summary>
    public const int FilerWildcardBit = 0x8000;

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
    /// Returns the file kind from a directory kind word.
    /// </summary>
    /// <param name="kindWord">The 16-bit kind field. Bit 15 is <see cref="FilerWildcardBit"/>.</param>
    /// <returns>Bits 0–3.</returns>
    public static int FileKind(int kindWord) => kindWord & KindMask;

    /// <summary>
    /// Returns whether the directory begins with an Apple Pascal volume header.
    /// </summary>
    /// <param name="directory">The start of the directory. A buffer shorter than one entry is not a header.</param>
    /// <param name="blockCount">Blocks the media exposes.</param>
    /// <returns>
    /// <see langword="true"/> when the first block is 0, the directory ends at block
    /// <see cref="FirstDataBlock"/>, the kind is <see cref="PascalFileKind.Untyped"/>, the name
    /// is 1–7 printable characters, the recorded block count equals <paramref name="blockCount"/>,
    /// and the file count is at most <see cref="MaxFiles"/>. Equality with the image is required:
    /// a Pascal header that names a different size is not this volume. ProDOS allows
    /// <c>total_blocks</c> to be smaller than the image; Pascal does not.
    /// </returns>
    public static bool IsVolumeHeader(ReadOnlySpan<byte> directory, int blockCount)
    {
        if (directory.Length < EntryLength || blockCount < FirstDataBlock)
        {
            return false;
        }

        var first = ReadU16(directory, VolumeHeader.FirstBlockOffset);
        var next = ReadU16(directory, VolumeHeader.NextBlockOffset);
        var kind = ReadU16(directory, VolumeHeader.KindOffset);
        if (first != 0 || next != FirstDataBlock || kind != (int)PascalFileKind.Untyped)
        {
            return false;
        }

        var nameLength = directory[VolumeHeader.NameOffset];
        if (nameLength is < 1 or > VolumeHeader.NameCapacity)
        {
            return false;
        }

        for (var i = 0; i < nameLength; i++)
        {
            var c = directory[VolumeHeader.NameOffset + 1 + i];
            if (c is < 0x21 or > 0x7E)
            {
                return false;
            }
        }

        var blocks = ReadU16(directory, VolumeHeader.BlocksOffset);
        var fileCount = ReadU16(directory, VolumeHeader.FileCountOffset);
        return blocks == blockCount && fileCount <= MaxFiles;
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset) => data[offset] | (data[offset + 1] << 8);

    /// <summary>The first 26-byte directory record, which names the volume.</summary>
    public static class VolumeHeader
    {
        /// <summary>Offset of the first block word. It is always 0.</summary>
        public const int FirstBlockOffset = 0;

        /// <summary>Offset of the next-block word. A standard directory stores 6.</summary>
        public const int NextBlockOffset = 2;

        /// <summary>Offset of the kind word. The volume header stores <see cref="PascalFileKind.Untyped"/>.</summary>
        public const int KindOffset = 4;

        /// <summary>Offset of the length-prefixed volume name.</summary>
        public const int NameOffset = 6;

        /// <summary>Maximum characters in a volume name, not counting the length byte.</summary>
        public const int NameCapacity = 7;

        /// <summary>Offset of the little-endian volume block count.</summary>
        public const int BlocksOffset = 0x0E;

        /// <summary>Offset of the little-endian file count.</summary>
        public const int FileCountOffset = 0x10;
    }

    /// <summary>One 26-byte file record after the volume header.</summary>
    public static class DirectoryEntry
    {
        /// <summary>Offset of the first-block word.</summary>
        public const int FirstBlockOffset = 0;

        /// <summary>Offset of the exclusive end-block word.</summary>
        public const int NextBlockOffset = 2;

        /// <summary>Offset of the kind word. Bits 0–3 are <see cref="PascalFileKind"/>.</summary>
        public const int KindOffset = 4;

        /// <summary>Offset of the length-prefixed file name.</summary>
        public const int NameOffset = 6;

        /// <summary>Maximum characters in a file name, not counting the length byte.</summary>
        public const int NameCapacity = 15;

        /// <summary>Bytes reserved for a file name, including the length byte.</summary>
        public const int NameFieldLength = 16;

        /// <summary>Offset of the little-endian bytes used in the last block.</summary>
        public const int LastBlockBytesOffset = 0x16;
    }
}