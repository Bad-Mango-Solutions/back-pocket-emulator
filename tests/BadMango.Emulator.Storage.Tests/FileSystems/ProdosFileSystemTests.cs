// <copyright file="ProdosFileSystemTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using System.Globalization;
using System.Text;

using BadMango.Unit.Components;

using Serilog;

/// <summary>
/// ProDOS reader tests. Each bug named in PRD §19 has a test against an image built here.
/// </summary>
[TestFixture]
public class ProdosFileSystemTests
{
    /// <summary>
    /// The volume directory header is not a catalog entry, on a 140K image.
    /// </summary>
    [Test]
    public void VolumeHeader_IsNotListedAsAFile()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var key = fixture.Image.Span.Slice(ProdosLayout.VolumeKeyBlock * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        Assume.That(ProdosLayout.IsVolumeDirectoryKeyBlock(key, fixture.BlockCount), Is.True);
        Assume.That(fixture.BlockCount, Is.EqualTo(ProdosLayout.StandardFloppyBlocks));
        var fs = Open(fixture);

        var listing = fs.ListDirectory(null);
        var names = listing.Entries.Select(entry => entry.Name).ToArray();

        Assert.That(fs.Kind, Is.EqualTo(AppleFileSystemKind.Prodos));
        Assert.That(fs.VolumeName, Is.EqualTo("SAMPLE"));
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(names, Is.EqualTo(fixture.Entries.Select(entry => entry.Name).ToArray()));
        Assert.That(names, Does.Not.Contain("SAMPLE"));
        Assert.That(listing.Entries.Any(entry => entry.StorageKind == FileStorageKind.None), Is.False);
        Assert.That(fs.GetFileInfo("/"), Is.Null);
        Assert.That(fs.GetFileInfo("/SAMPLE"), Is.Null);
        Assert.That(fs.TryReadFile("/", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile(string.Empty, FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        var hello = fs.GetFileInfo("HELLO");
        Assert.That(hello, Is.Not.Null);
        Assert.That(hello!.FileType, Is.EqualTo((byte)ProdosFileType.Text));
        Assert.That(hello.StorageKind, Is.EqualTo(FileStorageKind.ProdosSeedling));
        Assert.That(hello.Path, Is.EqualTo("HELLO"));
        var locked = fs.GetFileInfo("LOCKED");
        Assert.That(locked!.IsLocked, Is.True);
        Assert.That(locked.AuxType, Is.EqualTo((ushort)0x0803));
        Assert.That(locked.UnitSize, Is.EqualTo(ProdosLayout.BlockSize));
        Assert.That(fs.ListDirectory("HELLO").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.ListDirectory("MISSING").Status, Is.EqualTo(FileAccessStatus.NotFound));
    }

    /// <summary>
    /// A deleted directory slot is not a file.
    /// </summary>
    [Test]
    public void DeletedEntry_IsNotListed()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        var hello = EntryOffset(image, "HELLO");
        Assume.That(image[hello] >> 4, Is.EqualTo((byte)ProdosStorageType.Seedling));
        image[hello] = 0;
        var fs = Open(image, fixture.BlockCount);

        var names = fs.ListDirectory("/").Entries.Select(entry => entry.Name).ToArray();

        Assert.That(names, Does.Not.Contain("HELLO"));
        Assert.That(fs.GetFileInfo("HELLO"), Is.Null);
        Assert.That(fs.TryReadFile("HELLO", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(names, Does.Contain("LOCKED"));
    }

    /// <summary>
    /// Subdirectory paths list and read that directory. A path is not ignored.
    /// </summary>
    [Test]
    public void Subdirectory_HonorsThePath()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var sub = fixture.Entries.Single(entry => entry.Name == "SUB");
        Assume.That(sub.IsDirectory, Is.True);
        Assume.That(sub.Children[0].Path, Is.EqualTo("SUB/NOTE"));
        var fs = Open(fixture);

        var root = fs.ListDirectory("/");
        var listed = fs.ListDirectory("SUB");
        var absolute = fs.ListDirectory("/SAMPLE/SUB/");
        var note = fs.TryReadFile("SUB/NOTE", FileReadView.Raw);
        var ignored = fs.TryReadFile("NOTE", FileReadView.Raw);
        var nestedFile = fs.ListDirectory("SUB/NOTE");

        Assert.That(root.Entries.Any(entry => entry.Name == "NOTE"), Is.False);
        Assert.That(listed.Succeeded, Is.True);
        Assert.That(listed.Entries, Has.Count.EqualTo(1));
        Assert.That(listed.Entries[0].Name, Is.EqualTo("NOTE"));
        Assert.That(listed.Entries[0].Path, Is.EqualTo("SUB/NOTE"));
        Assert.That(listed.Entries[0].IsDirectory, Is.False);
        Assert.That(absolute.Entries[0].Path, Is.EqualTo("SUB/NOTE"));
        Assert.That(note.Succeeded, Is.True);
        Assert.That(note.Data.ToArray(), Is.EqualTo(new byte[] { (byte)'N' }));
        Assert.That(fs.TryReadFile("/sample/sub/note", FileReadView.Raw).Data.ToArray(), Is.EqualTo(new byte[] { (byte)'N' }));
        Assert.That(ignored.Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(ignored.Data.Length, Is.EqualTo(0));
        Assert.That(fs.TryReadFile("SUB", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile("SAMPLE/HELLO", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile("/SAMPLE/HELLO", FileReadView.Raw).Succeeded, Is.True);
        Assert.That(nestedFile.Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.GetFileInfo("SUB")!.IsDirectory, Is.True);
        Assert.That(fs.GetFileInfo("SUB")!.StorageKind, Is.EqualTo(FileStorageKind.ProdosSubdirectory));
        Assert.That(fs.ListDirectory("..").Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.ListDirectory("A//B").Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.TryReadFile(".", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.GetFileInfo(".."), Is.Null);
        Assert.Throws<ArgumentNullException>(() => fs.TryReadFile(null!, FileReadView.Raw));
        Assert.Throws<ArgumentNullException>(() => fs.GetFileInfo(null!));
    }

    /// <summary>
    /// A subdirectory inside a subdirectory is reached by its path.
    /// </summary>
    [Test]
    public void Subdirectory_NestedPath_ReadsTheLeaf()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        var sub = fixture.Entries.Single(entry => entry.Name == "SUB");
        var directory = FindFreeBlock(image, fixture.BlockCount, ProdosLayout.FirstDataBlock(fixture.BlockCount));
        var data = FindFreeBlock(image, fixture.BlockCount, directory + 1);
        Assume.That(directory, Is.Not.EqualTo(data));
        var nestSlot = (sub.KeyBlock * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset + (2 * ProdosLayout.Entry.Length);
        WriteEntry(image.AsSpan(nestSlot, ProdosLayout.Entry.Length), ProdosStorageType.Subdirectory, "NEST", ProdosFileType.Directory, directory, 1, ProdosLayout.BlockSize, sub.KeyBlock);
        var nestBlock = image.AsSpan(directory * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        nestBlock.Clear();
        WriteSubdirectoryHeader(nestBlock, "NEST", 1, sub.KeyBlock, 3);
        var leafSlot = ProdosLayout.Directory.FirstEntryOffset + ProdosLayout.Entry.Length;
        WriteEntry(nestBlock.Slice(leafSlot, ProdosLayout.Entry.Length), ProdosStorageType.Seedling, "LEAF", ProdosFileType.Text, data, 1, 1, directory);
        image[data * ProdosLayout.BlockSize] = (byte)'Z';
        var fs = Open(image, fixture.BlockCount);

        var parent = fs.ListDirectory("SUB");
        var nested = fs.ListDirectory("SUB/NEST");
        var leaf = fs.TryReadFile("sub/nest/leaf", FileReadView.Raw);

        Assert.That(parent.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "NOTE", "NEST" }));
        Assert.That(nested.Succeeded, Is.True);
        Assert.That(nested.Entries[0].Path, Is.EqualTo("SUB/NEST/LEAF"));
        Assert.That(leaf.Succeeded, Is.True);
        Assert.That(leaf.Data.ToArray(), Is.EqualTo(new byte[] { (byte)'Z' }));
        Assert.That(fs.TryReadFile("SUB/NEST", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
    }

    /// <summary>
    /// The thirteenth root file lives on the next directory block, not only in the key block.
    /// </summary>
    [Test]
    public void DirectoryChain_FollowsTheNextBlockPointer()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "CHAIN");
        for (var i = 0; i < 13; i++)
        {
            builder.AddSeedling("F" + i.ToString(CultureInfo.InvariantCulture), ProdosFileType.Text, 0, new byte[] { (byte)i }, false);
        }

        var fixture = builder.Build();
        Assume.That(fixture.Entries, Has.Count.EqualTo(13));
        Assume.That(fixture.Entries[12].Name, Is.EqualTo("F12"));
        var fs = Open(fixture);

        var listing = fs.ListDirectory(null);
        var last = fs.TryReadFile("F12", FileReadView.Raw);

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries, Has.Count.EqualTo(13));
        Assert.That(listing.Entries[12].Name, Is.EqualTo("F12"));
        Assert.That(last.Succeeded, Is.True);
        Assert.That(last.Data.ToArray(), Is.EqualTo(new byte[] { 12 }));
    }

    /// <summary>
    /// Sapling bytes come from low-half/high-half pointers. A hole in the middle is not the end.
    /// </summary>
    [Test]
    public void SaplingRead_UsesLowHalfThenHighHalf_AndKeepsTheSparseHole()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var sapling = fixture.Entries.Single(entry => entry.Name == "SAPLING");
        var index = fixture.Image.Span.Slice(sapling.KeyBlock * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        Assume.That(ProdosLayout.ReadIndexPointer(index, 1), Is.EqualTo(0));
        Assume.That(ProdosLayout.ReadIndexPointer(index, 2), Is.EqualTo(260));
        Assume.That(index[5], Is.EqualTo(0));
        var fs = Open(fixture);

        var raw = fs.TryReadFile("SAPLING", FileReadView.Raw);
        var logical = fs.TryReadFile("SAPLING", FileReadView.Logical);

        Assert.That(raw.Succeeded, Is.True);
        Assert.That(raw.Data.Length, Is.EqualTo((2 * ProdosLayout.BlockSize) + 10));
        Assert.That(raw.Data.Span[0], Is.EqualTo(0x11));
        Assert.That(raw.Data.Span[ProdosLayout.BlockSize - 1], Is.EqualTo(0x11));
        Assert.That(raw.Data.Span[ProdosLayout.BlockSize], Is.EqualTo(0));
        Assert.That(raw.Data.Span[(2 * ProdosLayout.BlockSize) - 1], Is.EqualTo(0));
        Assert.That(raw.Data.Span[2 * ProdosLayout.BlockSize], Is.EqualTo(0x22));
        Assert.That(raw.Data.Span[raw.Data.Length - 1], Is.EqualTo(0x22));
        Assert.That(raw.Data.ToArray(), Is.EqualTo(sapling.RawBytes.ToArray()));
        Assert.That(logical.Data.ToArray(), Is.EqualTo(raw.Data.ToArray()));
        Assert.That(fs.GetFileInfo("SAPLING")!.StorageKind, Is.EqualTo(FileStorageKind.ProdosSapling));
    }

    /// <summary>
    /// Tree bytes come from a master index and a subindex, both low-half/high-half, not a placeholder string.
    /// </summary>
    [Test]
    public void TreeRead_UsesLowHalfThenHighHalf_NotAPlaceholder()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var tree = fixture.Entries.Single(entry => entry.Name == "TREE");
        var master = fixture.Image.Span.Slice(tree.KeyBlock * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        Assume.That(ProdosLayout.ReadIndexPointer(master, 0), Is.EqualTo(261));
        Assume.That(master[ProdosLayout.Index.HighHalf], Is.EqualTo(1));
        Assume.That(master[1], Is.EqualTo(0));
        var fs = Open(fixture);

        var result = fs.TryReadFile("TREE", FileReadView.Raw);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Status, Is.EqualTo(FileAccessStatus.Success));
        Assert.That(result.Data.Length, Is.EqualTo((2 * ProdosLayout.BlockSize) + 2));
        Assert.That(result.Data.Span[0], Is.EqualTo(0x33));
        Assert.That(result.Data.Span[ProdosLayout.BlockSize], Is.EqualTo(0));
        Assert.That(result.Data.Span[2 * ProdosLayout.BlockSize], Is.EqualTo(0x44));
        Assert.That(result.Data.Span[result.Data.Length - 1], Is.EqualTo(0x44));
        Assert.That(result.Data.ToArray(), Is.EqualTo(tree.RawBytes.ToArray()));
        Assert.That(result.Reason, Is.EqualTo("ok"));
        Assert.That(fs.GetFileInfo("TREE")!.StorageKind, Is.EqualTo(FileStorageKind.ProdosTree));
    }

    /// <summary>
    /// A zero master-index pointer is a hole of zeros. The file continues in the next slot.
    /// </summary>
    [Test]
    public void SparseBlocks_AreZerosNotEndOfFile()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "TREE");
        builder.AddTree(
            "SPARSE",
            ProdosFileType.Binary,
            0,
            new byte[]?[] { new byte[] { 0x11, 0x22 } },
            subIndexMinimumBlock: 0,
            locked: false,
            masterSlot: 1);
        var fixture = builder.Build();
        var entry = fixture.Entries[0];
        var master = fixture.Image.Span.Slice(entry.KeyBlock * ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        Assume.That(ProdosLayout.ReadIndexPointer(master, 0), Is.EqualTo(0));
        Assume.That(ProdosLayout.ReadIndexPointer(master, 1), Is.Not.EqualTo(0));
        Assume.That(entry.EofBytes, Is.EqualTo(ProdosLayout.Index.SlotBytes + 2));
        var fs = Open(fixture);

        var result = fs.TryReadFile("SPARSE", FileReadView.Raw);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Data.Length, Is.EqualTo(ProdosLayout.Index.SlotBytes + 2));
        Assert.That(result.Data.Span[0], Is.EqualTo(0));
        Assert.That(result.Data.Span[ProdosLayout.Index.SlotBytes - 1], Is.EqualTo(0));
        Assert.That(result.Data.Span[ProdosLayout.Index.SlotBytes], Is.EqualTo(0x11));
        Assert.That(result.Data.Span[ProdosLayout.Index.SlotBytes + 1], Is.EqualTo(0x22));
    }

    /// <summary>
    /// Seedling, sapling, and an explicit tail are trimmed to the EOF field.
    /// </summary>
    [Test]
    public void Reads_AreTrimmedToEof()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        Assume.That(hello.EofBytes, Is.EqualTo(2));
        Assume.That(hello.StorageKind, Is.EqualTo(FileStorageKind.ProdosSeedling));
        var fs = Open(fixture);
        var builder = new ProdosImageBuilder(32, "EOF");
        builder.AddSeedling("EMPTY", ProdosFileType.Text, 0, ReadOnlyMemory<byte>.Empty, false);
        builder.AddSapling(
            "TAIL",
            ProdosFileType.Binary,
            0,
            new byte[]?[] { new byte[ProdosLayout.BlockSize] },
            false,
            eof: ProdosLayout.BlockSize + 100);
        var extended = builder.Build();
        var tailFs = Open(extended);

        var seedling = fs.TryReadFile("HELLO", FileReadView.Raw);
        var locked = fs.TryReadFile("LOCKED", FileReadView.Logical);
        var empty = tailFs.TryReadFile("EMPTY", FileReadView.Raw);
        var tail = tailFs.TryReadFile("TAIL", FileReadView.Raw);

        Assert.That(seedling.Succeeded, Is.True);
        Assert.That(seedling.Data.ToArray(), Is.EqualTo(new byte[] { (byte)'h', (byte)'i' }));
        Assert.That(seedling.Data.Length, Is.EqualTo(hello.EofBytes));
        Assert.That(locked.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(empty.Succeeded, Is.True);
        Assert.That(empty.Data.Length, Is.EqualTo(0));
        Assert.That(tail.Succeeded, Is.True);
        Assert.That(tail.Data.Length, Is.EqualTo(ProdosLayout.BlockSize + 100));
        Assert.That(tail.Data.Span[ProdosLayout.BlockSize], Is.EqualTo(0));
        Assert.That(tail.Data.Span[tail.Data.Length - 1], Is.EqualTo(0));
    }

    /// <summary>
    /// Storage type $5 is listed and returns no bytes.
    /// </summary>
    [Test]
    public void ExtendedStorage_ReturnsUnsupportedAndNoBytes()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var forked = fixture.Entries.Single(entry => entry.Name == "FORKED");
        var image = fixture.Image.Span;
        var entry = EntryOffset(image, "FORKED");
        Assume.That(image[entry] >> 4, Is.EqualTo((byte)ProdosStorageType.Extended));
        Assume.That(forked.RawBytes.Length, Is.EqualTo(0));
        Assume.That(forked.KeyBlock, Is.GreaterThan(0));
        var fs = Open(fixture);

        var info = fs.GetFileInfo("FORKED");
        var result = fs.TryReadFile("FORKED", FileReadView.Raw);
        var logical = fs.TryReadFile("FORKED", FileReadView.Logical);

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.StorageKind, Is.EqualTo(FileStorageKind.ProdosExtended));
        Assert.That(fs.ListDirectory(null).Entries.Any(item => item.Name == "FORKED"), Is.True);
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Status, Is.EqualTo(FileAccessStatus.UnsupportedStorage));
        Assert.That(result.Data.Length, Is.EqualTo(0));
        Assert.That(result.Data.ToArray(), Is.Empty);
        Assert.That(result.Reason, Does.Contain("$5"));
        Assert.That(logical.Status, Is.EqualTo(FileAccessStatus.UnsupportedStorage));
        Assert.That(logical.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// Storage type $4 returns unsupported and no bytes.
    /// </summary>
    [Test]
    public void PascalArea_ReturnsUnsupportedAndNoBytes()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        var entry = EntryOffset(image, "FORKED");
        Assume.That(image[entry] >> 4, Is.EqualTo((byte)ProdosStorageType.Extended));
        image[entry] = (byte)(((byte)ProdosStorageType.PascalArea << 4) | (image[entry] & 0x0F));
        var fs = Open(image, fixture.BlockCount);

        var info = fs.GetFileInfo("FORKED");
        var result = fs.TryReadFile("FORKED", FileReadView.Raw);

        Assert.That(info!.StorageKind, Is.EqualTo(FileStorageKind.ProdosPascalArea));
        Assert.That(result.Status, Is.EqualTo(FileAccessStatus.UnsupportedStorage));
        Assert.That(result.Data.Length, Is.EqualTo(0));
        Assert.That(result.Reason, Does.Contain("$4"));
    }

