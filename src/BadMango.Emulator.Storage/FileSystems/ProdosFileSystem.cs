// <copyright file="ProdosFileSystem.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Read-only ProDOS volume over an <see cref="IBlockReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The volume directory header and subdirectory headers are not files. Directory
/// blocks are followed by their next-block pointer. A repeated block is
/// <see cref="FileAccessStatus.CorruptStructure"/>. The reader does not own the
/// media and does not write.
/// </para>
/// <para>
/// Seedling data is the key block. Sapling and tree indexes are read with
/// <see cref="ProdosLayout.ReadIndexPointer"/>: byte <c>i</c> is the low byte and
/// byte <c>256 + i</c> is the high byte. A zero pointer is a sparse hole of zeros,
/// not the end of the file. The returned buffer length is the EOF field.
/// Storage types <c>$4</c> and <c>$5</c> return
/// <see cref="FileAccessStatus.UnsupportedStorage"/> and no bytes.
/// </para>
/// <para>
/// Names are the on-disk bytes unless the ProDOS 8 Tech Note #8 case-bit word is valid
/// (bit 15 set). Tech Note #8 numbers the volume word <c>+$1A</c>/<c>+$1B</c> from the
/// start of the key block, so the entry-relative offset is
/// <see cref="ProdosLayout.VolumeHeader.CaseBitsOffset"/> (<c>$16</c>), inside the reserved
/// bytes. Entry <c>$18</c>–<c>$1B</c> is the creation date and time. File and subdirectory
/// entries store case bits at entry offset <c>$1C</c>. Lookups ignore case. ProDOS logical
/// reads are the same bytes as raw reads.
/// </para>
/// <para>
/// One instance is not safe for concurrent calls. <see cref="ListDirectory"/> and
/// <see cref="TryReadFile"/> share the block, index, and master-index buffers.
/// </para>
/// </remarks>
public sealed class ProdosFileSystem : IAppleFileSystem
{
    /// <summary>Entry-relative case-bit word in a file or subdirectory entry. ProDOS 8 Tech Note #8.</summary>
    private const int FileEntryCaseBitsOffset = 0x1C;

    /// <summary>Bit 15 of the case-bit word. Clear means the on-disk bytes are the name.</summary>
    private const int CaseBitsValidMask = 0x8000;

    private readonly ILogger logger;
    private readonly IBlockReader blocks;
    private readonly int totalBlocks;
    private readonly byte[] blockBuffer;
    private readonly byte[] indexBuffer;
    private readonly byte[] masterBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProdosFileSystem"/> class.
    /// </summary>
    /// <param name="blocks">Block view of the volume. Not owned and not disposed.</param>
    /// <param name="logger">Logger for open, unsupported storage, and corrupt structure. Not used per block.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="blocks"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If the image is not a ProDOS volume directory.</exception>
    public ProdosFileSystem(IBlockReader blocks, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(logger);
        if (blocks.BlockSize != ProdosLayout.BlockSize)
        {
            throw new ArgumentException("ProDOS blocks are 512 bytes.", nameof(blocks));
        }

        if (blocks.BlockCount <= ProdosLayout.VolumeKeyBlock)
        {
            throw new ArgumentException("The image does not contain a ProDOS volume key block.", nameof(blocks));
        }

        this.blocks = blocks;
        this.logger = logger.ForContext<ProdosFileSystem>();
        this.blockBuffer = new byte[ProdosLayout.BlockSize];
        this.indexBuffer = new byte[ProdosLayout.BlockSize];
        this.masterBuffer = new byte[ProdosLayout.BlockSize];
        this.blocks.ReadBlock(ProdosLayout.VolumeKeyBlock, this.blockBuffer);
        if (!ProdosLayout.IsVolumeDirectoryKeyBlock(this.blockBuffer, this.blocks.BlockCount))
        {
            throw new ArgumentException("Block 2 is not a ProDOS volume directory key block.", nameof(blocks));
        }

        var header = this.blockBuffer.AsSpan(ProdosLayout.Directory.FirstEntryOffset, ProdosLayout.Entry.Length);
        this.VolumeName = DecodeName(header, header[0] & 0x0F, ProdosLayout.VolumeHeader.CaseBitsOffset);
        var bitmapPointer = header[ProdosLayout.VolumeHeader.BitmapPointerOffset]
            | (header[ProdosLayout.VolumeHeader.BitmapPointerOffset + 1] << 8);
        this.totalBlocks = header[ProdosLayout.VolumeHeader.TotalBlocksOffset]
            | (header[ProdosLayout.VolumeHeader.TotalBlocksOffset + 1] << 8);
        this.Allocation = this.ReadAllocation(bitmapPointer);
        this.logger.Debug(
            "Opened ProDOS volume {VolumeName} with {BlockCount} blocks.",
            this.VolumeName,
            this.blocks.BlockCount);
    }

