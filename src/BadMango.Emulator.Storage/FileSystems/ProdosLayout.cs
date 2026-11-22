// <copyright file="ProdosLayout.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS directory, index, and bitmap offsets the reader and the synthetic fixtures share.
/// </summary>
/// <remarks>
/// <para>
/// The volume key block is block <see cref="VolumeKeyBlock"/>. Its first entry, at offset
/// <see cref="KeyBlockHeaderOffset"/>, is the volume header (storage type
/// <see cref="StorageVolumeHeader"/>). That header is not a file. A subdirectory key block
/// starts with storage type <see cref="StorageSubdirectoryHeader"/>, which is also not a file.
/// A subdirectory file in its parent directory is storage type <see cref="StorageSubdirectory"/>.
/// </para>
/// <para>
/// Index blocks are not adjacent byte pairs. <see cref="ReadIndexPointer"/> reads byte
/// <c>i</c> from the low half and byte <c>256 + i</c> from the high half. A zero pointer is
/// a sparse hole. Readers still emit zeros and stop at the EOF field, not at the first hole.
/// Storage types <see cref="StorageExtended"/> and <see cref="StoragePascalArea"/> are
/// <see cref="FileAccessStatus.UnsupportedStorage"/>. An extended file's key block holds a
/// data-fork mini-entry at <see cref="ExtendedDataForkOffset"/> and a resource-fork
/// mini-entry at <see cref="ExtendedResourceForkOffset"/>. Readers do not return those
/// fork bytes as the file.
/// </para>
/// <para>
/// The volume bitmap stores one bit per block, and a set bit means free. Block 0 is
/// bit 7 of byte 0. <see cref="IsBlockFree"/> is that mapping.
/// </para>
/// <para>
/// Offsets named on the volume header and the subdirectory header are entry-relative.
/// Add <see cref="KeyBlockHeaderOffset"/> to reach them in the key block. File-entry offsets
/// are also entry-relative and differ from the header after the name field.
/// </para>
/// </remarks>
public static class ProdosLayout
{
    /// <summary>Bytes in one ProDOS block.</summary>
    public const int BlockSize = 512;

    /// <summary>Block that holds the volume directory key block.</summary>
    public const int VolumeKeyBlock = 2;

    /// <summary>Bytes in one directory entry, including the header.</summary>
    public const int EntryLength = 0x27;

    /// <summary>Directory entries that fit in one block.</summary>
    public const int EntriesPerBlock = 0x0D;

    /// <summary>Offset of the first entry in a directory block. The four bytes before it are the block links.</summary>
    public const int KeyBlockHeaderOffset = 4;

    /// <summary>Maximum characters in a ProDOS name.</summary>
    public const int NameCapacity = 15;

    /// <summary>Entry offset of the name bytes.</summary>
    public const int NameOffset = 0x01;

    /// <summary>File-entry offset of the ProDOS file type.</summary>
    public const int FileTypeOffset = 0x10;

    /// <summary>File-entry offset of the little-endian key pointer.</summary>
    public const int KeyPointerOffset = 0x11;

    /// <summary>File-entry offset of the little-endian blocks-used count.</summary>
    public const int BlocksUsedOffset = 0x13;

    /// <summary>File-entry offset of the 24-bit end-of-file.</summary>
    public const int EofOffset = 0x15;

    /// <summary>Entry offset of the access byte on both headers and file entries.</summary>
    public const int AccessOffset = 0x1E;

    /// <summary>File-entry offset of the little-endian auxiliary type.</summary>
    public const int AuxTypeOffset = 0x1F;

    /// <summary>File-entry offset of the directory key block that contains the entry.</summary>
    public const int HeaderPointerOffset = 0x25;

    /// <summary>Access bit that allows writing. Clear means locked.</summary>
    public const byte WriteAccessBit = 0x02;

    /// <summary>Typical unlocked access: destroy, rename, write, and read.</summary>
    public const byte AccessUnlocked = 0xC3;

    /// <summary>Locked access: destroy, rename, and read, with the write bit clear.</summary>
    public const byte AccessLocked = 0xC1;

    /// <summary>Storage type of a deleted or unused entry.</summary>
    public const byte StorageDeleted = 0x00;

    /// <summary>Storage type of a seedling file.</summary>
    public const byte StorageSeedling = 0x01;

