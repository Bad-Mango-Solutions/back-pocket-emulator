// <copyright file="SyntheticImageFixtureTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using System.Runtime.InteropServices;

/// <summary>
/// Byte-level checks for the DOS 3.3, ProDOS, and Pascal images built in the test process.
/// </summary>
[TestFixture]
public class SyntheticImageFixtureTests
{
    /// <summary>
    /// A blank DOS image has a VTOC at track 17 sector 0 and an empty catalog at sector 15.
    /// </summary>
    [Test]
    public void DosBlank_HasVtocAndEmptyCatalog()
    {
        var fixture = Dos33Fixture.CreateBlank();
        var image = fixture.Image.Span;
        Assume.That(image.Length, Is.EqualTo(18 * 16 * 256));
        var vtoc = image.Slice(17 * 16 * 256, 256);

        Assert.That(vtoc[Dos33Layout.Vtoc.DosVersionOffset], Is.EqualTo(Dos33Layout.Vtoc.Version33));
        Assert.That(vtoc[Dos33Layout.Vtoc.VolumeOffset], Is.EqualTo(254));
        Assert.That(vtoc[Dos33Layout.Vtoc.CatalogTrackOffset], Is.EqualTo(Dos33Layout.VtocTrack));
        Assert.That(vtoc[Dos33Layout.Vtoc.CatalogSectorOffset], Is.EqualTo(Dos33Layout.DefaultFirstCatalogSector));
        Assert.That(vtoc[Dos33Layout.Vtoc.TrackCountOffset], Is.EqualTo(18));
        Assert.That(vtoc[Dos33Layout.Vtoc.SectorsPerTrackOffset], Is.EqualTo(Dos33Layout.DefaultSectorsPerTrack));
        Assert.That(Dos33Layout.IsVtoc(vtoc, 18, 16), Is.True);
        Assert.That(vtoc[Dos33Layout.Vtoc.BitmapOffset], Is.EqualTo(0xFF));
        Assert.That(vtoc[Dos33Layout.Vtoc.BitmapOffset + 1], Is.EqualTo(0xFF));
        var catalogBitmap = Dos33Layout.Vtoc.BitmapOffset + (Dos33Layout.VtocTrack * Dos33Layout.Vtoc.BitmapBytesPerTrack);
        Assert.That(vtoc[catalogBitmap], Is.EqualTo(0x00));
        Assert.That(vtoc[catalogBitmap + 1], Is.EqualTo(0x00));
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 0, 0), Is.True);
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 17, 0), Is.False);
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 17, 15), Is.False);
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 17, 1), Is.False);
        Assert.That(Dos33Layout.IsVtoc(new byte[256], 18, 16), Is.False);

        var catalog = image.Slice(((17 * 16) + 15) * 256, 256);
        Assert.That(catalog[Dos33Layout.Catalog.HeaderLength], Is.EqualTo(Dos33Layout.UnusedTrack));
        Assert.That(fixture.Entries, Is.Empty);
        Assert.That(fixture.VolumeName, Is.EqualTo("254"));
    }

    /// <summary>
    /// The sample catalog stores the type at offset 2, marks a deletion as track $FF, and keeps a sparse hole.
    /// </summary>
    [Test]
    public void DosSample_PutsTypeAtOffset2_AndKeepsSparseHoles()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var image = fixture.Image.Span;
        var hello = Entry(fixture, "HELLO");
        var helloAt = CatalogOffset(hello);
        Assume.That(image.Length, Is.GreaterThan(helloAt + 35));

        Assert.That(image[helloAt + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo((byte)hello.TsTrack));
        Assert.That(image[helloAt + Dos33Layout.CatalogEntry.TypeOffset], Is.EqualTo((byte)Dos33FileType.Text));
        Assert.That(image[helloAt + Dos33Layout.CatalogEntry.NameOffset], Is.EqualTo((byte)('H' | 0x80)));

        var locked = Entry(fixture, "LOCKED");
        Assert.That(image[CatalogOffset(locked) + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo((byte)locked.TsTrack));
        Assert.That(image[CatalogOffset(locked) + Dos33Layout.CatalogEntry.TypeOffset], Is.EqualTo((byte)((byte)Dos33FileType.Binary | Dos33Layout.LockedMask)));
        Assert.That(image[CatalogOffset(locked)], Is.Not.EqualTo(image[CatalogOffset(locked) + 2]));
        var binary = DataSector(image, locked, 0);
        Assert.That(binary[0], Is.EqualTo(0xA0));
        Assert.That(binary[1], Is.EqualTo(0x08));
        Assert.That(binary[2], Is.EqualTo(0x02));
        Assert.That(binary[4], Is.EqualTo((byte)'O'));
        Assert.That(locked.LogicalBytes.Length, Is.EqualTo(2));
        Assert.That(locked.LogicalBytes.Span[0], Is.EqualTo((byte)'O'));
        Assert.That(locked.LogicalBytes.Span[1], Is.EqualTo((byte)'K'));
        Assert.That(Entry(fixture, "BASIC").LogicalBytes.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22 }));
        Assert.That(Entry(fixture, "INTBAS").LogicalBytes.ToArray(), Is.EqualTo(new byte[] { 0x33 }));

        var applesoft = DataSector(image, Entry(fixture, "BASIC"), 0);
        Assert.That(applesoft[0], Is.EqualTo(0x02));
        Assert.That(applesoft[2], Is.EqualTo(0x11));
        var integer = DataSector(image, Entry(fixture, "INTBAS"), 0);
        Assert.That(integer[0], Is.EqualTo(0x01));
        Assert.That(integer[2], Is.EqualTo(0x33));

        var sparse = Entry(fixture, "SPARSE");
        Assert.That(sparse.HasSparseHole, Is.True);
        var list = DataSector(image, sparse.TsTrack, sparse.TsSector);
        var third = sparse.DataSectors[2];
        Assume.That(third.Allocated, Is.True);
        Assert.That(list[Dos33Layout.TrackSectorList.PairOffset + 2], Is.EqualTo(0));
        Assert.That(list[Dos33Layout.TrackSectorList.PairOffset + 3], Is.EqualTo(0));
        Assert.That(list[Dos33Layout.TrackSectorList.PairOffset + 4], Is.EqualTo((byte)third.Track));
        Assert.That(list[Dos33Layout.TrackSectorList.PairOffset + 5], Is.EqualTo((byte)third.Sector));
        Assert.That(sparse.RawBytes.Length, Is.EqualTo(768));

        var gone = Entry(fixture, "GONE");
        Assert.That(image[CatalogOffset(gone) + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo(Dos33Layout.DeletedTrack));
        Assert.That(image[CatalogOffset(gone) + Dos33Layout.CatalogEntry.TypeOffset], Is.EqualTo((byte)Dos33FileType.Text));
        Assert.That(image[CatalogOffset(gone) + Dos33Layout.CatalogEntry.OriginalTrackOffset], Is.EqualTo((byte)gone.TsTrack));
    }

    /// <summary>
    /// Eight files spill onto the next catalog sector instead of stopping at the first sector.
    /// </summary>
    [Test]
    public void DosCatalog_ChainsWhenTheFirstSectorFills()
    {
        var builder = new Dos33ImageBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.AddFile("F" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { (byte)i } });
        }

        var fixture = builder.Build();
        var first = Sector(fixture.Image.Span, 17, 15);
        var second = Sector(fixture.Image.Span, 17, 14);

        Assert.That(first[1], Is.EqualTo(17));
        Assert.That(first[2], Is.EqualTo(14));
        Assert.That(second[1], Is.EqualTo(0));
        Assert.That(fixture.Entries[7].CatalogSector, Is.EqualTo(14));
    }

    /// <summary>
    /// Two builds do not share a buffer.
    /// </summary>
    [Test]
    public void DosBuilds_DoNotShareABuffer()
    {
        var first = Dos33Fixture.CreateBlank();
        var second = Dos33Fixture.CreateBlank();
        Assume.That(MemoryMarshal.TryGetArray(first.Image, out var firstArray), Is.True);
        Assume.That(MemoryMarshal.TryGetArray(second.Image, out var secondArray), Is.True);
        Assume.That(firstArray.Array, Is.Not.SameAs(secondArray.Array));

        var saved = secondArray.Array![0];
        firstArray.Array![0] = 0x5A;

        Assert.That(secondArray.Array[0], Is.EqualTo(saved));
    }

    /// <summary>
    /// A blank ProDOS volume identifies itself and does not list the volume header as a file.
    /// </summary>
    [Test]
    public void ProdosBlank_HasVolumeHeaderAndFreeBitmap()
    {
        var fixture = ProdosFixture.CreateBlank();
        var key = Block(fixture, 2);
        Assume.That(key.Length, Is.EqualTo(512));

        var header = ProdosLayout.Directory.FirstEntryOffset;
        Assert.That(key[header] >> 4, Is.EqualTo((byte)ProdosStorageType.VolumeHeader));
        Assert.That(key[header + ProdosLayout.VolumeHeader.EntryLengthOffset], Is.EqualTo(ProdosLayout.Entry.Length));
        Assert.That(key[header + ProdosLayout.VolumeHeader.EntriesPerBlockOffset], Is.EqualTo(ProdosLayout.Directory.EntriesPerBlock));
        Assert.That(key[header + ProdosLayout.VolumeHeader.FileCountOffset], Is.EqualTo(0));
        Assert.That(key[header + ProdosLayout.VolumeHeader.BitmapPointerOffset], Is.EqualTo(3));
        Assert.That(ProdosLayout.IsVolumeDirectoryKeyBlock(key, fixture.BlockCount), Is.True);
        Assert.That(fixture.Entries, Is.Empty);

        var bitmap = Block(fixture, 3);
        Assert.That(bitmap[0] & 0x80, Is.EqualTo(0));
        Assert.That(bitmap[0] & 0x08, Is.EqualTo(0x08));
        Assert.That(ProdosLayout.IsBlockFree(bitmap, 0), Is.False);
        Assert.That(ProdosLayout.IsBlockFree(bitmap, 4), Is.True);
    }

    /// <summary>
    /// Larger blank volumes keep the standard directory and grow the bitmap.
    /// </summary>
    [Test]
    public void ProdosBlank_SupportsFloppyAndMultiBitmapSizes()
    {
        var floppy = ProdosFixture.CreateBlank(ProdosLayout.StandardFloppyBlocks, "FLOPPY");
        var eightHundred = ProdosFixture.CreateBlank(ProdosLayout.Floppy800KBlocks, "DISK800");
        var wide = ProdosFixture.CreateBlank(4097, "WIDE");

        var volumeHeader = ProdosLayout.Directory.FirstEntryOffset;
        Assert.That(Block(floppy, 2)[volumeHeader + ProdosLayout.VolumeHeader.BitmapPointerOffset], Is.EqualTo(6));
        Assert.That(ReadU16(Block(eightHundred, 2), volumeHeader + ProdosLayout.VolumeHeader.TotalBlocksOffset), Is.EqualTo(ProdosLayout.Floppy800KBlocks));
        Assert.That(ProdosLayout.BitmapBlockCount(4097), Is.EqualTo(2));
        Assert.That(ProdosLayout.IsVolumeDirectoryKeyBlock(Block(wide, 2), 4097), Is.True);
        Assert.That(PascalLayout.IsVolumeHeader(Block(floppy, 2), floppy.BlockCount), Is.False);
    }

    /// <summary>
    /// The sample uses low-half/high-half indexes, a sparse hole, a subdirectory, and an extended entry.
    /// </summary>
    [Test]
    public void ProdosSample_UsesHalfIndexSparseSubdirectoryAndExtended()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var key = Block(fixture, 2);
        Assume.That(ProdosLayout.IsVolumeDirectoryKeyBlock(key, fixture.BlockCount), Is.True);
        Assert.That(key[ProdosLayout.Directory.FirstEntryOffset + ProdosLayout.VolumeHeader.FileCountOffset], Is.EqualTo(6));

        var hello = Root(fixture, "HELLO");
        Assert.That(hello.EofBytes, Is.EqualTo(2));
        Assert.That(Block(fixture, hello.KeyBlock)[0], Is.EqualTo((byte)'h'));
        Assert.That(Block(fixture, hello.KeyBlock)[2], Is.EqualTo(0));

        var locked = Root(fixture, "LOCKED");
        var lockedOffset = ProdosLayout.Directory.FirstEntryOffset + ((IndexOf(fixture, "LOCKED") + 1) * ProdosLayout.Entry.Length);
        Assert.That(key[lockedOffset + ProdosLayout.Entry.AccessOffset], Is.EqualTo(ProdosLayout.AccessLocked));
        Assert.That(ProdosLayout.IsLocked(key[lockedOffset + ProdosLayout.Entry.AccessOffset]), Is.True);

        var sapling = Root(fixture, "SAPLING");
        var index = Block(fixture, sapling.KeyBlock);
        Assert.That(ProdosLayout.ReadIndexPointer(index, 1), Is.EqualTo(0));
        Assert.That(ProdosLayout.ReadIndexPointer(index, 2), Is.EqualTo(260));
        Assert.That(index[ProdosLayout.Index.LowHalf + 2], Is.EqualTo(4));
        Assert.That(index[5], Is.EqualTo(0));
        Assert.That(index[ProdosLayout.Index.HighHalf + 2], Is.EqualTo(1));

        var tree = Root(fixture, "TREE");
        var master = Block(fixture, tree.KeyBlock);
        Assert.That(ProdosLayout.ReadIndexPointer(master, 0), Is.EqualTo(261));
        Assert.That(master[ProdosLayout.Index.LowHalf + 1], Is.EqualTo(0));
        Assert.That(master[ProdosLayout.Index.HighHalf], Is.EqualTo(1));
        var subIndex = Block(fixture, tree.IndexPointers[0]);
        Assert.That(ProdosLayout.ReadIndexPointer(subIndex, 1), Is.EqualTo(0));
        Assert.That(ProdosLayout.ReadIndexPointer(subIndex, 2), Is.Not.EqualTo(0));

        var sub = Root(fixture, "SUB");
        var subBlock = Block(fixture, sub.KeyBlock);
        Assert.That(subBlock[ProdosLayout.Directory.FirstEntryOffset] >> 4, Is.EqualTo((byte)ProdosStorageType.SubdirectoryHeader));
        Assert.That(key[ProdosLayout.Directory.FirstEntryOffset + ((IndexOf(fixture, "SUB") + 1) * ProdosLayout.Entry.Length)] >> 4, Is.EqualTo((byte)ProdosStorageType.Subdirectory));
        Assert.That(sub.Children[0].Path, Is.EqualTo("SUB/NOTE"));
        var subHeader = ProdosLayout.Directory.FirstEntryOffset;
        Assert.That(subBlock[subHeader + ProdosLayout.SubdirectoryHeader.ParentEntryNumberOffset], Is.EqualTo((byte)(IndexOf(fixture, "SUB") + 2)));
        Assert.That(ReadU16(subBlock, subHeader + ProdosLayout.SubdirectoryHeader.ParentPointerOffset), Is.EqualTo(ProdosLayout.VolumeKeyBlock));

        var forked = Root(fixture, "FORKED");
        var forkedOffset = ProdosLayout.Directory.FirstEntryOffset + ((IndexOf(fixture, "FORKED") + 1) * ProdosLayout.Entry.Length);
        Assert.That(key[forkedOffset] >> 4, Is.EqualTo((byte)ProdosStorageType.Extended));
        Assert.That(forked.RawBytes.Length, Is.EqualTo(0));
        Assert.That(forked.KeyBlock, Is.GreaterThan(0));
        var extended = Block(fixture, forked.KeyBlock);
        Assert.That(extended[ProdosLayout.ExtendedKeyBlock.DataForkOffset] >> 4, Is.EqualTo((byte)ProdosStorageType.Seedling));
        Assert.That(extended[ProdosLayout.ExtendedKeyBlock.ResourceForkOffset] >> 4, Is.EqualTo((byte)ProdosStorageType.Seedling));
        Assert.That(ReadU16(extended, ProdosLayout.ExtendedKeyBlock.KeyPointerOffset), Is.EqualTo(forked.IndexPointers[0]));
        Assert.That(ReadU16(extended.Slice(ProdosLayout.ExtendedKeyBlock.ResourceForkOffset), ProdosLayout.ExtendedKeyBlock.KeyPointerOffset), Is.EqualTo(forked.IndexPointers[1]));
    }

    /// <summary>
    /// A Pascal directory is 26-byte records, and a zero block is not a Pascal header.
    /// </summary>
    [Test]
    public void PascalSample_Uses26ByteEntries()
    {
        var blank = PascalFixture.CreateBlank();
        var blankDirectory = blank.ReadDirectory();
        Assume.That(blankDirectory.Length, Is.EqualTo(PascalLayout.DirectoryBytes));
        Assert.That(PascalLayout.IsVolumeHeader(blankDirectory, blank.BlockCount), Is.True);
        Assert.That(blankDirectory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset], Is.EqualTo(0));
        Assert.That(blank.Entries, Is.Empty);

        var fixture = PascalFixture.CreateCatalogSample();
        var directory = fixture.ReadDirectory();
        Assert.That(PascalLayout.IsVolumeHeader(directory, fixture.BlockCount), Is.True);
        Assert.That(directory[PascalLayout.VolumeHeader.NameOffset], Is.EqualTo((byte)fixture.VolumeName.Length));
        Assert.That(directory[PascalLayout.VolumeHeader.NameOffset + 1], Is.EqualTo((byte)'S'));
        var note = fixture.Entries[0];
        Assert.That(directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.FirstBlockOffset], Is.EqualTo((byte)note.StartBlock));
        Assert.That(directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.KindOffset], Is.EqualTo((byte)PascalFileKind.Text));
        Assert.That(PascalLayout.FileLength(note.StartBlock, note.EndBlock, note.BytesInLastBlock), Is.EqualTo(note.Data.Length));

        var code = fixture.Entries[1];
        Assert.That(code.StartBlock, Is.EqualTo(note.EndBlock));
        Assert.That(PascalLayout.FileLength(code.StartBlock, code.EndBlock, code.BytesInLastBlock), Is.EqualTo(600));
        Assert.That(Block(fixture, code.StartBlock)[0], Is.EqualTo(0xAB));
        Assert.That(PascalLayout.FileKind((int)PascalFileKind.Text | PascalLayout.FilerWildcardBit), Is.EqualTo((int)PascalFileKind.Text));
        Assert.That(PascalLayout.IsVolumeHeader(new byte[PascalLayout.EntryLength], fixture.BlockCount), Is.False);
        Assert.That(ProdosLayout.IsVolumeDirectoryKeyBlock(directory.AsSpan(0, 512), fixture.BlockCount), Is.False);
    }

    /// <summary>
    /// Sector 1 free and every other sector on the track used lands in byte 1 bit 1, not byte 0 bit 6.
    /// </summary>
    [Test]
    public void DosBitmap_Sector1Free_IsByte1Bit1()
    {
        var vtoc = new byte[256];
        for (var sector = 0; sector < 16; sector++)
        {
            Dos33Layout.SetSectorFree(vtoc, 0, sector, sector == 1);
        }

        Assert.That(vtoc[Dos33Layout.Vtoc.BitmapOffset], Is.EqualTo(0x00));
        Assert.That(vtoc[Dos33Layout.Vtoc.BitmapOffset + 1], Is.EqualTo(0x02));
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 0, 1), Is.True);
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 0, 0), Is.False);
        Assert.That(Dos33Layout.IsSectorFree(vtoc, 0, 15), Is.False);
    }

    /// <summary>
    /// A subdirectory past the key block records that later block as its parent.
    /// </summary>
    [Test]
    public void ProdosSubdirectory_PastTheKeyBlock_PointsAtTheContainingBlock()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "BIGDIR");
        for (var i = 0; i < 12; i++)
        {
            builder.AddSeedling("F" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), ProdosFileType.Text, 0, new byte[] { 1 }, false);
        }

        builder.AddSubdirectory("DIR", new[] { new ProdosFixture.SeedlingSpec("NOTE", ProdosFileType.Text, 0, new byte[] { 2 }, false) });
        var fixture = builder.Build();
        var subdirectory = fixture.Entries[12];
        var header = Block(fixture, subdirectory.KeyBlock);

        Assert.That(ReadU16(header, 4 + ProdosLayout.SubdirectoryHeader.ParentPointerOffset), Is.EqualTo(3));
        Assert.That(header[4 + ProdosLayout.SubdirectoryHeader.ParentEntryNumberOffset], Is.EqualTo(1));
    }

    /// <summary>
    /// Master slot 0 can be empty while later slots hold data past 128K of logical size.
    /// </summary>
    [Test]
    public void ProdosTree_EmptyMasterSlot0_HasEofPastOneSlot()
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
        var master = Block(fixture, entry.KeyBlock);

        Assert.That(entry.EofBytes, Is.GreaterThan(ProdosLayout.Index.SlotBytes));
        Assert.That(entry.EofBytes, Is.EqualTo(ProdosLayout.Index.SlotBytes + 2));
        Assert.That(ProdosLayout.ReadIndexPointer(master, 0), Is.EqualTo(0));
        Assert.That(ProdosLayout.ReadIndexPointer(master, 1), Is.EqualTo(entry.IndexPointers[1]));
        Assert.That(entry.RawBytes.Span[ProdosLayout.Index.SlotBytes], Is.EqualTo(0x11));
        Assert.That(entry.RawBytes.Span[0], Is.EqualTo(0));
    }

    /// <summary>
    /// An explicit EOF may run past the last allocated block without allocating the tail.
    /// </summary>
    [Test]
    public void ProdosEof_MayExtendPastTheLastAllocatedBlock()
    {
        var builder = new ProdosImageBuilder(32, "EOF");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.AddSeedling("SEED", ProdosFileType.Text, 0, new byte[] { 1, 2 }, false, eof: 600));
        builder.AddSapling("SAP", ProdosFileType.Binary, 0, new byte[]?[] { new byte[ProdosLayout.BlockSize] }, false, eof: ProdosLayout.BlockSize + 100);
        var fixture = builder.Build();

        Assert.That(fixture.Entries, Has.Count.EqualTo(1));
        Assert.That(fixture.Entries[0].EofBytes, Is.EqualTo(ProdosLayout.BlockSize + 100));
        Assert.That(fixture.Entries[0].IndexPointers, Has.Count.EqualTo(1));
        Assert.That(fixture.Entries[0].RawBytes.Span[ProdosLayout.BlockSize + 99], Is.EqualTo(0));
    }

    private static Dos33Fixture.Entry Entry(Dos33Fixture fixture, string name) =>
        fixture.Entries.Single(entry => entry.Name == name);

    private static int CatalogOffset(Dos33Fixture.Entry entry)
    {
        var sector = ((entry.CatalogTrack * 16) + entry.CatalogSector) * 256;
        return sector + 0x0B + (entry.CatalogSlot * 35);
    }

    private static ReadOnlySpan<byte> DataSector(ReadOnlySpan<byte> image, Dos33Fixture.Entry entry, int index)
    {
        var sector = entry.DataSectors[index];
        return DataSector(image, sector.Track, sector.Sector);
    }

    private static ReadOnlySpan<byte> DataSector(ReadOnlySpan<byte> image, int track, int sector) =>
        Sector(image, track, sector);

    private static ReadOnlySpan<byte> Sector(ReadOnlySpan<byte> image, int track, int sector) =>
        image.Slice(((track * 16) + sector) * 256, 256);

    private static ReadOnlySpan<byte> Block(ProdosFixture fixture, int block) =>
        fixture.Image.Span.Slice(block * 512, 512);

    private static ReadOnlySpan<byte> Block(PascalFixture fixture, int block) =>
        fixture.Image.Span.Slice(block * 512, 512);

    private static ProdosFixture.Entry Root(ProdosFixture fixture, string name) =>
        fixture.Entries.Single(entry => entry.Name == name);

    private static int IndexOf(ProdosFixture fixture, string name)
    {
        for (var i = 0; i < fixture.Entries.Count; i++)
        {
            if (fixture.Entries[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private static int ReadU16(ReadOnlySpan<byte> block, int offset) => block[offset] | (block[offset + 1] << 8);
}