    /// <inheritdoc />
    public string VolumeName { get; }

    /// <inheritdoc />
    public AppleFileSystemKind Kind => AppleFileSystemKind.Prodos;

    /// <inheritdoc />
    public VolumeSpace Allocation { get; }

    /// <inheritdoc />
    public DirectoryListing ListDirectory(string? path)
    {
        if (!AppleFileSystemPaths.TryParse(path, this.VolumeName, AppleFileSystemPathStyle.Hierarchical, out var segments, out var reason))
        {
            return DirectoryListing.Failure(FileAccessStatus.InvalidPath, reason);
        }

        if (!this.TryReadDirectory(ProdosLayout.VolumeKeyBlock, string.Empty, out var entries, out reason))
        {
            return DirectoryListing.Failure(FileAccessStatus.CorruptStructure, reason);
        }

        foreach (var segment in segments)
        {
            var match = Find(entries, segment);
            if (match is null)
            {
                return DirectoryListing.Failure(FileAccessStatus.NotFound, "The path does not name a directory.");
            }

            if (match.Value.Storage != ProdosStorageType.Subdirectory)
            {
                return DirectoryListing.Failure(FileAccessStatus.NotADirectory, "The path names a file.");
            }

            if (!this.TryReadDirectory(match.Value.KeyBlock, match.Value.Entry.Path, out entries, out reason))
            {
                return DirectoryListing.Failure(FileAccessStatus.CorruptStructure, reason);
            }
        }

        var listed = new FileEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            listed[i] = entries[i].Entry;
        }