    /// <summary>Storage type of a sapling file.</summary>
    public const byte StorageSapling = 0x02;

    /// <summary>Storage type of a tree file.</summary>
    public const byte StorageTree = 0x03;

    /// <summary>Storage type of a Pascal area. Not a readable file.</summary>
    public const byte StoragePascalArea = 0x04;

    /// <summary>Storage type of a GS/OS extended file. Not a readable file.</summary>
    public const byte StorageExtended = 0x05;

    /// <summary>Storage type of a subdirectory file entry in its parent.</summary>
    public const byte StorageSubdirectory = 0x0D;

    /// <summary>Storage type of the header inside a subdirectory key block.</summary>
    public const byte StorageSubdirectoryHeader = 0x0E;

    /// <summary>Storage type of the volume directory header.</summary>
    public const byte StorageVolumeHeader = 0x0F;

    /// <summary>Header entry offset of <see cref="EntryLength"/>.</summary>
    public const int VolumeHeaderEntryLengthOffset = 0x1F;

    /// <summary>Header entry offset of <see cref="EntriesPerBlock"/>.</summary>
    public const int VolumeHeaderEntriesPerBlockOffset = 0x20;

    /// <summary>Header entry offset of the active-entry count. The header itself is not included.</summary>
    public const int VolumeHeaderFileCountOffset = 0x21;

    /// <summary>Volume-header entry offset of the first bitmap block.</summary>
    public const int VolumeHeaderBitmapPointerOffset = 0x23;

    /// <summary>Volume-header entry offset of the volume's block count.</summary>
    public const int VolumeHeaderTotalBlocksOffset = 0x25;

    /// <summary>Subdirectory-header entry offset of the <c>$75</c> ProDOS 8 reserved mark.</summary>
    public const int SubdirectoryReservedOffset = 0x10;

    /// <summary>Value ProDOS 8 stores at <see cref="SubdirectoryReservedOffset"/>.</summary>
    public const byte SubdirectoryReservedMark = 0x75;

    /// <summary>
    /// Subdirectory-header entry offset of the parent directory block.
    /// This is the block that contains the subdirectory's entry. It is the volume key
    /// block only when the entry is stored there.
    /// </summary>
    public const int SubdirectoryParentPointerOffset = 0x23;

    /// <summary>
    /// Subdirectory-header entry offset of the 1-based entry number in the parent block.
    /// In a directory key block the header is entry 1, so the first file is entry 2.
    /// A continuation block has no header, so its first file is entry 1.
    /// </summary>
    public const int SubdirectoryParentEntryNumberOffset = 0x25;

    /// <summary>Subdirectory-header entry offset of the parent entry length.</summary>
    public const int SubdirectoryParentEntryLengthOffset = 0x26;

    /// <summary>Index-block offset of pointer 0's low byte. Pointer <c>i</c> is at this offset plus <c>i</c>.</summary>
    public const int IndexLowHalf = 0;

    /// <summary>Index-block offset of pointer 0's high byte. Pointer <c>i</c> is at this offset plus <c>i</c>.</summary>
    public const int IndexHighHalf = 256;

    /// <summary>Pointers in one index block.</summary>
    public const int IndexPointers = 256;

    /// <summary>Bytes addressed by one master-index slot (256 data blocks).</summary>
    public const int TreeSlotBytes = IndexPointers * BlockSize;

    /// <summary>Offset of the data-fork mini-entry in an extended key block.</summary>
    public const int ExtendedDataForkOffset = 0x000;

    /// <summary>Offset of the resource-fork mini-entry in an extended key block.</summary>
    public const int ExtendedResourceForkOffset = 0x100;

    /// <summary>Mini-entry offset of the storage-type byte. The name length is 0.</summary>
    public const int ExtendedMiniStorageOffset = 0;

    /// <summary>Mini-entry offset of the little-endian fork key pointer.</summary>
    public const int ExtendedMiniKeyPointerOffset = 1;

    /// <summary>Mini-entry offset of the little-endian blocks-used count.</summary>
    public const int ExtendedMiniBlocksUsedOffset = 3;

    /// <summary>Mini-entry offset of the 24-bit fork EOF.</summary>
    public const int ExtendedMiniEofOffset = 5;

    /// <summary>ProDOS text file type.</summary>
    public const byte FileTypeText = 0x04;

