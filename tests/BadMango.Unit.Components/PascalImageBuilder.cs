// <copyright file="PascalImageBuilder.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Builds an Apple Pascal volume in memory. Files are contiguous and the directory is 26-byte records.
/// </summary>
/// <remarks>
/// The directory fills blocks 2–5 even when it lists no files. Names are uppercase letters, digits,
/// and periods. The image contains no Pascal system files.
/// </remarks>
public sealed class PascalImageBuilder
{
    private readonly int blockCount;
    private readonly string volumeName;
    private readonly List<FileSpec> files = new();
    private int nextBlock = PascalLayout.FirstDataBlock;

    /// <summary>
    /// Initializes a new instance of the <see cref="PascalImageBuilder"/> class.
    /// </summary>
    /// <param name="blockCount">Blocks. At least 6 and at most <see cref="ProdosLayout.MaxBlockCount"/>.</param>
    /// <param name="volumeName">Volume name, 1–7 characters.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="blockCount"/> is out of range.</exception>
    /// <exception cref="ArgumentException">If <paramref name="volumeName"/> is not a legal volume name.</exception>
    public PascalImageBuilder(int blockCount, string volumeName)
    {
        if (blockCount < PascalLayout.FirstDataBlock || blockCount > ProdosLayout.MaxBlockCount)
        {
            throw new ArgumentOutOfRangeException(nameof(blockCount), "A Pascal fixture needs 6 through 65535 blocks.");
        }

        this.blockCount = blockCount;
        this.volumeName = Normalize(volumeName, PascalLayout.VolumeHeader.NameCapacity, nameof(volumeName));
    }

    /// <summary>
    /// Adds a contiguous file after the directory.
    /// </summary>
    /// <param name="name">File name, 1–15 characters.</param>
    /// <param name="kind">File kind. The Filer wildcard bit is not stored.</param>
    /// <param name="data">File bytes. Empty files are rejected.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">If the name or payload is not usable.</exception>
    /// <exception cref="InvalidOperationException">If the file does not fit, or the directory already has 77 files.</exception>
    public PascalImageBuilder AddFile(string name, PascalFileKind kind, ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
        {
            throw new ArgumentException("Pascal fixture files are non-empty.", nameof(data));
        }

        if (this.files.Count >= PascalLayout.MaxFiles)
        {
            throw new InvalidOperationException("An Apple Pascal directory holds at most 77 files.");
        }

        var blocks = (data.Length + PascalLayout.BlockSize - 1) / PascalLayout.BlockSize;
        if (this.nextBlock + blocks > this.blockCount)
        {
            throw new InvalidOperationException("The Pascal image does not have room for the file.");
        }

        var start = this.nextBlock;
        var end = start + blocks;
        this.nextBlock = end;
        var bytesInLast = data.Length - ((blocks - 1) * PascalLayout.BlockSize);
        this.files.Add(new FileSpec(Normalize(name, PascalLayout.DirectoryEntry.NameCapacity, nameof(name)), kind, start, end, bytesInLast, data.ToArray()));
        return this;
    }

    /// <summary>
    /// Writes the directory and the file bytes.
    /// </summary>
    /// <returns>A new image. Repeated calls return independent buffers.</returns>
    public PascalFixture Build()
    {
        var image = new byte[this.blockCount * PascalLayout.BlockSize];
        var directory = new byte[PascalLayout.DirectoryBytes];
        WriteU16(directory, PascalLayout.VolumeHeader.FirstBlockOffset, 0);
        WriteU16(directory, PascalLayout.VolumeHeader.NextBlockOffset, PascalLayout.FirstDataBlock);
        WriteU16(directory, PascalLayout.VolumeHeader.KindOffset, (int)PascalFileKind.Untyped);
        WriteName(directory.AsSpan(PascalLayout.VolumeHeader.NameOffset, PascalLayout.VolumeHeader.NameCapacity + 1), this.volumeName);
        WriteU16(directory, PascalLayout.VolumeHeader.BlocksOffset, this.blockCount);
        WriteU16(directory, PascalLayout.VolumeHeader.FileCountOffset, this.files.Count);

        var entries = new List<PascalFixture.Entry>(this.files.Count);
        for (var i = 0; i < this.files.Count; i++)
        {
            var file = this.files[i];
            var entry = directory.AsSpan((i + 1) * PascalLayout.EntryLength, PascalLayout.EntryLength);
            WriteU16(entry, PascalLayout.DirectoryEntry.FirstBlockOffset, file.StartBlock);
            WriteU16(entry, PascalLayout.DirectoryEntry.NextBlockOffset, file.EndBlock);
            WriteU16(entry, PascalLayout.DirectoryEntry.KindOffset, (int)file.Kind);
            WriteName(entry.Slice(PascalLayout.DirectoryEntry.NameOffset, PascalLayout.DirectoryEntry.NameFieldLength), file.Name);
            WriteU16(entry, PascalLayout.DirectoryEntry.LastBlockBytesOffset, file.BytesInLastBlock);
            file.Data.CopyTo(image.AsSpan(file.StartBlock * PascalLayout.BlockSize, file.Data.Length));
            entries.Add(new PascalFixture.Entry(file.Name, file.Kind, file.StartBlock, file.EndBlock, file.BytesInLastBlock, file.Data));
        }

        for (var block = 0; block < PascalLayout.DirectoryBlockCount; block++)
        {
            directory.AsSpan(block * PascalLayout.BlockSize, PascalLayout.BlockSize)
                .CopyTo(image.AsSpan((PascalLayout.DirectoryStartBlock + block) * PascalLayout.BlockSize));
        }

        return new PascalFixture(image, this.blockCount, this.volumeName, entries);
    }

    private static void WriteName(Span<byte> destination, string name)
    {
        destination.Clear();
        destination[0] = (byte)name.Length;
        for (var i = 0; i < name.Length; i++)
        {
            destination[i + 1] = (byte)name[i];
        }
    }

    private static string Normalize(string name, int capacity, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var upper = name.ToUpperInvariant();
        if (upper.Length > capacity)
        {
            throw new ArgumentException($"Pascal names are at most {capacity} characters.", paramName);
        }

        foreach (var ch in upper)
        {
            var letter = ch is >= 'A' and <= 'Z';
            var digit = ch is >= '0' and <= '9';
            if (!letter && !digit && ch != '.')
            {
                throw new ArgumentException("Pascal fixture names are letters, digits, and periods.", paramName);
            }
        }

        return upper;
    }

    private static void WriteU16(Span<byte> destination, int offset, int value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
    }

    private sealed class FileSpec
    {
        public FileSpec(string name, PascalFileKind kind, int startBlock, int endBlock, int bytesInLastBlock, byte[] data)
        {
            this.Name = name;
            this.Kind = kind;
            this.StartBlock = startBlock;
            this.EndBlock = endBlock;
            this.BytesInLastBlock = bytesInLastBlock;
            this.Data = data;
        }

        public string Name { get; }

        public PascalFileKind Kind { get; }

        public int StartBlock { get; }

        public int EndBlock { get; }

        public int BytesInLastBlock { get; }

        public byte[] Data { get; }
    }
}