        return DirectoryListing.Success(listed);
    }

    /// <inheritdoc />
    public FileReadResult TryReadFile(string path, FileReadView view)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Enum.IsDefined(view))
        {
            throw new ArgumentOutOfRangeException(nameof(view));
        }

        if (!this.TryResolve(path, out var item, out var status, out var reason))
        {
            return FileReadResult.Failure(status, reason);
        }

        if (item.Storage == ProdosStorageType.Subdirectory)
        {
            return FileReadResult.Failure(FileAccessStatus.IsDirectory, "The path names a directory.");
        }

        if (item.Storage == ProdosStorageType.Extended)
        {
            this.logger.Information("ProDOS storage type $5 is not read for {Path}.", item.Entry.Path);
            return FileReadResult.Failure(FileAccessStatus.UnsupportedStorage, "Storage type $5 is not read.");
        }

        if (item.Storage == ProdosStorageType.PascalArea)
        {
            this.logger.Information("ProDOS storage type $4 is not read for {Path}.", item.Entry.Path);
            return FileReadResult.Failure(FileAccessStatus.UnsupportedStorage, "Storage type $4 is not read.");
        }

        var result = item.Storage switch
        {
            ProdosStorageType.Seedling => this.ReadSeedling(item.KeyBlock, item.Entry.EofBytes),
            ProdosStorageType.Sapling => this.ReadSapling(item.KeyBlock, item.Entry.EofBytes),
            ProdosStorageType.Tree => this.ReadTree(item.KeyBlock, item.Entry.EofBytes),
            _ => FileReadResult.Failure(FileAccessStatus.UnsupportedStorage, "The storage type is not read."),
        };
        if (result.Succeeded)
        {
            this.logger.Debug("Read ProDOS file {Path} ({Length} bytes).", item.Entry.Path, result.Data.Length);
        }

        return result;
    }

    /// <inheritdoc />
    public FileEntry? GetFileInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!this.TryResolve(path, out var item, out _, out _))
        {
            return null;
        }

        return item.Entry;
    }

    private static bool IsSubdirectoryKeyBlock(ReadOnlySpan<byte> block)
    {
        var header = ProdosLayout.Directory.FirstEntryOffset;
        if ((block[header] >> 4) != (byte)ProdosStorageType.SubdirectoryHeader)
        {
            return false;
        }

        return block[header + ProdosLayout.SubdirectoryHeader.EntryLengthOffset] == ProdosLayout.Entry.Length
            && block[header + ProdosLayout.SubdirectoryHeader.EntriesPerBlockOffset] == ProdosLayout.Directory.EntriesPerBlock;
    }

    private static string DecodeName(ReadOnlySpan<byte> entry, int nameLength, int caseBitsOffset)
    {
        var caseField = entry[caseBitsOffset] | (entry[caseBitsOffset + 1] << 8);
        var applyCase = (caseField & CaseBitsValidMask) != 0;
        var chars = new char[nameLength];
        for (var i = 0; i < nameLength; i++)
        {
            var c = (char)entry[ProdosLayout.Entry.NameOffset + i];
            if (applyCase && c is >= 'A' and <= 'Z' && (caseField & (1 << (14 - i))) != 0)
            {
                c = (char)(c - 'A' + 'a');
            }

            chars[i] = c;
        }

        return new string(chars);
    }

    private static CatalogItem? Find(List<CatalogItem> entries, string segment)
    {
        foreach (var entry in entries)
        {
            if (entry.Entry.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static int ReadU16(ReadOnlySpan<byte> entry, int offset) => entry[offset] | (entry[offset + 1] << 8);

    private static int ReadU24(ReadOnlySpan<byte> entry, int offset) =>
        entry[offset] | (entry[offset + 1] << 8) | (entry[offset + 2] << 16);

    private bool TryResolve(string path, out CatalogItem item, out FileAccessStatus status, out string reason)
    {
        item = default;
        if (!AppleFileSystemPaths.TryParse(path, this.VolumeName, AppleFileSystemPathStyle.Hierarchical, out var segments, out reason))
        {
            status = FileAccessStatus.InvalidPath;
            return false;
        }

        if (segments.Count == 0)
        {
            status = FileAccessStatus.IsDirectory;
            reason = "The path names a directory.";
            return false;
        }

        if (!this.TryReadDirectory(ProdosLayout.VolumeKeyBlock, string.Empty, out var entries, out reason))
        {
            status = FileAccessStatus.CorruptStructure;
            return false;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var match = Find(entries, segments[i]);
            if (match is null)
            {
                status = FileAccessStatus.NotFound;
                reason = "The path does not name a file.";
                return false;
            }

            var last = i == segments.Count - 1;
            if (!last)
            {
                if (match.Value.Storage != ProdosStorageType.Subdirectory)
                {
                    status = FileAccessStatus.NotADirectory;
                    reason = "The path names a file.";
                    return false;
                }

                if (!this.TryReadDirectory(match.Value.KeyBlock, match.Value.Entry.Path, out entries, out reason))
                {
                    status = FileAccessStatus.CorruptStructure;
                    return false;
                }

                continue;
            }

            item = match.Value;
            status = FileAccessStatus.Success;
            reason = string.Empty;
            return true;
        }

        status = FileAccessStatus.NotFound;
        reason = "The path does not name a file.";
        return false;
    }

    private bool TryReadDirectory(int keyBlock, string directoryPath, out List<CatalogItem> entries, out string reason)
    {
        entries = new List<CatalogItem>();
        reason = string.Empty;
        if ((uint)keyBlock >= (uint)this.totalBlocks || keyBlock == 0)
        {
            reason = "A directory key block is outside the volume.";
            this.logger.Warning("ProDOS directory key block {Block} is outside the volume.", keyBlock);
            return false;
        }

        var seen = new HashSet<int>();
        var block = keyBlock;
        var first = true;
        while (block != 0)
        {
            if (!seen.Add(block))
            {
                reason = "The directory block chain loops.";
                this.logger.Warning("ProDOS directory block chain loops at block {Block}.", block);
                return false;
            }

            if ((uint)block >= (uint)this.totalBlocks)
            {
                reason = "A directory block is outside the volume.";
                this.logger.Warning("ProDOS directory block {Block} is outside the volume.", block);
                return false;
            }

            this.blocks.ReadBlock(block, this.blockBuffer);
            if (first && keyBlock != ProdosLayout.VolumeKeyBlock && !IsSubdirectoryKeyBlock(this.blockBuffer))
            {
                reason = "A subdirectory key block does not start with a subdirectory header.";
                this.logger.Warning("ProDOS block {Block} is not a subdirectory header.", block);
                return false;
            }

            var next = this.blockBuffer[2] | (this.blockBuffer[3] << 8);
            var start = first ? 1 : 0;
            for (var slot = start; slot < ProdosLayout.Directory.EntriesPerBlock; slot++)
            {
                var offset = ProdosLayout.Directory.FirstEntryOffset + (slot * ProdosLayout.Entry.Length);
                this.AddEntry(this.blockBuffer.AsSpan(offset, ProdosLayout.Entry.Length), directoryPath, entries);
            }

            block = next;
            first = false;
        }

        return true;
    }

    private void AddEntry(ReadOnlySpan<byte> entry, string directoryPath, List<CatalogItem> entries)
    {
        var storageNibble = (byte)(entry[0] >> 4);
        var storage = (ProdosStorageType)storageNibble;
        if (!Enum.IsDefined(storage))
        {
            return;
        }

        var kind = ProdosLayout.ToStorageKind(storage);
        if (kind == FileStorageKind.None)
        {
            return;
        }

        var nameLength = entry[0] & 0x0F;
        if (nameLength == 0)
        {
            return;
        }

        var name = DecodeName(entry, nameLength, FileEntryCaseBitsOffset);
        var path = AppleFileSystemPaths.Child(directoryPath, name);
        var eof = ReadU24(entry, ProdosLayout.Entry.EofOffset);
        var allocated = ReadU16(entry, ProdosLayout.Entry.BlocksUsedOffset);
        var aux = (ushort)ReadU16(entry, ProdosLayout.Entry.AuxTypeOffset);
        var created = ProdosTimestamp.Decode(
            (ushort)ReadU16(entry, ProdosLayout.Entry.CreationDateOffset),
            (ushort)ReadU16(entry, ProdosLayout.Entry.CreationTimeOffset));
        var modified = ProdosTimestamp.Decode(
            (ushort)ReadU16(entry, ProdosLayout.Entry.ModificationDateOffset),
            (ushort)ReadU16(entry, ProdosLayout.Entry.ModificationTimeOffset));
        var file = new FileEntry(
            path,
            name,
            entry[ProdosLayout.Entry.FileTypeOffset],
            aux,
            eof,
            allocated,
            ProdosLayout.BlockSize,
            ProdosLayout.IsLocked(entry[ProdosLayout.Entry.AccessOffset]),
            storage == ProdosStorageType.Subdirectory,
            kind,
            displayName: null,
            created: created,
            modified: modified);
        entries.Add(new CatalogItem(file, ReadU16(entry, ProdosLayout.Entry.KeyPointerOffset), storage));
    }

    private FileReadResult ReadSeedling(int keyBlock, int eof)
    {
        if (eof == 0)
        {
            return FileReadResult.Success(ReadOnlyMemory<byte>.Empty);
        }

        if (eof > ProdosLayout.BlockSize)
        {
            return this.Corrupt("A seedling EOF is at most 512 bytes.");
        }

        if (keyBlock == 0)
        {
            return this.Corrupt("A file key pointer of zero is the boot block.");
        }

        var data = new byte[eof];
        var invalid = this.PointerOutOfRange(keyBlock);
        if (invalid is not null)
        {
            return invalid;
        }

        this.blocks.ReadBlock(keyBlock, this.blockBuffer);
        var count = Math.Min(eof, ProdosLayout.BlockSize);
        this.blockBuffer.AsSpan(0, count).CopyTo(data);
        return FileReadResult.Success(data);
    }

    private FileReadResult ReadSapling(int keyBlock, int eof)
    {
        if (eof == 0)
        {
            return FileReadResult.Success(ReadOnlyMemory<byte>.Empty);
        }

        if (eof > ProdosLayout.Index.PointerCount * ProdosLayout.BlockSize)
        {
            return this.Corrupt("A sapling EOF is at most 131072 bytes.");
        }

        if (keyBlock == 0)
        {
            return this.Corrupt("A file key pointer of zero is the boot block.");
        }

        var data = new byte[eof];
        var invalid = this.PointerOutOfRange(keyBlock);
        if (invalid is not null)
        {
            return invalid;
        }

        this.blocks.ReadBlock(keyBlock, this.indexBuffer);
        return this.CopyIndex(this.indexBuffer, data, 0, eof) ?? FileReadResult.Success(data);
    }

    private FileReadResult ReadTree(int keyBlock, int eof)
    {
        if (eof == 0)
        {
            return FileReadResult.Success(ReadOnlyMemory<byte>.Empty);
        }

        if (keyBlock == 0)
        {
            return this.Corrupt("A file key pointer of zero is the boot block.");
        }

        var data = new byte[eof];
        var invalid = this.PointerOutOfRange(keyBlock);
        if (invalid is not null)
        {
            return invalid;
        }

        this.blocks.ReadBlock(keyBlock, this.masterBuffer);
        var slotBytes = ProdosLayout.Index.SlotBytes;
        for (var slot = 0; (slot * slotBytes) < eof && slot < ProdosLayout.Index.PointerCount; slot++)
        {
            var pointer = ProdosLayout.ReadIndexPointer(this.masterBuffer, slot);
            var slotStart = slot * slotBytes;
            var length = Math.Min(slotBytes, eof - slotStart);
            if (pointer == 0)
            {
                continue;
            }

            invalid = this.PointerOutOfRange(pointer);
            if (invalid is not null)
            {
                return invalid;
            }

            this.blocks.ReadBlock(pointer, this.indexBuffer);
            var failed = this.CopyIndex(this.indexBuffer, data, slotStart, length);
            if (failed is not null)
            {
                return failed;
            }
        }

        return FileReadResult.Success(data);
    }

    private FileReadResult? CopyIndex(ReadOnlySpan<byte> index, byte[] destination, int destinationOffset, int length)
    {
        var blocksToCopy = (length + ProdosLayout.BlockSize - 1) / ProdosLayout.BlockSize;
        if (blocksToCopy > ProdosLayout.Index.PointerCount)
        {
            blocksToCopy = ProdosLayout.Index.PointerCount;
        }

        for (var i = 0; i < blocksToCopy; i++)
        {
            var pointer = ProdosLayout.ReadIndexPointer(index, i);
            if (pointer == 0)
            {
                continue;
            }

            var invalid = this.PointerOutOfRange(pointer);
            if (invalid is not null)
            {
                return invalid;
            }

            this.blocks.ReadBlock(pointer, this.blockBuffer);
            var offset = destinationOffset + (i * ProdosLayout.BlockSize);
            var count = Math.Min(ProdosLayout.BlockSize, destinationOffset + length - offset);
            this.blockBuffer.AsSpan(0, count).CopyTo(destination.AsSpan(offset));
        }

        return null;
    }

    private VolumeSpace ReadAllocation(int bitmapPointer)
    {
        var bitmapBlocks = ProdosLayout.BitmapBlockCount(this.totalBlocks);
        var bitmap = new byte[bitmapBlocks * ProdosLayout.BlockSize];
        for (var index = 0; index < bitmapBlocks; index++)
        {
            this.blocks.ReadBlock(bitmapPointer + index, this.blockBuffer);
            this.blockBuffer.AsSpan().CopyTo(bitmap.AsSpan(index * ProdosLayout.BlockSize));
        }

        return ProdosLayout.CountFreeBlocks(bitmap, this.totalBlocks);
    }

    private FileReadResult? PointerOutOfRange(int block)
    {
        if ((uint)block >= (uint)this.totalBlocks)
        {
            return this.Corrupt("A block pointer is outside the volume.");
        }

        return null;
    }

    private FileReadResult Corrupt(string reason)
    {
        this.logger.Warning("ProDOS file structure is corrupt: {Reason}", reason);
        return FileReadResult.Failure(FileAccessStatus.CorruptStructure, reason);
    }

    private readonly struct CatalogItem
    {
        public CatalogItem(FileEntry entry, int keyBlock, ProdosStorageType storage)
        {
            this.Entry = entry;
            this.KeyBlock = keyBlock;
            this.Storage = storage;
        }

        public FileEntry Entry { get; }

        public int KeyBlock { get; }

        public ProdosStorageType Storage { get; }
    }
}