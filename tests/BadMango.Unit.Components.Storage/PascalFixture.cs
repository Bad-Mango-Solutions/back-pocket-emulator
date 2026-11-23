// <copyright file="PascalFixture.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// An Apple Pascal block image built in the test process. No bytes are read from <c>disks/</c>.
/// </summary>
public sealed class PascalFixture
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PascalFixture"/> class.
    /// </summary>
    /// <param name="image">Contiguous 512-byte blocks.</param>
    /// <param name="blockCount">Blocks in <paramref name="image"/>.</param>
    /// <param name="volumeName">Volume name stored in the directory header.</param>
    /// <param name="entries">File entries in directory order. The volume header is not an entry.</param>
    public PascalFixture(ReadOnlyMemory<byte> image, int blockCount, string volumeName, IReadOnlyList<Entry> entries)
    {
        this.Image = image;
        this.BlockCount = blockCount;
        this.VolumeName = volumeName;
        this.Entries = entries;
    }

    /// <summary>Gets the block image.</summary>
    /// <value>The blocks. Blocks 0 and 1 are zero.</value>
    public ReadOnlyMemory<byte> Image { get; }

    /// <summary>Gets the block count.</summary>
    /// <value>The volume size stored in the header.</value>
    public int BlockCount { get; }

    /// <summary>Gets the volume name.</summary>
    /// <value>The uppercase name, without the length byte.</value>
    public string VolumeName { get; }

    /// <summary>Gets the file entries.</summary>
    /// <value>26-byte records after the volume header, in directory order.</value>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>
    /// Builds an empty volume. The directory occupies blocks 2–5 and lists no files.
    /// </summary>
    /// <param name="blockCount">Blocks. At least 6. Defaults to 6.</param>
    /// <param name="volumeName">Volume name, 1–7 characters. Defaults to <c>BLANK</c>.</param>
    /// <returns>A blank image.</returns>
    public static PascalFixture CreateBlank(int blockCount = 6, string volumeName = "BLANK") =>
        new PascalImageBuilder(blockCount, volumeName).Build();

    /// <summary>
    /// Builds a volume with one short text file and one code file that spans two blocks.
    /// </summary>
    /// <returns>The sample image.</returns>
    public static PascalFixture CreateCatalogSample()
    {
        var builder = new PascalImageBuilder(blocks: 9, name: "SAMPLE");
        builder.AddFile("NOTE.TEXT", PascalFileKind.Text, "Pascal"u8.ToArray());
        var code = new byte[600];
        code.AsSpan().Fill(0xAB);
        builder.AddFile("UNIT.CODE", PascalFileKind.Code, code);
        return builder.Build();
    }

    /// <summary>
    /// Opens a block reader over <see cref="Image"/>.
    /// </summary>
    /// <returns>A block reader.</returns>
    public IBlockReader CreateBlockReader() => new MemoryBlockReader(this.Image, this.BlockCount, PascalLayout.BlockSize);

    /// <summary>Reads the 2048-byte directory from blocks 2–5.</summary>
    /// <returns>The packed directory.</returns>
    public byte[] ReadDirectory()
    {
        var directory = new byte[PascalLayout.DirectoryBytes];
        for (var block = 0; block < PascalLayout.DirectoryBlockCount; block++)
        {
            this.Image.Span.Slice((PascalLayout.DirectoryStartBlock + block) * PascalLayout.BlockSize, PascalLayout.BlockSize)
                .CopyTo(directory.AsSpan(block * PascalLayout.BlockSize));
        }

        return directory;
    }

    /// <summary>One file written into a <see cref="PascalFixture"/>.</summary>
    public sealed class Entry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class.
        /// </summary>
        /// <param name="name">File name.</param>
        /// <param name="kind">Kind nibble.</param>
        /// <param name="startBlock">First block of the file.</param>
        /// <param name="endBlock">First block after the file.</param>
        /// <param name="bytesInLastBlock">Bytes used in the last block.</param>
        /// <param name="data">The file bytes a reader returns.</param>
        public Entry(string name, PascalFileKind kind, int startBlock, int endBlock, int bytesInLastBlock, ReadOnlyMemory<byte> data)
        {
            this.Name = name;
            this.Kind = kind;
            this.StartBlock = startBlock;
            this.EndBlock = endBlock;
            this.BytesInLastBlock = bytesInLastBlock;
            this.Data = data;
        }

        /// <summary>Gets the file name.</summary>
        /// <value>The name without the length byte.</value>
        public string Name { get; }

        /// <summary>Gets the file kind.</summary>
        /// <value>Bits 0–3 of the directory kind word.</value>
        public PascalFileKind Kind { get; }

        /// <summary>Gets the first block.</summary>
        /// <value>The start block.</value>
        public int StartBlock { get; }

        /// <summary>Gets the first block after the file.</summary>
        /// <value>The exclusive end block.</value>
        public int EndBlock { get; }

        /// <summary>Gets the bytes used in the last block.</summary>
        /// <value>1–512. A full block is stored as 512.</value>
        public int BytesInLastBlock { get; }

        /// <summary>Gets the file bytes.</summary>
        /// <value>The contiguous payload, with no block padding.</value>
        public ReadOnlyMemory<byte> Data { get; }
    }
}