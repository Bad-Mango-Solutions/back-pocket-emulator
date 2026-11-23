// <copyright file="PascalFileSystem.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Read-only Apple Pascal (UCSD) volume over an <see cref="IBlockReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The probe calls <see cref="PascalLayout.IsVolumeHeader(System.ReadOnlySpan{byte}, int)"/> and
/// returns <see cref="FileSystemProbeAttempt.Reject(AppleFileSystemKind, string)"/> when that check
/// is false. This constructor repeats the check and throws <see cref="ArgumentException"/> when a
/// caller skips it. Neither type catches. A block-read
/// <see cref="ArgumentOutOfRangeException"/>, <see cref="InvalidOperationException"/>,
/// <see cref="IOException"/>, or <see cref="ObjectDisposedException"/> propagates.
/// </para>
/// <para>
/// Directory records are <see cref="PascalLayout.EntryLength"/> bytes packed across blocks
/// <see cref="PascalLayout.DirectoryStartBlock"/> through 5. The volume header is not a
/// <see cref="FileEntry"/>. File length is <see cref="PascalLayout.FileLength(int, int, int)"/>.
/// The file type is <see cref="PascalLayout.KindMask"/> of the kind word, so
/// <see cref="PascalLayout.FilerWildcardBit"/> is not part of the type.
/// <see cref="PascalFileKind.Secure"/> is a contiguous file, not a directory. A counted record
/// is <see cref="FileAccessStatus.CorruptStructure"/> when its name is not 1–15 bytes in
/// <c>0x20</c>–<c>0x7E</c>, when its end block is not past its start, when the last block does
/// not use 1 through <see cref="PascalLayout.BlockSize"/> bytes, when its start is before the
/// volume header's next block or the previous file's end, or when the span falls outside the volume.
/// </para>
/// <para>
/// Paths use <see cref="AppleFileSystemPathStyle.Flat"/>. Logical reads return the same bytes as
/// raw reads. One block buffer is reused for file bytes. The instance is not safe for concurrent
/// calls. It does not own or dispose the <see cref="IBlockReader"/>.
/// </para>
/// </remarks>
public sealed class PascalFileSystem : IAppleFileSystem
{
    private readonly ILogger logger;
    private readonly IBlockReader blocks;
    private readonly byte[] blockBuffer;
    private readonly CatalogFile[] files;
    private readonly string? catalogError;

    /// <summary>
    /// Initializes a new instance of the <see cref="PascalFileSystem"/> class.
    /// </summary>
    /// <param name="logger">Logger for open and failed lookups. Not used for each block.</param>
    /// <param name="blocks">Pascal block image. Not owned and not disposed.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> or <paramref name="blocks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="blocks"/> is not an Apple Pascal volume.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If a directory block read is out of range.</exception>
    /// <exception cref="InvalidOperationException">If the block reader refuses the directory read.</exception>
    /// <exception cref="IOException">If the backing store cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">If the block reader has been disposed.</exception>
    public PascalFileSystem(ILogger logger, IBlockReader blocks)
        : this(logger, blocks, ReadDirectoryForOpen(logger, blocks))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PascalFileSystem"/> class from a directory that was already read.
    /// </summary>
    /// <param name="logger">Logger for open and failed lookups. Not used for each block.</param>
    /// <param name="blocks">Pascal block image. Not owned and not disposed. File bytes are read from it later.</param>
    /// <param name="directory">
    /// The <see cref="PascalLayout.DirectoryBytes"/> directory from blocks 2–5. This constructor does not read it again.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> or <paramref name="blocks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="blocks"/> or <paramref name="directory"/> is not an Apple Pascal volume.</exception>
    internal PascalFileSystem(ILogger logger, IBlockReader blocks, ReadOnlySpan<byte> directory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.BlockSize != PascalLayout.BlockSize || blocks.BlockCount < PascalLayout.FirstDataBlock)
        {
            throw new ArgumentException("The block image is not an Apple Pascal volume.", nameof(blocks));
        }

        if (directory.Length != PascalLayout.DirectoryBytes || !PascalLayout.IsVolumeHeader(directory, blocks.BlockCount))
        {
            throw new ArgumentException("The block image is not an Apple Pascal volume.", nameof(directory));
        }