    /// <summary>
    /// An 800K image and a hard-disk image with two bitmap blocks read the same way as a 140K floppy.
    /// </summary>
    [Test]
    public void Reader_WorksOn800KAndHardDiskImages()
    {
        var floppy800 = new ProdosImageBuilder(ProdosLayout.Floppy800KBlocks, "DISK800");
        var wide = new byte[ProdosLayout.BlockSize];
        wide[0] = 0xAB;
        floppy800.AddSapling("WIDE", ProdosFileType.Binary, 0, new byte[]?[] { null, wide }, false, minimumBlockForLast: 300);
        floppy800.AddSubdirectory("DIR", new[] { new ProdosFixture.SeedlingSpec("NOTE", ProdosFileType.Text, 0, new byte[] { 0x21 }, false) });
        var eightHundred = floppy800.Build();
        Assume.That(eightHundred.BlockCount, Is.EqualTo(ProdosLayout.Floppy800KBlocks));
        var hardBuilder = new ProdosImageBuilder(4097, "HARD");
        hardBuilder.AddSeedling("BOOT", ProdosFileType.Text, 0, new byte[] { 0x42, 0x42 }, false);
        hardBuilder.AddSapling("FAR", ProdosFileType.Binary, 0, new byte[]?[] { new byte[] { 0x77 } }, false, minimumBlockForLast: 4000);
        var hard = hardBuilder.Build();
        Assume.That(ProdosLayout.BitmapBlockCount(hard.BlockCount), Is.EqualTo(2));
        Assume.That(hard.Entries.Single(entry => entry.Name == "FAR").IndexPointers[0], Is.GreaterThanOrEqualTo(4000));
        var fs800 = Open(eightHundred);
        var fsHard = Open(hard);

        var wideFile = fs800.TryReadFile("WIDE", FileReadView.Raw);
        var note = fs800.TryReadFile("DIR/NOTE", FileReadView.Logical);
        var boot = fsHard.TryReadFile("BOOT", FileReadView.Raw);
        var far = fsHard.TryReadFile("FAR", FileReadView.Logical);

        Assert.That(fs800.VolumeName, Is.EqualTo("DISK800"));
        Assert.That(wideFile.Succeeded, Is.True);
        Assert.That(wideFile.Data.Length, Is.EqualTo(ProdosLayout.BlockSize * 2));
        Assert.That(wideFile.Data.Span[0], Is.EqualTo(0));
        Assert.That(wideFile.Data.Span[ProdosLayout.BlockSize], Is.EqualTo(0xAB));
        Assert.That(note.Data.ToArray(), Is.EqualTo(new byte[] { 0x21 }));
        Assert.That(fsHard.VolumeName, Is.EqualTo("HARD"));
        Assert.That(fsHard.ListDirectory(null).Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "BOOT", "FAR" }));
        Assert.That(boot.Data.ToArray(), Is.EqualTo(new byte[] { 0x42, 0x42 }));
        Assert.That(far.Succeeded, Is.True);
        Assert.That(far.Data.ToArray(), Is.EqualTo(new byte[] { 0x77 }));
    }

    /// <summary>
    /// GS/OS Technical Note #8 case bits. The volume word is at entry $1A and a file word is at entry $1C.
    /// Bit 15 set means bits 14..0 lowercase those characters. The published word $B9C0 is Desk.Accs.
    /// </summary>
    [Test]
    public void Names_ApplyGsOsCaseBitsWhenTheFlagIsSet()
    {
        var volume = ProdosFixture.CreateBlank(ProdosLayout.StandardFloppyBlocks, "SAMPLE");
        var volumeImage = volume.Image.ToArray();
        var header = (ProdosLayout.VolumeKeyBlock * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset;
        WriteU16(volumeImage.AsSpan(header + 0x1A), 0xBE00);
        WriteU16(volumeImage.AsSpan(header + 0x1C), 0xFE00);
        var volumeFs = Open(volumeImage, volume.BlockCount);

        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "CASE");
        builder.AddSeedling("DESK.ACCS", ProdosFileType.Text, 0, new byte[] { 1 }, false);
        var fixture = builder.Build();
        var image = fixture.Image.ToArray();
        var entry = (ProdosLayout.VolumeKeyBlock * ProdosLayout.BlockSize) + ProdosLayout.Directory.FirstEntryOffset + ProdosLayout.Entry.Length;
        Assume.That(image.AsSpan(entry + ProdosLayout.Entry.NameOffset, 9).SequenceEqual(Encoding.ASCII.GetBytes("DESK.ACCS")), Is.True);
        image[entry + 0x1C] = 0xC0;
        image[entry + 0x1D] = 0xB9;
        var fs = Open(image, fixture.BlockCount);

        var info = fs.GetFileInfo("desk.accs");

        Assert.That(volumeFs.VolumeName, Is.EqualTo("Sample"));
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Name, Is.EqualTo("Desk.Accs"));
        Assert.That(info.Path, Is.EqualTo("Desk.Accs"));
        Assert.That(fs.TryReadFile("DESK.ACCS", FileReadView.Raw).Data.ToArray(), Is.EqualTo(new byte[] { 1 }));
        Assert.That(fs.ListDirectory(null).Entries[0].Name, Is.EqualTo("Desk.Accs"));
    }

    /// <summary>
    /// A directory chain that returns to an earlier block is corrupt and does not hang.
    /// </summary>
    [Test]
    public void DirectoryChain_LoopIsCorruptStructure()
    {
        var fixture = ProdosFixture.CreateBlank(ProdosLayout.StandardFloppyBlocks, "LOOP");
        var image = fixture.Image.ToArray();
        var block3 = 3 * ProdosLayout.BlockSize;
        Assume.That(image[block3 + 2] | (image[block3 + 3] << 8), Is.EqualTo(4));
        image[block3 + 2] = 2;
        image[block3 + 3] = 0;
        var fs = Open(image, fixture.BlockCount);

        var listing = fs.ListDirectory(null);
        var read = fs.TryReadFile("HELLO", FileReadView.Raw);

        Assert.That(listing.Succeeded, Is.False);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Entries, Is.Empty);
        Assert.That(listing.Reason, Does.Contain("loop"));
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// A directory next pointer past the end of the image is corrupt, not a block-read exception.
    /// </summary>
    [Test]
    public void DirectoryChain_PointerPastTheVolumeIsCorruptStructure()
    {
        var fixture = ProdosFixture.CreateBlank(ProdosLayout.StandardFloppyBlocks, "RANGE");
        var image = fixture.Image.ToArray();
        var block3 = 3 * ProdosLayout.BlockSize;
        image[block3 + 2] = 0xFF;
        image[block3 + 3] = 0xFF;
        var fs = Open(image, fixture.BlockCount);

        var listing = fs.ListDirectory("/");

        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Entries, Is.Empty);
    }

    /// <summary>
    /// An index pointer past the end of the image is corrupt, not a block-read exception.
    /// </summary>
    [Test]
    public void IndexPointer_OutsideTheVolumeIsCorruptStructure()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var sapling = fixture.Entries.Single(entry => entry.Name == "SAPLING");
        var image = fixture.Image.ToArray();
        var index = sapling.KeyBlock * ProdosLayout.BlockSize;
        image[index + ProdosLayout.Index.LowHalf] = (byte)fixture.BlockCount;
        image[index + ProdosLayout.Index.HighHalf] = (byte)(fixture.BlockCount >> 8);
        var fs = Open(image, fixture.BlockCount);

        var result = fs.TryReadFile("SAPLING", FileReadView.Raw);

        Assert.That(result.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Data.Length, Is.EqualTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.TryReadFile("SAPLING", (FileReadView)99));
    }

    /// <summary>
    /// The probe accepts a ProDOS key block, rejects other formats, and does not catch a read failure.
    /// </summary>
    [Test]
    public void Probe_RecognizesProdosAndRejectsOtherFormats()
    {
        var probe = new ProdosFileSystemProbe(Log());
        var fixture = ProdosFixture.CreateCatalogSample();
        var match = probe.TryOpen(new FileSystemSource(fixture.CreateBlockReader(), null));
        var dos = Dos33Fixture.CreateBlank();
        Assume.That(dos.Image.Length % ProdosLayout.BlockSize, Is.EqualTo(0));
        var dosBlocks = new MemoryBlockReader(dos.Image, dos.Image.Length / ProdosLayout.BlockSize, ProdosLayout.BlockSize);
        var dosAttempt = probe.TryOpen(new FileSystemSource(dosBlocks, null));
        var pascal = PascalFixture.CreateBlank();
        var pascalAttempt = probe.TryOpen(new FileSystemSource(pascal.CreateBlockReader(), null));
        var shortImage = new MemoryBlockReader(new byte[ProdosLayout.BlockSize], 1, ProdosLayout.BlockSize);
        var shortAttempt = probe.TryOpen(new FileSystemSource(shortImage, null));
        var sectors = new MemorySectorReader(new byte[256], 1, 1, 256);
        var noBlocks = probe.TryOpen(new FileSystemSource(null, sectors));
        var oddSize = new MemoryBlockReader(new byte[256], 1, 256);
        var oddAttempt = probe.TryOpen(new FileSystemSource(oddSize, null));

        Assert.That(probe.Kind, Is.EqualTo(AppleFileSystemKind.Prodos));
        Assert.That(match.Recognized, Is.True);
        Assert.That(match.FileSystem, Is.Not.Null);
        Assert.That(match.FileSystem!.VolumeName, Is.EqualTo("SAMPLE"));
        Assert.That(match.FileSystem.TryReadFile("HELLO", FileReadView.Raw).Data.ToArray(), Is.EqualTo(new byte[] { (byte)'h', (byte)'i' }));
        Assert.That(match.Reason, Is.Not.Empty);
        Assert.That(dosAttempt.Recognized, Is.False);
        Assert.That(dosAttempt.FileSystem, Is.Null);
        Assert.That(dosAttempt.Reason, Is.Not.Empty);
        Assert.That(pascalAttempt.Recognized, Is.False);
        Assert.That(pascalAttempt.FileSystem, Is.Null);
        Assert.That(shortAttempt.Recognized, Is.False);
        Assert.That(noBlocks.Recognized, Is.False);
        Assert.That(noBlocks.Reason, Does.Contain("block"));
        Assert.That(oddAttempt.Recognized, Is.False);
        Assert.That(oddAttempt.Reason, Does.Contain("512"));
        Assert.Throws<ArgumentNullException>(() => probe.TryOpen(null!));
        Assert.Throws<ArgumentNullException>(() => new ProdosFileSystemProbe(null!));
    }

    /// <summary>
    /// A block read that throws is not turned into a rejection.
    /// </summary>
    [Test]
    public void Probe_WhenBlockReadThrows_PropagatesTheException()
    {
        var probe = new ProdosFileSystemProbe(Log());

        var error = Assert.Throws<IOException>(() => probe.TryOpen(new FileSystemSource(new ThrowingBlockReader(), null)));

        Assert.That(error!.Message, Is.EqualTo("unreadable block"));
    }

    /// <summary>
    /// Opening a volume that is not ProDOS throws. The probe is what turns that into a rejection.
    /// </summary>
    [Test]
    public void Constructor_RejectsAVolumeThatIsNotProdos()
    {
        var blocks = new MemoryBlockReader(new byte[ProdosLayout.BlockSize * 3], 3, ProdosLayout.BlockSize);
        var logger = Log();

        Assert.Throws<ArgumentException>(() => new ProdosFileSystem(blocks, logger));
        Assert.Throws<ArgumentException>(() => new ProdosFileSystem(new MemoryBlockReader(new byte[256], 1, 256), logger));
        Assert.Throws<ArgumentNullException>(() => new ProdosFileSystem(null!, logger));
        Assert.Throws<ArgumentNullException>(() => new ProdosFileSystem(blocks, null!));
    }

    /// <summary>
    /// A recognized ProDOS probe is what the detector selects.
    /// </summary>
    [Test]
    public void Detector_SelectsTheProdosProbe()
    {
        var fixture = ProdosFixture.CreateBlank();
        var detector = new AppleFileSystemDetector(Log(), new IAppleFileSystemProbe[] { new ProdosFileSystemProbe(Log()) });

        var result = detector.Detect(new FileSystemSource(fixture.CreateBlockReader(), null));

        Assert.That(result.IsRecognized, Is.True);
        Assert.That(result.FileSystem!.Kind, Is.EqualTo(AppleFileSystemKind.Prodos));
        Assert.That(result.FileSystem.VolumeName, Is.EqualTo("BLANK"));
        Assert.That(result.FileSystem.ListDirectory(null).Entries, Is.Empty);
        Assert.That(result.Attempts[0].Recognized, Is.True);
    }

    private static ProdosFileSystem Open(ProdosFixture fixture) => new(fixture.CreateBlockReader(), Log());

    private static ProdosFileSystem Open(byte[] image, int blockCount) =>
        new(new MemoryBlockReader(image, blockCount, ProdosLayout.BlockSize), Log());

    private static ILogger Log() => Generator.Log().Object;

    private static int EntryOffset(ReadOnlySpan<byte> image, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        for (var i = 0; i <= image.Length - bytes.Length; i++)
        {
            if (image.Slice(i, bytes.Length).SequenceEqual(bytes))
            {
                return i - ProdosLayout.Entry.NameOffset;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private static int FindFreeBlock(byte[] image, int blockCount, int start)
    {
        var bitmapAt = ProdosLayout.BitmapPointer(blockCount) * ProdosLayout.BlockSize;
        var bitmap = image.AsSpan(bitmapAt, ProdosLayout.BitmapBlockCount(blockCount) * ProdosLayout.BlockSize);
        for (var block = start; block < blockCount; block++)
        {
            if (ProdosLayout.IsBlockFree(bitmap, block))
            {
                return block;
            }
        }

        throw new InvalidOperationException("No free ProDOS block.");
    }

    private static void WriteEntry(
        Span<byte> entry,
        ProdosStorageType storage,
        string name,
        ProdosFileType fileType,
        int keyBlock,
        int blocksUsed,
        int eof,
        int headerBlock)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        entry.Clear();
        entry[0] = (byte)(((byte)storage << 4) | bytes.Length);
        bytes.CopyTo(entry[ProdosLayout.Entry.NameOffset..]);
        entry[ProdosLayout.Entry.FileTypeOffset] = (byte)fileType;
        WriteU16(entry[ProdosLayout.Entry.KeyPointerOffset..], keyBlock);
        WriteU16(entry[ProdosLayout.Entry.BlocksUsedOffset..], blocksUsed);
        entry[ProdosLayout.Entry.EofOffset] = (byte)eof;
        entry[ProdosLayout.Entry.EofOffset + 1] = (byte)(eof >> 8);
        entry[ProdosLayout.Entry.EofOffset + 2] = (byte)(eof >> 16);
        entry[ProdosLayout.Entry.AccessOffset] = ProdosLayout.AccessUnlocked;
        WriteU16(entry[ProdosLayout.Entry.HeaderPointerOffset..], headerBlock);
    }

    private static void WriteSubdirectoryHeader(Span<byte> block, string name, int fileCount, int parentBlock, int parentEntry)
    {
        var header = block.Slice(ProdosLayout.Directory.FirstEntryOffset, ProdosLayout.Entry.Length);
        var bytes = Encoding.ASCII.GetBytes(name);
        header[0] = (byte)(((byte)ProdosStorageType.SubdirectoryHeader << 4) | bytes.Length);
        bytes.CopyTo(header[ProdosLayout.Entry.NameOffset..]);
        header[ProdosLayout.SubdirectoryHeader.ReservedOffset] = ProdosLayout.SubdirectoryHeader.ReservedMark;
        header[ProdosLayout.Entry.AccessOffset] = ProdosLayout.AccessUnlocked;
        header[ProdosLayout.SubdirectoryHeader.EntryLengthOffset] = ProdosLayout.Entry.Length;
        header[ProdosLayout.SubdirectoryHeader.EntriesPerBlockOffset] = ProdosLayout.Directory.EntriesPerBlock;
        WriteU16(header[ProdosLayout.SubdirectoryHeader.FileCountOffset..], fileCount);
        WriteU16(header[ProdosLayout.SubdirectoryHeader.ParentPointerOffset..], parentBlock);
        header[ProdosLayout.SubdirectoryHeader.ParentEntryNumberOffset] = (byte)parentEntry;
        header[ProdosLayout.SubdirectoryHeader.ParentEntryLengthOffset] = ProdosLayout.Entry.Length;
    }

    private static void WriteU16(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
    }

    private sealed class ThrowingBlockReader : IBlockReader
    {
        public int BlockSize => ProdosLayout.BlockSize;

        public int BlockCount => ProdosLayout.StandardFloppyBlocks;

        public void ReadBlock(int blockIndex, Span<byte> destination) => throw new IOException("unreadable block");
    }
}