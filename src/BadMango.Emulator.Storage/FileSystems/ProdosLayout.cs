// <copyright file="ProdosLayout.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS directory, index, and bitmap offsets the reader and the synthetic fixtures share.
/// </summary>
/// <remarks>
/// <para>
/// The volume key block is block <see cref="VolumeKeyBlock"/>. Its first entry, at
/// <see cref="Directory.FirstEntryOffset"/>, is the volume header
/// (<see cref="ProdosStorageType.VolumeHeader"/>). That header is not a file. A subdirectory
/// key block starts with <see cref="ProdosStorageType.SubdirectoryHeader"/>, which is also
/// not a file. A subdirectory file in its parent is <see cref="ProdosStorageType.Subdirectory"/>.
/// </para>
/// <para>
/// Index blocks are not adjacent byte pairs. <see cref="ReadIndexPointer"/> reads byte
/// <c>i</c> from <see cref="Index.LowHalf"/> and byte <c>256 + i</c> from <see cref="Index.HighHalf"/>.
/// A zero pointer is a sparse hole. Readers still emit zeros and stop at the EOF field.
/// <see cref="ProdosStorageType.Extended"/> and <see cref="ProdosStorageType.PascalArea"/> are
/// <see cref="FileAccessStatus.UnsupportedStorage"/>. An extended key block holds a data-fork
/// mini-entry at <see cref="ExtendedKeyBlock.DataForkOffset"/> and a resource-fork mini-entry
/// at <see cref="ExtendedKeyBlock.ResourceForkOffset"/>.
/// </para>
/// <para>
/// Header field offsets are entry-relative. Add <see cref="Directory.FirstEntryOffset"/> to
/// reach them in a key block. The volume bitmap stores one bit per block, and a set bit means
/// free. Block 0 is bit 7 of byte 0. <see cref="IsBlockFree"/> is that mapping.
/// </para>
/// </remarks>
public static class ProdosLayout
{
    /// <summary>Bytes in one ProDOS block.</summary>
    public const int BlockSize = 512;

    /// <summary>Block that holds the volume directory key block.</summary>
    public const int VolumeKeyBlock = 2;

    /// <summary>Maximum characters in a ProDOS name.</summary>
    public const int NameCapacity = 15;

    /// <summary>Access bit that allows writing. Clear means locked.</summary>
    public const byte WriteAccessBit = 0x02;

    /// <summary>Typical unlocked access: destroy, rename, write, and read.</summary>
    public const byte AccessUnlocked = 0xC3;