    /// <summary>ProDOS binary file type.</summary>
    public const byte FileTypeBinary = 0x06;

    /// <summary>ProDOS directory file type.</summary>
    public const byte FileTypeDirectory = 0x0F;

    /// <summary>Bits in one bitmap block.</summary>
    public const int BitsPerBitmapBlock = 4096;

    /// <summary>Blocks on a 140K floppy.</summary>
    public const int StandardFloppyBlocks = 280;

    /// <summary>Blocks on an 800K disk.</summary>
    public const int Floppy800KBlocks = 1600;

    /// <summary>Largest block count the block-device runtime mounts.</summary>
    public const int MaxBlockCount = 65535;

    /// <summary>Directory blocks a standard floppy-or-larger volume uses.</summary>
    public const int StandardDirectoryBlocks = 4;

    /// <summary>
    /// Returns the directory block count a fixture uses for a volume of the given size.
    /// </summary>
    /// <param name="blockCount">Blocks on the volume.</param>
    /// <returns>4 when the volume is at least a 140K floppy, otherwise 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="blockCount"/> is not positive.</exception>
    public static int DirectoryBlockCount(int blockCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCount);
        return blockCount >= StandardFloppyBlocks ? StandardDirectoryBlocks : 1;
    }

    /// <summary>
    /// Returns how many bitmap blocks a volume of the given size needs.
    /// </summary>
    /// <param name="blockCount">Blocks on the volume.</param>
    /// <returns>One block per <see cref="BitsPerBitmapBlock"/>, rounded up.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="blockCount"/> is not positive.</exception>
    public static int BitmapBlockCount(int blockCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCount);
        return (blockCount + BitsPerBitmapBlock - 1) / BitsPerBitmapBlock;
    }

    /// <summary>
    /// Returns the first bitmap block for a fixture laid out by <see cref="DirectoryBlockCount"/>.
    /// </summary>
    /// <param name="blockCount">Blocks on the volume.</param>
    /// <returns>The block after the directory.</returns>
    public static int BitmapPointer(int blockCount) => VolumeKeyBlock + DirectoryBlockCount(blockCount);

    /// <summary>
    /// Returns the first block a fixture may allocate to a file.
    /// </summary>
    /// <param name="blockCount">Blocks on the volume.</param>
    /// <returns>The block after the bitmap.</returns>
    public static int FirstDataBlock(int blockCount) => BitmapPointer(blockCount) + BitmapBlockCount(blockCount);

    /// <summary>
    /// Reads one index pointer from a sapling index or a tree master index.
    /// </summary>
    /// <param name="indexBlock">A 512-byte index block.</param>
    /// <param name="index">Pointer index in the range 0–255.</param>
    /// <returns>The block number, or 0 when the pointer is a sparse hole.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the block is shorter than 512 or <paramref name="index"/> is out of range.</exception>
    /// <remarks>
    /// Byte <c>index</c> is the low byte and byte <c>256 + index</c> is the high byte.
    /// This is not the adjacent pair at offset <c>2 × index</c>.
    /// </remarks>
    public static int ReadIndexPointer(ReadOnlySpan<byte> indexBlock, int index)
    {
        if (indexBlock.Length < BlockSize)
        {
            throw new ArgumentOutOfRangeException(nameof(indexBlock), "An index block is 512 bytes.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, IndexPointers);
        return indexBlock[IndexLowHalf + index] | (indexBlock[IndexHighHalf + index] << 8);
    }

    /// <summary>
    /// Maps a storage-type nibble onto <see cref="FileStorageKind"/>.
    /// </summary>
    /// <param name="storageNibble">The high nibble of a directory entry's first byte.</param>
    /// <returns>
    /// The matching kind. Headers, deleted slots, and unknown nibbles return <see cref="FileStorageKind.None"/>.
    /// </returns>
    public static FileStorageKind ToStorageKind(byte storageNibble) => storageNibble switch
    {
        StorageSeedling => FileStorageKind.ProdosSeedling,
        StorageSapling => FileStorageKind.ProdosSapling,
        StorageTree => FileStorageKind.ProdosTree,
        StorageSubdirectory => FileStorageKind.ProdosSubdirectory,
        StorageExtended => FileStorageKind.ProdosExtended,
        StoragePascalArea => FileStorageKind.ProdosPascalArea,
        _ => FileStorageKind.None,
    };

    /// <summary>
    /// Returns whether a ProDOS access byte denies writing.
    /// </summary>
    /// <param name="access">The access byte.</param>
    /// <returns><see langword="true"/> when <see cref="WriteAccessBit"/> is clear.</returns>
    public static bool IsLocked(byte access) => (access & WriteAccessBit) == 0;

    /// <summary>
    /// Returns whether a volume-bitmap bit says the block is free.
    /// </summary>
    /// <param name="bitmap">The volume bitmap bytes, not the whole disk image.</param>
    /// <param name="block">Zero-based block number.</param>
    /// <returns><see langword="true"/> when bit 7 of the block's byte, shifted by <c>block % 8</c>, is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="block"/> is negative or its byte is outside <paramref name="bitmap"/>.</exception>
    /// <remarks>Block 0 is bit 7 of byte 0. The mask is <c>0x80 &gt;&gt; (block &amp; 7)</c>.</remarks>
    public static bool IsBlockFree(ReadOnlySpan<byte> bitmap, int block)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(block);
        var offset = block >> 3;
        if ((uint)offset >= (uint)bitmap.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(block), "The bitmap byte is outside the buffer.");
        }

        var mask = (byte)(0x80 >> (block & 7));
        return (bitmap[offset] & mask) != 0;
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> is a legal on-disk ProDOS name.
    /// </summary>
    /// <param name="name">Uppercase name bytes, without the length nibble.</param>
    /// <returns>
    /// <see langword="true"/> when the length is 1–15, the first byte is <c>A</c>–<c>Z</c>,
    /// and the rest are letters, digits, or <c>.</c>.
    /// </returns>
    public static bool IsValidName(ReadOnlySpan<byte> name)
    {
        if (name.Length is < 1 or > NameCapacity || !IsUpperLetter(name[0]))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!IsUpperLetter(c) && !IsDigit(c) && c != (byte)'.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether block 2 is a ProDOS volume directory key block for this image.
    /// </summary>
    /// <param name="block">Block 2. A short buffer is not a key block.</param>
    /// <param name="blockCount">Blocks the media exposes.</param>
    /// <returns>
    /// <see langword="true"/> when the previous pointer is 0, the storage nibble is
    /// <see cref="StorageVolumeHeader"/>, the name is legal, the entry length and entries-per-block
    /// are the standard values, the recorded total blocks fit in the image, and the bitmap
    /// starting at the recorded pointer occupies <see cref="BitmapBlockCount"/> blocks that
    /// also fit in that total.
    /// </returns>
    public static bool IsVolumeDirectoryKeyBlock(ReadOnlySpan<byte> block, int blockCount)
    {
        if (block.Length < BlockSize || blockCount <= VolumeKeyBlock)
        {
            return false;
        }

        if (block[0] != 0 || block[1] != 0)
        {
            return false;
        }

        var next = block[2] | (block[3] << 8);
        if (next != 0 && (next <= VolumeKeyBlock || next >= blockCount))
        {
            return false;
        }

        var storageAndLength = block[KeyBlockHeaderOffset];
        if ((storageAndLength >> 4) != StorageVolumeHeader)
        {
            return false;
        }

        var nameLength = storageAndLength & 0x0F;
        if (!IsValidName(block.Slice(KeyBlockHeaderOffset + NameOffset, nameLength)))
        {
            return false;
        }

        var header = KeyBlockHeaderOffset;
        if (block[header + VolumeHeaderEntryLengthOffset] != EntryLength
            || block[header + VolumeHeaderEntriesPerBlockOffset] != EntriesPerBlock)
        {
            return false;
        }

        var total = block[header + VolumeHeaderTotalBlocksOffset] | (block[header + VolumeHeaderTotalBlocksOffset + 1] << 8);
        var bitmap = block[header + VolumeHeaderBitmapPointerOffset] | (block[header + VolumeHeaderBitmapPointerOffset + 1] << 8);
        if (total < 4 || total > blockCount || bitmap <= VolumeKeyBlock)
        {
            return false;
        }

        var bitmapBlocks = BitmapBlockCount(total);
        return (long)bitmap + bitmapBlocks <= total;
    }

    private static bool IsUpperLetter(byte c) => c is >= (byte)'A' and <= (byte)'Z';

    private static bool IsDigit(byte c) => c is >= (byte)'0' and <= (byte)'9';
}