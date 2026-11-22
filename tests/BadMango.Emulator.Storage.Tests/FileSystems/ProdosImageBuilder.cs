// <copyright file="ProdosImageBuilder.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

/// <summary>
/// Builds a ProDOS volume in memory. Boot blocks stay zero. Names are uppercase.
/// </summary>
/// <remarks>
/// Volumes of at least 280 blocks use four directory blocks and a bitmap at block 6.
/// Smaller volumes use one directory block and the following bitmap block. Index pointers
/// are written low-half then high-half. The image contains no ProDOS system files.
/// </remarks>
public sealed class ProdosImageBuilder
{
    private readonly int blockCount;
    private readonly string volumeName;
    private readonly List<Pending> root = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProdosImageBuilder"/> class.
    /// </summary>
    /// <param name="blockCount">Blocks, from the bitmap's end through <see cref="ProdosLayout.MaxBlockCount"/>.</param>
    /// <param name="volumeName">Volume name, 1–15 ProDOS characters.</param>
    /// <exception cref="ArgumentOutOfRangeException">If the volume cannot hold its directory and bitmap.</exception>
    /// <exception cref="ArgumentException">If <paramref name="volumeName"/> is not a ProDOS name.</exception>
    public ProdosImageBuilder(int blockCount, string volumeName)
    {
        if (blockCount > ProdosLayout.MaxBlockCount || blockCount < ProdosLayout.FirstDataBlock(Math.Max(blockCount, 1)))
        {
            throw new ArgumentOutOfRangeException(nameof(blockCount), "Block count must fit a directory and a bitmap, and is at most 65535.");
        }

        this.blockCount = blockCount;
        this.volumeName = Normalize(volumeName);
    }

    private enum PendingKind
    {
        Seedling,
        Sapling,
        Tree,
        Extended,
        Directory,
    }

    /// <summary>Adds a seedling of at most one block.</summary>
    /// <param name="name">File name.</param>
    /// <param name="fileType">ProDOS file type.</param>
    /// <param name="auxType">Auxiliary type.</param>
    /// <param name="data">EOF bytes.</param>
    /// <param name="locked">Whether the write bit is clear.</param>
    /// <param name="eof">When set, the EOF field. At most 512. A larger file is a sapling.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="eof"/> is greater than 512.</exception>
    public ProdosImageBuilder AddSeedling(string name, ProdosFileType fileType, ushort auxType, ReadOnlyMemory<byte> data, bool locked, int? eof = null)
    {
        if (eof is > ProdosLayout.BlockSize)
        {
            throw new ArgumentOutOfRangeException(nameof(eof), "A seedling EOF is at most 512 bytes. A larger file is a sapling.");
        }

        var pending = this.CreateSeedling(name, fileType, auxType, data, locked, headerBlock: ProdosLayout.VolumeKeyBlock, directoryPath: string.Empty);
        pending.ExplicitEof = eof;
        return this.AddRoot(pending);
    }

    /// <summary>
    /// Adds a sapling. A null slot is a sparse hole. Only the last occupied block may be shorter than 512 bytes.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="fileType">ProDOS file type.</param>
    /// <param name="auxType">Auxiliary type.</param>
    /// <param name="dataBlocks">Index slots. Null is a hole.</param>
    /// <param name="locked">Whether the write bit is clear.</param>
    /// <param name="minimumBlockForLast">When set, the last occupied data block is allocated at or above this index.</param>
    /// <param name="eof">When set, the EOF field. It may extend past the last allocated block as trailing holes.</param>
    /// <returns>This builder.</returns>
    public ProdosImageBuilder AddSapling(
        string name,
        ProdosFileType fileType,
        ushort auxType,
        IReadOnlyList<byte[]?> dataBlocks,
        bool locked,
        int? minimumBlockForLast = null,
        int? eof = null)
    {
        this.root.Add(new Pending(name, fileType, auxType, locked, PendingKind.Sapling, dataBlocks, minimumBlockForLast ?? 0, 0)
        {
            ExplicitEof = eof,
        });
        return this;
    }

