// <copyright file="ProdosFixture.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// A ProDOS block image built in the test process. No bytes are read from <c>disks/</c>.
/// </summary>
public sealed class ProdosFixture
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProdosFixture"/> class.
    /// </summary>
    /// <param name="image">Contiguous 512-byte blocks.</param>
    /// <param name="blockCount">Blocks in <paramref name="image"/>.</param>
    /// <param name="volumeName">Volume name stored in the key block.</param>
    /// <param name="entries">Root directory entries. The volume header is not an entry.</param>
    public ProdosFixture(ReadOnlyMemory<byte> image, int blockCount, string volumeName, IReadOnlyList<Entry> entries)
    {
        this.Image = image;
        this.BlockCount = blockCount;
        this.VolumeName = volumeName;
        this.Entries = entries;
    }

    /// <summary>Gets the block image.</summary>
    /// <value>The blocks, including boot blocks left as zeros.</value>
    public ReadOnlyMemory<byte> Image { get; }

    /// <summary>Gets the block count.</summary>
    /// <value>The volume size.</value>
    public int BlockCount { get; }

    /// <summary>Gets the volume name.</summary>
    /// <value>The uppercase ProDOS name.</value>
    public string VolumeName { get; }

    /// <summary>Gets the root entries in directory order.</summary>
    /// <value>Files and subdirectory entries. Nested files are on <see cref="Entry.Children"/>.</value>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>
    /// Builds an empty formatted volume. Blocks 0 and 1 are zero. The bitmap follows the directory.
    /// </summary>
    /// <param name="blockCount">Volume size. A 140K floppy is 280. An 800K disk is 1600.</param>
    /// <param name="volumeName">Volume name. Defaults to <c>BLANK</c>.</param>
    /// <returns>A blank image.</returns>
    public static ProdosFixture CreateBlank(int blockCount = 16, string volumeName = "BLANK") =>
        new ProdosImageBuilder(blockCount, volumeName).Build();

    /// <summary>
    /// Builds a 140K volume with a seedling, a locked file, a sapling, a tree, a subdirectory, and an extended entry.
    /// </summary>
    /// <returns>The sample image.</returns>
    /// <remarks>
    /// The sapling's third pointer and the tree's subindex live at blocks 260 and 261 so the high
    /// half of the index is non-zero. Index slot 1 of the sapling is a sparse hole.
    /// </remarks>
    public static ProdosFixture CreateCatalogSample()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "SAMPLE");
        builder.AddSeedling("HELLO", ProdosFileType.Text, auxType: 0, new byte[] { (byte)'h', (byte)'i' }, locked: false);
        builder.AddSeedling("LOCKED", ProdosFileType.Binary, auxType: 0x0803, new byte[] { 1, 2, 3, 4 }, locked: true);

        var sapling = new byte[]?[3];
        sapling[0] = new byte[ProdosLayout.BlockSize];
        sapling[0].AsSpan().Fill(0x11);
        sapling[2] = new byte[10];
        sapling[2].AsSpan().Fill(0x22);
        builder.AddSapling("SAPLING", ProdosFileType.Binary, auxType: 0, sapling, locked: false, minimumBlockForLast: 260);

        var treeFirst = new byte[ProdosLayout.BlockSize];
        treeFirst[0] = 0x33;
        var tree = new byte[]?[3];
        tree[0] = treeFirst;
        tree[2] = new byte[] { 0x44, 0x44 };
        builder.AddTree("TREE", ProdosFileType.Binary, auxType: 0, tree, subIndexMinimumBlock: 261, locked: false);

        builder.AddSubdirectory(
            "SUB",
            new[] { new SeedlingSpec("NOTE", ProdosFileType.Text, 0, new byte[] { (byte)'N' }, false) });
        builder.AddExtended("FORKED", ProdosFileType.Text);
        return builder.Build();
    }

    /// <summary>
    /// Opens a block reader over <see cref="Image"/>.
    /// </summary>
    /// <returns>A block reader.</returns>
    public IBlockReader CreateBlockReader() => new MemoryBlockReader(this.Image, this.BlockCount, ProdosLayout.BlockSize);

    /// <summary>A seedling stored inside a synthetic subdirectory.</summary>
    /// <param name="Name">File name.</param>
    /// <param name="FileType">ProDOS file type.</param>
    /// <param name="AuxType">Auxiliary type.</param>
    /// <param name="Data">End-of-file bytes. At most one block.</param>
    /// <param name="Locked">Whether the write bit is clear.</param>
    public readonly record struct SeedlingSpec(string Name, ProdosFileType FileType, ushort AuxType, ReadOnlyMemory<byte> Data, bool Locked);

    /// <summary>One root or nested catalog entry in a <see cref="ProdosFixture"/>.</summary>
    public sealed class Entry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class.
        /// </summary>
        /// <param name="name">File name.</param>
        /// <param name="path">Volume-relative path.</param>
        /// <param name="fileType">ProDOS file type.</param>
        /// <param name="auxType">Auxiliary type.</param>
        /// <param name="eofBytes">EOF field.</param>
        /// <param name="allocatedUnits">Blocks-used field.</param>
        /// <param name="isLocked">Whether the write bit is clear.</param>
        /// <param name="isDirectory">Whether this is a subdirectory entry.</param>
        /// <param name="storageKind">Storage kind.</param>
        /// <param name="keyBlock">Seedling data, sapling or tree index, or subdirectory key block.</param>
        /// <param name="indexPointers">Index pointers. Zero is a hole. Empty when the file has no index.</param>
        /// <param name="childIndexPointers">Pointers in a tree's single subindex.</param>
        /// <param name="rawBytes">Bytes a raw read returns. Empty for directories and extended files.</param>
        /// <param name="children">Files inside a subdirectory.</param>
        public Entry(
            string name,
            string path,
            ProdosFileType fileType,
            ushort auxType,
            int eofBytes,
            int allocatedUnits,
            bool isLocked,
            bool isDirectory,
            FileStorageKind storageKind,
            int keyBlock,
            IReadOnlyList<int> indexPointers,
            IReadOnlyList<int> childIndexPointers,
            ReadOnlyMemory<byte> rawBytes,
            IReadOnlyList<Entry> children)
        {
            this.Name = name;
            this.Path = path;
            this.FileType = fileType;
            this.AuxType = auxType;
            this.EofBytes = eofBytes;
            this.AllocatedUnits = allocatedUnits;
            this.IsLocked = isLocked;
            this.IsDirectory = isDirectory;
            this.StorageKind = storageKind;
            this.KeyBlock = keyBlock;
            this.IndexPointers = indexPointers;
            this.ChildIndexPointers = childIndexPointers;
            this.RawBytes = rawBytes;
            this.Children = children;
        }

        /// <summary>Gets the file name.</summary>
        /// <value>The catalog name.</value>
        public string Name { get; }

        /// <summary>Gets the volume-relative path.</summary>
        /// <value>The path a reader stores on <see cref="FileEntry"/>.</value>
        public string Path { get; }

        /// <summary>Gets the ProDOS file type.</summary>
        /// <value>The type byte.</value>
        public ProdosFileType FileType { get; }

        /// <summary>Gets the auxiliary type.</summary>
        /// <value>The aux type.</value>
        public ushort AuxType { get; }

        /// <summary>Gets the EOF field.</summary>
        /// <value>The logical length in bytes.</value>
        public int EofBytes { get; }

        /// <summary>Gets the blocks-used field.</summary>
        /// <value>The allocated block count, including index blocks.</value>
        public int AllocatedUnits { get; }

        /// <summary>Gets a value indicating whether the write bit is clear.</summary>
        /// <value><see langword="true"/> when the file is locked.</value>
        public bool IsLocked { get; }

        /// <summary>Gets a value indicating whether this is a subdirectory.</summary>
        /// <value><see langword="true"/> for storage type <c>$D</c>.</value>
        public bool IsDirectory { get; }

        /// <summary>Gets the storage kind.</summary>
        /// <value>The kind a reader reports.</value>
        public FileStorageKind StorageKind { get; }

        /// <summary>Gets the key block.</summary>
        /// <value>Zero for an extended entry that has no key block.</value>
        public int KeyBlock { get; }

        /// <summary>Gets the index pointers for a sapling or a tree master.</summary>
        /// <value>Block numbers. Zero is a sparse hole.</value>
        public IReadOnlyList<int> IndexPointers { get; }

        /// <summary>Gets the data pointers in a tree's subindex.</summary>
        /// <value>Block numbers. Empty unless <see cref="StorageKind"/> is a tree.</value>
        public IReadOnlyList<int> ChildIndexPointers { get; }

        /// <summary>Gets the trimmed raw bytes.</summary>
        /// <value>The EOF-trimmed file, with sparse holes as zeros.</value>
        public ReadOnlyMemory<byte> RawBytes { get; }

        /// <summary>Gets subdirectory children.</summary>
        /// <value>An empty list when this entry is not a directory.</value>
        public IReadOnlyList<Entry> Children { get; }
    }
}