    /// <summary>Locked access: destroy, rename, and read, with the write bit clear.</summary>
    public const byte AccessLocked = 0xC1;

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
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Index.PointerCount);
        return indexBlock[Index.LowHalf + index] | (indexBlock[Index.HighHalf + index] << 8);
    }

    /// <summary>
    /// Maps a storage-type nibble onto <see cref="FileStorageKind"/>.
    /// </summary>
    /// <param name="storageType">The storage type.</param>
    /// <returns>
    /// The matching kind. Headers, deleted slots, and an undefined cast return <see cref="FileStorageKind.None"/>.
    /// </returns>
    public static FileStorageKind ToStorageKind(ProdosStorageType storageType) => storageType switch
    {
        ProdosStorageType.Seedling => FileStorageKind.ProdosSeedling,
        ProdosStorageType.Sapling => FileStorageKind.ProdosSapling,
        ProdosStorageType.Tree => FileStorageKind.ProdosTree,
        ProdosStorageType.Subdirectory => FileStorageKind.ProdosSubdirectory,
        ProdosStorageType.Extended => FileStorageKind.ProdosExtended,
        ProdosStorageType.PascalArea => FileStorageKind.ProdosPascalArea,
        ProdosStorageType.Deleted or ProdosStorageType.SubdirectoryHeader or ProdosStorageType.VolumeHeader => FileStorageKind.None,
        _ => FileStorageKind.None,
    };

    /// <summary>
    /// Maps a storage-type nibble onto <see cref="FileStorageKind"/>.
    /// </summary>
    /// <param name="storageNibble">The high nibble of a directory entry's first byte.</param>
    /// <returns>
    /// The matching kind. Unknown nibbles, headers, and deleted slots return <see cref="FileStorageKind.None"/>.
    /// </returns>
    public static FileStorageKind ToStorageKind(byte storageNibble)
    {
        var storageType = (ProdosStorageType)storageNibble;
        return Enum.IsDefined(storageType) ? ToStorageKind(storageType) : FileStorageKind.None;
    }

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
    /// Counts free and used blocks from a volume bitmap.
    /// </summary>
    /// <param name="bitmap">The volume bitmap bytes, not the whole disk image.</param>
    /// <param name="totalBlocks">Blocks named by the volume header. Each one is tested.</param>
    /// <returns>
    /// Free blocks are those whose bitmap bit is set. Used is <paramref name="totalBlocks"/> minus free.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="totalBlocks"/> is negative or a bit falls outside <paramref name="bitmap"/>.</exception>
    public static VolumeSpace CountFreeBlocks(ReadOnlySpan<byte> bitmap, int totalBlocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalBlocks);
        var free = 0;
        for (var block = 0; block < totalBlocks; block++)
        {
            if (IsBlockFree(bitmap, block))
            {
                free++;
            }
        }

        return new VolumeSpace(free, totalBlocks - free, totalBlocks);
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
    /// <see cref="ProdosStorageType.VolumeHeader"/>, the name is legal, the entry length and
    /// entries-per-block are the standard values, the recorded total blocks fit in the image,
    /// and the bitmap starting at the recorded pointer occupies <see cref="BitmapBlockCount"/>
    /// blocks that also fit in that total.
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

        var storageAndLength = block[Directory.FirstEntryOffset];
        if ((storageAndLength >> 4) != (byte)ProdosStorageType.VolumeHeader)
        {
            return false;
        }

        var nameLength = storageAndLength & 0x0F;
        if (!IsValidName(block.Slice(Directory.FirstEntryOffset + Entry.NameOffset, nameLength)))
        {
            return false;
        }

        var header = Directory.FirstEntryOffset;
        if (block[header + VolumeHeader.EntryLengthOffset] != Entry.Length
            || block[header + VolumeHeader.EntriesPerBlockOffset] != Directory.EntriesPerBlock)
        {
            return false;
        }

        var total = block[header + VolumeHeader.TotalBlocksOffset] | (block[header + VolumeHeader.TotalBlocksOffset + 1] << 8);
        var bitmap = block[header + VolumeHeader.BitmapPointerOffset] | (block[header + VolumeHeader.BitmapPointerOffset + 1] << 8);
        if (total < 4 || total > blockCount || bitmap <= VolumeKeyBlock)
        {
            return false;
        }

        var bitmapBlocks = BitmapBlockCount(total);
        return (long)bitmap + bitmapBlocks <= total;
    }

    private static bool IsUpperLetter(byte c) => c is >= (byte)'A' and <= (byte)'Z';

    private static bool IsDigit(byte c) => c is >= (byte)'0' and <= (byte)'9';

    /// <summary>A directory block: the link words, then entry slots.</summary>
    public static class Directory
    {
        /// <summary>Offset of the first entry. The four bytes before it are the block links.</summary>
        public const int FirstEntryOffset = 4;

        /// <summary>Entry slots in one directory block.</summary>
        public const int EntriesPerBlock = 0x0D;
    }

    /// <summary>One 39-byte file or header entry. Offsets are entry-relative.</summary>
    public static class Entry
    {
        /// <summary>Bytes in one directory entry.</summary>
        public const int Length = 0x27;

        /// <summary>Offset of the name bytes.</summary>
        public const int NameOffset = 0x01;

        /// <summary>Offset of the ProDOS file type.</summary>
        public const int FileTypeOffset = 0x10;

        /// <summary>Offset of the little-endian key pointer.</summary>
        public const int KeyPointerOffset = 0x11;

        /// <summary>Offset of the little-endian blocks-used count.</summary>
        public const int BlocksUsedOffset = 0x13;

        /// <summary>Offset of the 24-bit end-of-file.</summary>
        public const int EofOffset = 0x15;

        /// <summary>Offset of the little-endian creation date.</summary>
        public const int CreationDateOffset = 0x18;

        /// <summary>Offset of the little-endian creation time. It follows <see cref="CreationDateOffset"/>.</summary>
        public const int CreationTimeOffset = 0x1A;

        /// <summary>Offset of the access byte. Headers use the same entry-relative offset.</summary>
        public const int AccessOffset = 0x1E;

        /// <summary>Offset of the little-endian auxiliary type.</summary>
        public const int AuxTypeOffset = 0x1F;

        /// <summary>Offset of the little-endian modification date.</summary>
        public const int ModificationDateOffset = 0x21;

        /// <summary>Offset of the little-endian modification time. It follows <see cref="ModificationDateOffset"/>.</summary>
        public const int ModificationTimeOffset = 0x23;

        /// <summary>Offset of the directory key block that contains the entry.</summary>
        public const int HeaderPointerOffset = 0x25;
    }

    /// <summary>Entry-relative fields of a volume directory header.</summary>
    public static class VolumeHeader
    {
        /// <summary>
        /// Offset of the ProDOS 8 Tech Note #8 case-bit word.
        /// </summary>
        /// <remarks>
        /// Tech Note #8 numbers this word <c>+$1A</c>/<c>+$1B</c> from the start of the key block.
        /// The header entry begins after the 4-byte previous and next pointers, so the
        /// entry-relative offset is <c>$16</c>. It sits in the reserved bytes <c>$10</c>–<c>$17</c>.
        /// Entry <c>$1A</c> is the creation time. The high byte of that word is the hour, so bit 15 is clear.
        /// </remarks>
        public const int CaseBitsOffset = 0x16;

        /// <summary>Offset of the standard entry length.</summary>
        public const int EntryLengthOffset = 0x1F;

        /// <summary>Offset of the entries-per-block count.</summary>
        public const int EntriesPerBlockOffset = 0x20;

        /// <summary>Offset of the active-entry count. The header itself is not included.</summary>
        public const int FileCountOffset = 0x21;

        /// <summary>Offset of the first bitmap block.</summary>
        public const int BitmapPointerOffset = 0x23;

        /// <summary>Offset of the volume's block count.</summary>
        public const int TotalBlocksOffset = 0x25;
    }

    /// <summary>Entry-relative fields of a subdirectory header.</summary>
    public static class SubdirectoryHeader
    {
        /// <summary>Offset of the ProDOS 8 reserved mark.</summary>
        public const int ReservedOffset = 0x10;

        /// <summary>Value ProDOS 8 stores at <see cref="ReservedOffset"/>.</summary>
        public const byte ReservedMark = 0x75;

        /// <summary>Offset of the standard entry length. Same entry-relative slot as the volume header.</summary>
        public const int EntryLengthOffset = 0x1F;

        /// <summary>Offset of the entries-per-block count.</summary>
        public const int EntriesPerBlockOffset = 0x20;

        /// <summary>Offset of the active-entry count. The header itself is not included.</summary>
        public const int FileCountOffset = 0x21;

        /// <summary>
        /// Offset of the parent directory block. This is the block that contains the
        /// subdirectory's entry, which is the volume key block only when the entry lives there.
        /// </summary>
        public const int ParentPointerOffset = 0x23;

        /// <summary>
        /// Offset of the 1-based entry number in the parent block.
        /// In a directory key block the header is entry 1, so the first file is entry 2.
        /// A continuation block has no header, so its first file is entry 1.
        /// </summary>
        public const int ParentEntryNumberOffset = 0x25;

        /// <summary>Offset of the parent entry length.</summary>
        public const int ParentEntryLengthOffset = 0x26;
    }

    /// <summary>A sapling index or a tree master index.</summary>
    public static class Index
    {
        /// <summary>Offset of pointer 0's low byte. Pointer <c>i</c> is at this offset plus <c>i</c>.</summary>
        public const int LowHalf = 0;

        /// <summary>Offset of pointer 0's high byte. Pointer <c>i</c> is at this offset plus <c>i</c>.</summary>
        public const int HighHalf = 256;

        /// <summary>Pointers in one index block.</summary>
        public const int PointerCount = 256;

        /// <summary>Bytes addressed by one master-index slot.</summary>
        public const int SlotBytes = PointerCount * BlockSize;
    }

    /// <summary>Key block of a storage-type <see cref="ProdosStorageType.Extended"/> file.</summary>
    public static class ExtendedKeyBlock
    {
        /// <summary>Offset of the data-fork mini-entry.</summary>
        public const int DataForkOffset = 0x000;

        /// <summary>Offset of the resource-fork mini-entry.</summary>
        public const int ResourceForkOffset = 0x100;

        /// <summary>Mini-entry offset of the storage-type byte. The name length is 0.</summary>
        public const int StorageOffset = 0;

        /// <summary>Mini-entry offset of the little-endian fork key pointer.</summary>
        public const int KeyPointerOffset = 1;

        /// <summary>Mini-entry offset of the little-endian blocks-used count.</summary>
        public const int BlocksUsedOffset = 3;

        /// <summary>Mini-entry offset of the 24-bit fork EOF.</summary>
        public const int EofOffset = 5;
    }

    /// <summary>The ProDOS boot block (block 0).</summary>
    public static class BootBlock
    {
        private static readonly byte[] SignatureBytes =
        [
            0x01, 0x38, 0xB0, 0x03,
        ];

        /// <summary>Gets the boot-block prefix <c>01 38 B0 03</c>.</summary>
        /// <value>The four bytes at the start of block 0.</value>
        public static ReadOnlySpan<byte> Signature => SignatureBytes;
    }
}