    /// <summary>
    /// Adds a tree whose master index points at one subindex.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="fileType">ProDOS file type.</param>
    /// <param name="auxType">Auxiliary type.</param>
    /// <param name="subIndexData">Data slots of that subindex. Null is a hole.</param>
    /// <param name="subIndexMinimumBlock">Lowest block number for the subindex, so its high byte can be non-zero.</param>
    /// <param name="locked">Whether the write bit is clear.</param>
    /// <param name="masterSlot">Master-index slot that holds the subindex. Slot 0 empty with slot 1 used is a sparse tree.</param>
    /// <returns>This builder.</returns>
    public ProdosImageBuilder AddTree(
        string name,
        ProdosFileType fileType,
        ushort auxType,
        IReadOnlyList<byte[]?> subIndexData,
        int subIndexMinimumBlock,
        bool locked,
        int masterSlot = 0)
    {
        if (masterSlot is < 0 or >= ProdosLayout.Index.PointerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(masterSlot), "A master index has 256 slots.");
        }

        this.root.Add(new Pending(name, fileType, auxType, locked, PendingKind.Tree, subIndexData, 0, subIndexMinimumBlock)
        {
            MasterSlot = masterSlot,
        });
        return this;
    }

    /// <summary>
    /// Adds a storage-type <c>$5</c> entry whose key block has data and resource mini-entries.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="fileType">ProDOS file type.</param>
    /// <returns>This builder.</returns>
    public ProdosImageBuilder AddExtended(string name, ProdosFileType fileType)
    {
        this.root.Add(new Pending(name, fileType, 0, false, PendingKind.Extended, Array.Empty<byte[]?>(), 0, 0));
        return this;
    }

    /// <summary>Adds a subdirectory of seedlings. At most 12 children.</summary>
    /// <param name="name">Directory name.</param>
    /// <param name="children">Files stored in the subdirectory key block.</param>
    /// <returns>This builder.</returns>
    public ProdosImageBuilder AddSubdirectory(string name, IReadOnlyList<ProdosFixture.SeedlingSpec> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count > 12)
        {
            throw new ArgumentException("This fixture builder stores at most 12 files in a subdirectory.", nameof(children));
        }

        this.root.Add(new Pending(name, ProdosFileType.Directory, 0, false, PendingKind.Directory, Array.Empty<byte[]?>(), 0, 0)
        {
            Children = children,
        });
        return this;
    }

    /// <summary>Writes the volume.</summary>
    /// <returns>A new image. Repeated calls return independent buffers.</returns>
    /// <exception cref="InvalidOperationException">If the directory or the bitmap runs out of blocks.</exception>
    public ProdosFixture Build()
    {
        var image = new byte[this.blockCount * ProdosLayout.BlockSize];
        var used = new bool[this.blockCount];
        var firstData = ProdosLayout.FirstDataBlock(this.blockCount);
        for (var block = 0; block < firstData; block++)
        {
            used[block] = true;
        }

        var entries = new List<ProdosFixture.Entry>(this.root.Count);
        foreach (var pending in this.root)
        {
            entries.Add(this.Place(image, used, pending));
        }

        this.WriteDirectory(image, entries);
        this.WriteBitmap(image, used);
        return new ProdosFixture(image, this.blockCount, this.volumeName, entries);
    }

    private ProdosImageBuilder AddRoot(Pending pending)
    {
        this.root.Add(pending);
        return this;
    }

    private Pending CreateSeedling(string name, ProdosFileType fileType, ushort auxType, ReadOnlyMemory<byte> data, bool locked, int headerBlock, string directoryPath)
    {
        if (data.Length > ProdosLayout.BlockSize)
        {
            throw new ArgumentException("A seedling is at most one block.", nameof(data));
        }

        return new Pending(name, fileType, auxType, locked, PendingKind.Seedling, Array.Empty<byte[]?>(), 0, 0)
        {
            Seedling = data.ToArray(),
            HeaderBlock = headerBlock,
            DirectoryPath = directoryPath,
        };
    }

    private ProdosFixture.Entry Place(byte[] image, bool[] used, Pending pending)
    {
        var name = Normalize(pending.Name);
        return pending.Kind switch
        {
            PendingKind.Seedling => this.PlaceSeedling(image, used, pending, name),
            PendingKind.Sapling => this.PlaceIndexed(image, used, pending, name, FileStorageKind.ProdosSapling, pending.MinimumBlockForLast),
            PendingKind.Tree => this.PlaceTree(image, used, pending, name),
            PendingKind.Extended => this.PlaceExtended(image, used, pending, name),
            PendingKind.Directory => this.PlaceDirectory(image, used, pending, name),
            _ => throw new InvalidOperationException("Unknown ProDOS fixture entry."),
        };
    }

    private ProdosFixture.Entry PlaceSeedling(byte[] image, bool[] used, Pending pending, string name)
    {
        var dataBlock = Allocate(used, 0);
        pending.Seedling.CopyTo(image.AsSpan(dataBlock * ProdosLayout.BlockSize, pending.Seedling.Length));
        var raw = PadToEof(pending.Seedling, ResolveEof(pending.Seedling.Length, pending.ExplicitEof, ProdosLayout.BlockSize));
        return ToEntry(pending, name, dataBlock, raw);
    }

    private ProdosFixture.Entry PlaceIndexed(
        byte[] image,
        bool[] used,
        Pending pending,
        string name,
        FileStorageKind kind,
        int minimumForLast)
    {
        var pointers = this.AllocateData(image, used, pending.Blocks, minimumForLast);
        var indexBlock = Allocate(used, 0);
        WriteIndex(image, indexBlock, pointers);
        var natural = LogicalBytes(pending.Blocks, pointers);
        var raw = PadToEof(natural, ResolveEof(natural.Length, pending.ExplicitEof));
        var usedBlocks = 1 + pointers.Count(pointer => pointer != 0);
        return new ProdosFixture.Entry(name, name, pending.FileType, pending.AuxType, raw.Length, usedBlocks, pending.Locked, false, kind, indexBlock, pointers, Array.Empty<int>(), raw, Array.Empty<ProdosFixture.Entry>());
    }

    private ProdosFixture.Entry PlaceTree(byte[] image, bool[] used, Pending pending, string name)
    {
        var dataPointers = this.AllocateData(image, used, pending.Blocks, 0);
        var subIndex = Allocate(used, pending.SubIndexMinimumBlock);
        WriteIndex(image, subIndex, dataPointers);
        var master = Allocate(used, 0);
        var masterPointers = new int[pending.MasterSlot + 1];
        masterPointers[pending.MasterSlot] = subIndex;
        WriteIndex(image, master, masterPointers);
        var subRaw = LogicalBytes(pending.Blocks, dataPointers);
        var naturalLength = (pending.MasterSlot * ProdosLayout.Index.SlotBytes) + subRaw.Length;
        var raw = new byte[ResolveEof(naturalLength, pending.ExplicitEof)];
        subRaw.CopyTo(raw.AsSpan(pending.MasterSlot * ProdosLayout.Index.SlotBytes));
        var usedBlocks = 2 + dataPointers.Count(pointer => pointer != 0);
        return new ProdosFixture.Entry(
            name,
            name,
            pending.FileType,
            pending.AuxType,
            raw.Length,
            usedBlocks,
            pending.Locked,
            false,
            FileStorageKind.ProdosTree,
            master,
            masterPointers,
            dataPointers,
            raw,
            Array.Empty<ProdosFixture.Entry>());
    }

    private ProdosFixture.Entry PlaceExtended(byte[] image, bool[] used, Pending pending, string name)
    {
        var dataFork = new byte[] { 0xD1, 0xD2 };
        var resourceFork = new byte[] { 0xE1 };
        var dataBlock = Allocate(used, 0);
        var resourceBlock = Allocate(used, 0);
        dataFork.CopyTo(image.AsSpan(dataBlock * ProdosLayout.BlockSize));
        resourceFork.CopyTo(image.AsSpan(resourceBlock * ProdosLayout.BlockSize));
        var key = Allocate(used, 0);
        WriteForkMiniEntry(image, key, ProdosLayout.ExtendedKeyBlock.DataForkOffset, dataBlock, dataFork.Length);
        WriteForkMiniEntry(image, key, ProdosLayout.ExtendedKeyBlock.ResourceForkOffset, resourceBlock, resourceFork.Length);
        return new ProdosFixture.Entry(
            name,
            name,
            pending.FileType,
            0,
            dataFork.Length,
            3,
            false,
            false,
            FileStorageKind.ProdosExtended,
            key,
            new[] { dataBlock, resourceBlock },
            Array.Empty<int>(),
            ReadOnlyMemory<byte>.Empty,
            Array.Empty<ProdosFixture.Entry>());
    }

    private ProdosFixture.Entry PlaceDirectory(byte[] image, bool[] used, Pending pending, string name)
    {
        var key = Allocate(used, 0);
        var children = new List<ProdosFixture.Entry>(pending.Children.Count);
        for (var i = 0; i < pending.Children.Count; i++)
        {
            var spec = pending.Children[i];
            var child = this.CreateSeedling(spec.Name, spec.FileType, spec.AuxType, spec.Data, spec.Locked, key, name);
            var placed = this.PlaceSeedling(image, used, child, Normalize(spec.Name));
            var slot = (key * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset + ((i + 1) * ProdosLayout.Entry.Length);
            FileEntryBytes(placed, key).CopyTo(image.AsSpan(slot, ProdosLayout.Entry.Length));
            children.Add(placed);
        }

        WriteSubdirectoryHeader(image, key, name, children.Count);
        return new ProdosFixture.Entry(
            name,
            name,
            ProdosFileType.Directory,
            0,
            ProdosLayout.BlockSize,
            1,
            false,
            true,
            FileStorageKind.ProdosSubdirectory,
            key,
            Array.Empty<int>(),
            Array.Empty<int>(),
            ReadOnlyMemory<byte>.Empty,
            children);
    }

    private int[] AllocateData(byte[] image, bool[] used, IReadOnlyList<byte[]?> blocks, int minimumForLast)
    {
        if (blocks.Count == 0 || blocks.Count > ProdosLayout.Index.PointerCount)
        {
            throw new ArgumentException("An index needs 1 to 256 slots.");
        }

        var last = LastOccupied(blocks);
        var pointers = new int[last + 1];
        for (var i = 0; i <= last; i++)
        {
            var body = blocks[i];
            if (body is null)
            {
                continue;
            }

            if (i != last && body.Length != ProdosLayout.BlockSize)
            {
                throw new ArgumentException("Only the last occupied block of an index may be shorter than 512 bytes.");
            }

            if (body.Length is < 1 or > ProdosLayout.BlockSize)
            {
                throw new ArgumentException("A data block is 1 to 512 bytes.");
            }

            var minimum = i == last ? minimumForLast : 0;
            var block = Allocate(used, minimum);
            body.CopyTo(image.AsSpan(block * ProdosLayout.BlockSize, body.Length));
            pointers[i] = block;
        }

        return pointers;
    }

    private void WriteDirectory(byte[] image, List<ProdosFixture.Entry> entries)
    {
        var directoryBlocks = ProdosLayout.DirectoryBlockCount(this.blockCount);
        for (var i = 0; i < directoryBlocks; i++)
        {
            var block = ProdosLayout.VolumeKeyBlock + i;
            var prev = i == 0 ? 0 : block - 1;
            var next = i == directoryBlocks - 1 ? 0 : block + 1;
            WriteU16(image.AsSpan(block * ProdosLayout.BlockSize), prev);
            WriteU16(image.AsSpan((block * ProdosLayout.BlockSize) + 2), next);
        }

        WriteVolumeHeader(image, entries.Count);
        var cursor = 0;
        cursor = WriteSlots(image, ProdosLayout.VolumeKeyBlock, startSlot: 1, slotCount: 12, entries, cursor);
        for (var i = 1; i < directoryBlocks && cursor < entries.Count; i++)
        {
            cursor = WriteSlots(image, ProdosLayout.VolumeKeyBlock + i, startSlot: 0, slotCount: ProdosLayout.Directory.EntriesPerBlock, entries, cursor);
        }

        if (cursor != entries.Count)
        {
            throw new InvalidOperationException("The ProDOS directory does not have room for every entry.");
        }
    }

    private int WriteSlots(byte[] image, int block, int startSlot, int slotCount, List<ProdosFixture.Entry> entries, int cursor)
    {
        for (var slot = startSlot; slot < startSlot + slotCount && cursor < entries.Count; slot++)
        {
            var entry = entries[cursor];
            var offset = (block * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset + (slot * ProdosLayout.Entry.Length);
            FileEntryBytes(entry, ProdosLayout.VolumeKeyBlock).CopyTo(image.AsSpan(offset, ProdosLayout.Entry.Length));
            if (entry.IsDirectory)
            {
                var entryNumber = slot + 1;
                var headerBase = (entry.KeyBlock * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset;
                image[headerBase + ProdosLayout.SubdirectoryHeader.ParentEntryNumberOffset] = (byte)entryNumber;
                WriteU16(image.AsSpan(headerBase + ProdosLayout.SubdirectoryHeader.ParentPointerOffset), block);
            }

            cursor++;
        }

        return cursor;
    }

    private void WriteVolumeHeader(byte[] image, int fileCount)
    {
        var header = image.AsSpan((ProdosLayout.VolumeKeyBlock * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset, ProdosLayout.Entry.Length);
        WriteName(header, ProdosStorageType.VolumeHeader, this.volumeName);
        header[ProdosLayout.Entry.AccessOffset] = ProdosLayout.AccessUnlocked;
        header[ProdosLayout.VolumeHeader.EntryLengthOffset] = ProdosLayout.Entry.Length;
        header[ProdosLayout.VolumeHeader.EntriesPerBlockOffset] = ProdosLayout.Directory.EntriesPerBlock;
        WriteU16(header[ProdosLayout.VolumeHeader.FileCountOffset..], fileCount);
        WriteU16(header[ProdosLayout.VolumeHeader.BitmapPointerOffset..], ProdosLayout.BitmapPointer(this.blockCount));
        WriteU16(header[ProdosLayout.VolumeHeader.TotalBlocksOffset..], this.blockCount);
    }

    private void WriteBitmap(byte[] image, bool[] used)
    {
        var pointer = ProdosLayout.BitmapPointer(this.blockCount);
        var bytes = ProdosLayout.BitmapBlockCount(this.blockCount) * ProdosLayout.BlockSize;
        var bitmap = new byte[bytes];
        for (var block = 0; block < this.blockCount; block++)
        {
            if (used[block])
            {
                continue;
            }

            bitmap[block >> 3] |= (byte)(0x80 >> (block & 7));
        }

        bitmap.CopyTo(image.AsSpan(pointer * ProdosLayout.BlockSize));
    }

    private void WriteSubdirectoryHeader(byte[] image, int key, string name, int fileCount)
    {
        var header = image.AsSpan((key * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset, ProdosLayout.Entry.Length);
        WriteName(header, ProdosStorageType.SubdirectoryHeader, name);
        header[ProdosLayout.SubdirectoryHeader.ReservedOffset] = ProdosLayout.SubdirectoryHeader.ReservedMark;
        header[ProdosLayout.Entry.AccessOffset] = ProdosLayout.AccessUnlocked;
        header[ProdosLayout.SubdirectoryHeader.EntryLengthOffset] = ProdosLayout.Entry.Length;
        header[ProdosLayout.SubdirectoryHeader.EntriesPerBlockOffset] = ProdosLayout.Directory.EntriesPerBlock;
        WriteU16(header[ProdosLayout.SubdirectoryHeader.FileCountOffset..], fileCount);
        header[ProdosLayout.SubdirectoryHeader.ParentEntryLengthOffset] = ProdosLayout.Entry.Length;
    }

    private byte[] FileEntryBytes(ProdosFixture.Entry entry, int headerBlock)
    {
        var bytes = new byte[ProdosLayout.Entry.Length];
        WriteName(bytes, StorageOf(entry.StorageKind), entry.Name);
        bytes[ProdosLayout.Entry.FileTypeOffset] = (byte)entry.FileType;
        WriteU16(bytes.AsSpan(ProdosLayout.Entry.KeyPointerOffset), entry.KeyBlock);
        WriteU16(bytes.AsSpan(ProdosLayout.Entry.BlocksUsedOffset), entry.AllocatedUnits);
        WriteU24(bytes.AsSpan(ProdosLayout.Entry.EofOffset), entry.EofBytes);
        bytes[ProdosLayout.Entry.AccessOffset] = entry.IsLocked ? ProdosLayout.AccessLocked : ProdosLayout.AccessUnlocked;
        WriteU16(bytes.AsSpan(ProdosLayout.Entry.AuxTypeOffset), entry.AuxType);
        WriteU16(bytes.AsSpan(ProdosLayout.Entry.HeaderPointerOffset), headerBlock);
        return bytes;
    }

    private ProdosStorageType StorageOf(FileStorageKind kind) => kind switch
    {
        FileStorageKind.ProdosSeedling => ProdosStorageType.Seedling,
        FileStorageKind.ProdosSapling => ProdosStorageType.Sapling,
        FileStorageKind.ProdosTree => ProdosStorageType.Tree,
        FileStorageKind.ProdosPascalArea => ProdosStorageType.PascalArea,
        FileStorageKind.ProdosExtended => ProdosStorageType.Extended,
        FileStorageKind.ProdosSubdirectory => ProdosStorageType.Subdirectory,
        FileStorageKind.None or FileStorageKind.DosTrackSectorList or FileStorageKind.PascalContiguous =>
            throw new InvalidOperationException("The fixture entry has no ProDOS storage type."),
        _ => throw new InvalidOperationException("The fixture entry has no ProDOS storage type."),
    };

    private void WriteForkMiniEntry(byte[] image, int keyBlock, int forkOffset, int dataBlock, int eof)
    {
        var span = image.AsSpan((keyBlock * ProdosLayout.BlockSize) + forkOffset);
        span[ProdosLayout.ExtendedKeyBlock.StorageOffset] = (byte)((byte)ProdosStorageType.Seedling << 4);
        WriteU16(span[ProdosLayout.ExtendedKeyBlock.KeyPointerOffset..], dataBlock);
        WriteU16(span[ProdosLayout.ExtendedKeyBlock.BlocksUsedOffset..], 1);
        WriteU24(span[ProdosLayout.ExtendedKeyBlock.EofOffset..], eof);
    }

    private int ResolveEof(int naturalLength, int? requested, int maximum = 0xFFFFFF)
    {
        if (requested is null)
        {
            return naturalLength;
        }

        if (requested.Value < naturalLength || requested.Value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "EOF must cover the allocated bytes and stay within the storage kind.");
        }

        return requested.Value;
    }

    private byte[] PadToEof(byte[] natural, int eof)
    {
        if (natural.Length == eof)
        {
            return natural;
        }

        var padded = new byte[eof];
        natural.CopyTo(padded, 0);
        return padded;
    }

    private void WriteIndex(byte[] image, int block, IReadOnlyList<int> pointers)
    {
        var span = image.AsSpan(block * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        for (var i = 0; i < pointers.Count; i++)
        {
            span[ProdosLayout.Index.LowHalf + i] = (byte)pointers[i];
            span[ProdosLayout.Index.HighHalf + i] = (byte)(pointers[i] >> 8);
        }
    }

    private byte[] LogicalBytes(IReadOnlyList<byte[]?> blocks, IReadOnlyList<int> pointers)
    {
        var last = pointers.Count - 1;
        while (last >= 0 && pointers[last] == 0)
        {
            last--;
        }

        if (last < 0)
        {
            return [];
        }

        var raw = new byte[(last * ProdosLayout.BlockSize) + blocks[last]!.Length];
        for (var i = 0; i <= last; i++)
        {
            if (blocks[i] is null)
            {
                continue;
            }

            blocks[i]!.CopyTo(raw.AsSpan(i * ProdosLayout.BlockSize));
        }

        return raw;
    }

    private int LastOccupied(IReadOnlyList<byte[]?> blocks)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i] is not null)
            {
                return i;
            }
        }

        throw new ArgumentException("An index needs one occupied block.");
    }

    private int Allocate(bool[] used, int minimum)
    {
        for (var block = Math.Max(minimum, 0); block < used.Length; block++)
        {
            if (!used[block])
            {
                used[block] = true;
                return block;
            }
        }

        throw new InvalidOperationException("The ProDOS image ran out of blocks.");
    }

    private ProdosFixture.Entry ToEntry(Pending pending, string name, int keyBlock, ReadOnlyMemory<byte> raw)
    {
        var path = pending.DirectoryPath.Length == 0 ? name : AppleFileSystemPaths.Child(pending.DirectoryPath, name);
        return new ProdosFixture.Entry(
            name,
            path,
            pending.FileType,
            pending.AuxType,
            raw.Length,
            1,
            pending.Locked,
            false,
            FileStorageKind.ProdosSeedling,
            keyBlock,
            Array.Empty<int>(),
            Array.Empty<int>(),
            raw,
            Array.Empty<ProdosFixture.Entry>());
    }

    private void WriteName(Span<byte> entry, ProdosStorageType storage, string name)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(name);
        entry[0] = (byte)(((byte)storage << 4) | bytes.Length);
        bytes.CopyTo(entry[ProdosLayout.Entry.NameOffset..]);
    }

    private string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var upper = name.ToUpperInvariant();
        if (!ProdosLayout.IsValidName(System.Text.Encoding.ASCII.GetBytes(upper)))
        {
            throw new ArgumentException("ProDOS names are 1–15 characters: a letter, then letters, digits, or periods.", nameof(name));
        }

        return upper;
    }

    private void WriteU16(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
    }

    private void WriteU24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private sealed class Pending
    {
        public Pending(string name, ProdosFileType fileType, ushort auxType, bool locked, PendingKind kind, IReadOnlyList<byte[]?> blocks, int minimumBlockForLast, int subIndexMinimumBlock)
        {
            this.Name = name;
            this.FileType = fileType;
            this.AuxType = auxType;
            this.Locked = locked;
            this.Kind = kind;
            this.Blocks = blocks;
            this.MinimumBlockForLast = minimumBlockForLast;
            this.SubIndexMinimumBlock = subIndexMinimumBlock;
            this.Seedling = [];
            this.Children = Array.Empty<ProdosFixture.SeedlingSpec>();
            this.DirectoryPath = string.Empty;
            this.HeaderBlock = ProdosLayout.VolumeKeyBlock;
        }

        public string Name { get; }

        public ProdosFileType FileType { get; }

        public ushort AuxType { get; }

        public bool Locked { get; }

        public PendingKind Kind { get; }

        public IReadOnlyList<byte[]?> Blocks { get; }

        public int MinimumBlockForLast { get; }

        public int SubIndexMinimumBlock { get; }

        public byte[] Seedling { get; set; }

        public IReadOnlyList<ProdosFixture.SeedlingSpec> Children { get; set; }

        public string DirectoryPath { get; set; }

        public int HeaderBlock { get; set; }

        public int? ExplicitEof { get; set; }

        public int MasterSlot { get; set; }
    }
}