        this.logger = logger.ForContext<PascalFileSystem>();
        this.blocks = blocks;
        this.blockBuffer = new byte[PascalLayout.BlockSize];
        this.VolumeName = ReadName(directory.Slice(PascalLayout.VolumeHeader.NameOffset, PascalLayout.VolumeHeader.NameCapacity + 1));
        this.files = ParseFiles(directory, blocks.BlockCount, out var error);
        this.catalogError = error;
        if (this.catalogError is null)
        {
            this.logger.Information(
                "Opened Apple Pascal volume {VolumeName} with {FileCount} files in {BlockCount} blocks.",
                this.VolumeName,
                this.files.Length,
                blocks.BlockCount);
        }
        else
        {
            this.logger.Warning(
                "Opened Apple Pascal volume {VolumeName} with a corrupt directory: {Reason}",
                this.VolumeName,
                this.catalogError);
        }
    }

    /// <inheritdoc />
    public string VolumeName { get; }

    /// <inheritdoc />
    public AppleFileSystemKind Kind => AppleFileSystemKind.Pascal;

    /// <inheritdoc />
    public DirectoryListing ListDirectory(string? path)
    {
        if (!AppleFileSystemPaths.TryParse(path, this.VolumeName, AppleFileSystemPathStyle.Flat, out var segments, out var reason))
        {
            return this.FailList(FileAccessStatus.InvalidPath, reason);
        }

        if (this.catalogError is not null)
        {
            return this.FailList(FileAccessStatus.CorruptStructure, this.catalogError);
        }

        if (segments.Count == 0)
        {
            var listed = new FileEntry[this.files.Length];
            for (var i = 0; i < this.files.Length; i++)
            {
                listed[i] = this.files[i].Entry;
            }

            return DirectoryListing.Success(listed);
        }

        if (!this.TryFind(segments[0], out var file))
        {
            return this.FailList(FileAccessStatus.NotFound, $"'{segments[0]}' was not found.");
        }

        return this.FailList(FileAccessStatus.NotADirectory, $"'{file.Entry.Name}' is a file.");
    }

    /// <inheritdoc />
    public FileReadResult TryReadFile(string path, FileReadView view)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (view != FileReadView.Raw && view != FileReadView.Logical)
        {
            throw new ArgumentOutOfRangeException(nameof(view));
        }

        if (!AppleFileSystemPaths.TryParse(path, this.VolumeName, AppleFileSystemPathStyle.Flat, out var segments, out var reason))
        {
            return this.FailRead(FileAccessStatus.InvalidPath, reason);
        }

        if (segments.Count == 0)
        {
            return this.FailRead(FileAccessStatus.IsDirectory, "The path names the volume.");
        }

        if (this.catalogError is not null)
        {
            return this.FailRead(FileAccessStatus.CorruptStructure, this.catalogError);
        }

        if (!this.TryFind(segments[0], out var file))
        {
            return this.FailRead(FileAccessStatus.NotFound, $"'{segments[0]}' was not found.");
        }

        return FileReadResult.Success(this.ReadBytes(file));
    }

    /// <inheritdoc />
    public FileEntry? GetFileInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!AppleFileSystemPaths.TryParse(path, this.VolumeName, AppleFileSystemPathStyle.Flat, out var segments, out _))
        {
            return null;
        }

        if (segments.Count == 0 || this.catalogError is not null)
        {
            return null;
        }

        return this.TryFind(segments[0], out var file) ? file.Entry : null;
    }

    /// <summary>
    /// Copies directory blocks 2–5 into <paramref name="directory"/>.
    /// </summary>
    /// <param name="blocks">Block image whose block size is 512.</param>
    /// <param name="directory">Destination. Its length is <see cref="PascalLayout.DirectoryBytes"/>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="blocks"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="directory"/> is not 2048 bytes or the block size is not 512.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If a directory block read is out of range.</exception>
    /// <exception cref="InvalidOperationException">If the block reader refuses the read.</exception>
    /// <exception cref="IOException">If the backing store cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">If the block reader has been disposed.</exception>
    internal static void ReadDirectory(IBlockReader blocks, Span<byte> directory)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (directory.Length != PascalLayout.DirectoryBytes)
        {
            throw new ArgumentException("The Pascal directory is 2048 bytes.", nameof(directory));
        }

        if (blocks.BlockSize != PascalLayout.BlockSize)
        {
            throw new ArgumentException("Apple Pascal blocks are 512 bytes.", nameof(blocks));
        }

        var block = new byte[PascalLayout.BlockSize];
        for (var index = 0; index < PascalLayout.DirectoryBlockCount; index++)
        {
            blocks.ReadBlock(PascalLayout.DirectoryStartBlock + index, block);
            block.AsSpan().CopyTo(directory.Slice(index * PascalLayout.BlockSize, PascalLayout.BlockSize));
        }
    }

    private static byte[] ReadDirectoryForOpen(ILogger logger, IBlockReader blocks)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.BlockSize != PascalLayout.BlockSize || blocks.BlockCount < PascalLayout.FirstDataBlock)
        {
            throw new ArgumentException("The block image is not an Apple Pascal volume.", nameof(blocks));
        }

        var directory = new byte[PascalLayout.DirectoryBytes];
        ReadDirectory(blocks, directory);
        return directory;
    }

    private static CatalogFile[] ParseFiles(ReadOnlySpan<byte> directory, int blockCount, out string? error)
    {
        var fileCount = ReadU16(directory, PascalLayout.VolumeHeader.FileCountOffset);
        var firstDataBlock = ReadU16(directory, PascalLayout.VolumeHeader.NextBlockOffset);
        var previousEnd = firstDataBlock;
        var parsed = new CatalogFile[fileCount];
        for (var i = 0; i < fileCount; i++)
        {
            var record = directory.Slice((i + 1) * PascalLayout.EntryLength, PascalLayout.EntryLength);
            if (!TryParseFile(record, blockCount, firstDataBlock, previousEnd, i + 1, out parsed[i], out error))
            {
                return Array.Empty<CatalogFile>();
            }

            previousEnd = parsed[i].EndBlock;
        }

        error = null;
        return parsed;
    }

    private static bool TryParseFile(
        ReadOnlySpan<byte> record,
        int blockCount,
        int firstDataBlock,
        int previousEnd,
        int recordNumber,
        out CatalogFile file,
        out string? error)
    {
        file = default;
        var start = ReadU16(record, PascalLayout.DirectoryEntry.FirstBlockOffset);
        var end = ReadU16(record, PascalLayout.DirectoryEntry.NextBlockOffset);
        if (!TryReadName(record, out var name, out var nameError))
        {
            error = $"Pascal directory file {recordNumber} {nameError}";
            return false;
        }

        if (end <= start)
        {
            error = $"Pascal directory file {recordNumber} does not end after it starts.";
            return false;
        }

        var bytesInLast = ReadU16(record, PascalLayout.DirectoryEntry.LastBlockBytesOffset);
        if (bytesInLast < 1 || bytesInLast > PascalLayout.BlockSize)
        {
            error = $"Pascal directory file {recordNumber} must use 1 to {PascalLayout.BlockSize} bytes in its last block.";
            return false;
        }

        if (start < firstDataBlock)
        {
            error = $"Pascal directory file {recordNumber} points into the boot or directory blocks.";
            return false;
        }

        if (start < previousEnd)
        {
            error = $"Pascal directory file {recordNumber} overlaps the previous entry.";
            return false;
        }

        if (start >= blockCount || end > blockCount)
        {
            error = $"Pascal directory file {recordNumber} points outside the volume.";
            return false;
        }

        var length = PascalLayout.FileLength(start, end, bytesInLast);
        var entry = new FileEntry(
            name,
            name,
            (byte)PascalLayout.FileKind(ReadU16(record, PascalLayout.DirectoryEntry.KindOffset)),
            auxType: 0,
            length,
            end - start,
            PascalLayout.BlockSize,
            isLocked: false,
            isDirectory: false,
            FileStorageKind.PascalContiguous);
        file = new CatalogFile(entry, start, end);
        error = null;
        return true;
    }

    private static bool TryReadName(ReadOnlySpan<byte> record, out string name, out string error)
    {
        var length = record[PascalLayout.DirectoryEntry.NameOffset];
        if (length < 1 || length > PascalLayout.DirectoryEntry.NameCapacity)
        {
            name = string.Empty;
            error = "does not have a 1 to 15 character name.";
            return false;
        }

        var field = record.Slice(PascalLayout.DirectoryEntry.NameOffset, length + 1);
        for (var i = 1; i <= length; i++)
        {
            if (field[i] is < 0x20 or > 0x7E)
            {
                name = string.Empty;
                error = "has a name character outside 0x20 through 0x7E.";
                return false;
            }
        }

        name = ReadName(field);
        error = string.Empty;
        return true;
    }

    private static string ReadName(ReadOnlySpan<byte> field)
    {
        var length = field[0];
        if (length < 1 || length >= field.Length)
        {
            throw new ArgumentException("The Pascal name is not a length-prefixed string.", nameof(field));
        }

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)field[i + 1];
        }

        return new string(chars);
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset) => data[offset] | (data[offset + 1] << 8);

    private DirectoryListing FailList(FileAccessStatus status, string reason)
    {
        this.logger.Debug(
            "Pascal list on {VolumeName} failed with {Status}: {Reason}",
            this.VolumeName,
            status,
            reason);
        return DirectoryListing.Failure(status, reason);
    }

    private FileReadResult FailRead(FileAccessStatus status, string reason)
    {
        this.logger.Debug(
            "Pascal read on {VolumeName} failed with {Status}: {Reason}",
            this.VolumeName,
            status,
            reason);
        return FileReadResult.Failure(status, reason);
    }

    private bool TryFind(string name, out CatalogFile file)
    {
        for (var i = 0; i < this.files.Length; i++)
        {
            if (this.files[i].Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                file = this.files[i];
                return true;
            }
        }

        file = default;
        return false;
    }

    private byte[] ReadBytes(CatalogFile file)
    {
        var length = file.Entry.EofBytes;
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var data = new byte[length];
        var fullBlocks = file.EndBlock - file.StartBlock - 1;
        var offset = 0;
        for (var i = 0; i < fullBlocks; i++)
        {
            this.blocks.ReadBlock(file.StartBlock + i, this.blockBuffer);
            this.blockBuffer.AsSpan().CopyTo(data.AsSpan(offset, PascalLayout.BlockSize));
            offset += PascalLayout.BlockSize;
        }

        var tail = length - offset;
        if (tail > 0)
        {
            this.blocks.ReadBlock(file.EndBlock - 1, this.blockBuffer);
            this.blockBuffer.AsSpan(0, tail).CopyTo(data.AsSpan(offset));
        }

        return data;
    }

    private readonly record struct CatalogFile(FileEntry Entry, int StartBlock, int EndBlock);
}