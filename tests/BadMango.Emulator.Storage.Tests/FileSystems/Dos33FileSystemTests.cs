// <copyright file="Dos33FileSystemTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using System.Globalization;
using System.Runtime.InteropServices;

using BadMango.Unit.Components;

using Serilog;

/// <summary>
/// DOS 3.3 reader tests. Each bug named for the reader has its own test, on an image
/// the test builds. No DOS master is read from disk.
/// </summary>
[TestFixture]
public class Dos33FileSystemTests
{
    /// <summary>
    /// The type and the lock bit come from catalog offset 2, not the track byte at offset 0.
    /// </summary>
    [Test]
    public void Catalog_ReadsTypeAndLockedFlagFromEntryOffset2()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var image = MutableImage(fixture);
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        var locked = fixture.Entries.Single(entry => entry.Name == "LOCKED");
        var helloAt = EntryOffset(hello);
        var lockedAt = EntryOffset(locked);
        Assume.That(hello.TsTrack, Is.Not.Zero);
        Assume.That(image[helloAt + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo((byte)hello.TsTrack));
        Assume.That(image[helloAt + Dos33Layout.CatalogEntry.TypeOffset], Is.EqualTo((byte)Dos33FileType.Text));
        Assume.That(image[helloAt], Is.Not.EqualTo(image[helloAt + Dos33Layout.CatalogEntry.TypeOffset]));
        Assume.That(
            image[lockedAt + Dos33Layout.CatalogEntry.TypeOffset],
            Is.EqualTo((byte)((byte)Dos33FileType.Binary | Dos33Layout.LockedMask)));
        Assume.That(image[lockedAt], Is.Not.EqualTo(image[lockedAt + Dos33Layout.CatalogEntry.TypeOffset]));

        var fs = Open(fixture);
        var listing = fs.ListDirectory(null);
        var live = fixture.Entries.Where(entry => !entry.IsDeleted).ToList();

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(live.Select(entry => entry.Name).ToArray()));
        for (var i = 0; i < live.Count; i++)
        {
            var expected = live[i];
            var actual = listing.Entries[i];
            Assert.That(actual.FileType, Is.EqualTo((byte)expected.FileType), expected.Name);
            Assert.That(actual.IsLocked, Is.EqualTo(expected.IsLocked), expected.Name);
            Assert.That(actual.FileType & Dos33Layout.LockedMask, Is.EqualTo(0), expected.Name);
            Assert.That(actual.EofBytes, Is.EqualTo(expected.RawBytes.Length), expected.Name);
            Assert.That(actual.AllocatedUnits, Is.EqualTo(expected.AllocatedUnits), expected.Name);
            Assert.That(actual.AuxType, Is.EqualTo(0), expected.Name);
            Assert.That(actual.UnitSize, Is.EqualTo(Dos33Layout.SectorSize), expected.Name);
            Assert.That(actual.IsDirectory, Is.False, expected.Name);
            Assert.That(actual.StorageKind, Is.EqualTo(FileStorageKind.DosTrackSectorList), expected.Name);
            Assert.That(actual.Path, Is.EqualTo(expected.Name), expected.Name);
        }

        var again = fs.ListDirectory("/");
        Assert.That(again.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(listing.Entries.Select(entry => entry.Name).ToArray()));
        Assert.That(fs.GetFileInfo("locked")!.IsLocked, Is.True);
        Assert.That(fs.GetFileInfo("LOCKED")!.FileType, Is.EqualTo((byte)Dos33FileType.Binary));
    }

    /// <summary>
    /// A track byte of <c>$FF</c> is deleted and a track byte of <c>$00</c> is unused.
    /// Files after a deleted entry are still listed.
    /// </summary>
    [Test]
    public void Catalog_DoesNotReturnDeletedOrUnusedEntries()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("KEEP", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        builder.AddFile("GONE", Dos33FileType.Binary, true, true, new byte[]?[] { new byte[] { 0x22 } });
        builder.AddFile("TAIL", Dos33FileType.Integer, false, false, new byte[]?[] { new byte[] { 0x01, 0x00, 0x33 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var gone = fixture.Entries[1];
        var tail = fixture.Entries[2];
        var goneAt = EntryOffset(gone);
        Assume.That(gone.IsDeleted, Is.True);
        Assume.That(tail.CatalogSector, Is.EqualTo(gone.CatalogSector));
        Assume.That(tail.CatalogSlot, Is.GreaterThan(gone.CatalogSlot));
        Assume.That(image[goneAt + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo(Dos33Layout.DeletedTrack));
        var ghostAt = EntryOffset(gone.CatalogTrack, gone.CatalogSector, slot: 6);
        Assume.That(image[ghostAt + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo(Dos33Layout.UnusedTrack));
        WriteEntry(image, gone.CatalogTrack, gone.CatalogSector, 6, tsTrack: 0, tsSector: 0, (byte)Dos33FileType.Text, "GHOST");

        var fs = Open(fixture);
        var listing = fs.ListDirectory(string.Empty);

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "KEEP", "TAIL" }));
        Assert.That(fs.GetFileInfo("GONE"), Is.Null);
        Assert.That(fs.GetFileInfo("GHOST"), Is.Null);
        Assert.That(fs.TryReadFile("GONE", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        var tailRead = fs.TryReadFile("TAIL", FileReadView.Logical);
        Assert.That(tailRead.Succeeded, Is.True);
        Assert.That(tailRead.Data.ToArray(), Is.EqualTo(new byte[] { 0x33 }));
    }

    /// <summary>
    /// The catalog starts at the VTOC pointer and follows the link. A sector on track 17
    /// that the link never names is not part of the catalog.
    /// </summary>
    [Test]
    public void Read_FollowsTheCatalogChainInsteadOfRescanningTrack17()
    {
        var builder = new Dos33ImageBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.AddFile(
                "F" + i.ToString(CultureInfo.InvariantCulture),
                Dos33FileType.Text,
                false,
                false,
                new byte[]?[] { new byte[] { (byte)i } });
        }

        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var firstEntry = fixture.Entries[0];
        var lastEntry = fixture.Entries[7];
        Assume.That(firstEntry.TsTrack, Is.Not.Zero);
        Assume.That(lastEntry.CatalogSector, Is.EqualTo(14));
        Assume.That(lastEntry.DataSectors[0].Track, Is.Not.EqualTo(Dos33Layout.VtocTrack));
        var first = Sector(image, Dos33Layout.VtocTrack, Dos33Layout.DefaultFirstCatalogSector);
        var second = Sector(image, Dos33Layout.VtocTrack, 14);
        Assume.That(first[Dos33Layout.Catalog.NextTrackOffset], Is.EqualTo(Dos33Layout.VtocTrack));
        Assume.That(first[Dos33Layout.Catalog.NextSectorOffset], Is.EqualTo(14));
        Assume.That(second[Dos33Layout.Catalog.NextTrackOffset], Is.EqualTo(0));
        var vtoc = Sector(image, Dos33Layout.VtocTrack, Dos33Layout.VtocSector);
        Assume.That(vtoc[Dos33Layout.Vtoc.CatalogSectorOffset], Is.EqualTo(Dos33Layout.DefaultFirstCatalogSector));
        WriteEntry(image, Dos33Layout.VtocTrack, 13, 0, tsTrack: 1, tsSector: 0, (byte)Dos33FileType.Text, "DECOY");

        var listed = Open(fixture).ListDirectory("/");

        Assert.That(listed.Succeeded, Is.True);
        Assert.That(listed.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "F0", "F1", "F2", "F3", "F4", "F5", "F6", "F7" }));
        var firstFile = Open(fixture).TryReadFile("F0", FileReadView.Raw);
        var lastFile = Open(fixture).TryReadFile("F7", FileReadView.Raw);
        Assert.That(firstFile.Succeeded, Is.True);
        Assert.That(firstFile.Data.Span[0], Is.EqualTo(0));
        Assert.That(lastFile.Succeeded, Is.True);
        Assert.That(lastFile.Data.ToArray(), Is.EqualTo(lastEntry.RawBytes.ToArray()));
        Assert.That(lastFile.Data.Span[0], Is.EqualTo(7));

        vtoc[Dos33Layout.Vtoc.CatalogSectorOffset] = 14;
        var retargeted = Open(fixture).ListDirectory(null);

        Assert.That(retargeted.Succeeded, Is.True);
        Assert.That(retargeted.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "F7" }));
    }

    /// <summary>
    /// A file is the sectors named by its own track/sector list. The next list's sector
    /// base is 122, and the gap before that pair is a hole.
    /// </summary>
    [Test]
    public void Read_FollowsThatFilesTrackSectorList()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile(
            "CHAIN",
            Dos33FileType.Text,
            false,
            false,
            new byte[]?[] { new byte[] { 0x11 }, new byte[] { 0x22 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var entry = fixture.Entries[0];
        var data0 = entry.DataSectors[0];
        var data1 = entry.DataSectors[1];
        Assume.That(entry.TsTrack, Is.Not.Zero);
        Assume.That(data0.Allocated && data1.Allocated, Is.True);
        Assume.That(data0.Track, Is.Not.EqualTo(Dos33Layout.VtocTrack));
        Assume.That(data1.Track, Is.Not.EqualTo(Dos33Layout.VtocTrack));
        var ts = Sector(image, entry.TsTrack, entry.TsSector);
        var pair0 = Dos33Layout.TrackSectorList.PairOffset;
        Assume.That(ts[Dos33Layout.TrackSectorList.SectorBaseOffset], Is.EqualTo(0));
        Assume.That(ts[Dos33Layout.TrackSectorList.SectorBaseOffset + 1], Is.EqualTo(0));
        Assume.That(ts[pair0], Is.EqualTo((byte)data0.Track));
        Assume.That(ts[pair0 + 2], Is.EqualTo((byte)data1.Track));
        Assume.That(TryFindZeroSector(image, fixture.TrackCount, out var linkTrack, out var linkSector), Is.True);
        Assume.That(linkTrack, Is.Not.Zero);

        ts[Dos33Layout.TrackSectorList.NextTrackOffset] = (byte)linkTrack;
        ts[Dos33Layout.TrackSectorList.NextSectorOffset] = (byte)linkSector;
        ts[pair0 + 2] = 0;
        ts[pair0 + 3] = 0;
        var link = Sector(image, linkTrack, linkSector);
        link[Dos33Layout.TrackSectorList.SectorBaseOffset] = (byte)Dos33Layout.TrackSectorList.PairsPerSector;
        link[Dos33Layout.TrackSectorList.PairOffset] = (byte)data1.Track;
        link[Dos33Layout.TrackSectorList.PairOffset + 1] = (byte)data1.Sector;
        var tail = Dos33Layout.TrackSectorList.PairsPerSector * Dos33Layout.SectorSize;

        var fs = Open(fixture);
        var read = fs.TryReadFile("CHAIN", FileReadView.Raw);

        Assert.That(read.Succeeded, Is.True);
        Assert.That(read.Data.Length, Is.EqualTo(tail + Dos33Layout.SectorSize));
        Assert.That(read.Data.Span[0], Is.EqualTo(0x11));
        Assert.That(read.Data.Span[Dos33Layout.SectorSize], Is.EqualTo(0));
        Assert.That(read.Data.Span[tail], Is.EqualTo(0x22));
        Assert.That(fs.GetFileInfo("CHAIN")!.EofBytes, Is.EqualTo(tail + Dos33Layout.SectorSize));
    }

    /// <summary>
    /// A zero pair with an occupied pair after it is a hole. Zero pairs after the last
    /// occupied pair are not part of the file.
    /// </summary>
    [Test]
    public void Read_PreservesSparseHolesBeforeTheLastOccupiedPair()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var image = MutableImage(fixture);
        var sparse = fixture.Entries.Single(entry => entry.Name == "SPARSE");
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        Assume.That(sparse.HasSparseHole, Is.True);
        Assume.That(sparse.RawBytes.Length, Is.EqualTo(Dos33Layout.SectorSize * 3));
        Assume.That(hello.RawBytes.Length, Is.EqualTo(Dos33Layout.SectorSize));
        var list = Sector(image, sparse.TsTrack, sparse.TsSector);
        var pair = Dos33Layout.TrackSectorList.PairOffset;
        Assume.That(list[pair + 2], Is.EqualTo(0));
        Assume.That(list[pair + 3], Is.EqualTo(0));
        Assume.That(list[pair + 4], Is.Not.EqualTo(0));

        var fs = Open(fixture);
        var read = fs.TryReadFile("SPARSE", FileReadView.Raw);
        var helloRead = fs.TryReadFile("HELLO", FileReadView.Raw);

        Assert.That(read.Succeeded, Is.True);
        Assert.That(read.Data.ToArray(), Is.EqualTo(sparse.RawBytes.ToArray()));
        Assert.That(read.Data.Span[0], Is.EqualTo((byte)'A'));
        Assert.That(read.Data.Span.Slice(Dos33Layout.SectorSize, Dos33Layout.SectorSize).ToArray(), Is.All.EqualTo(0));
        Assert.That(read.Data.Span[Dos33Layout.SectorSize * 2], Is.EqualTo((byte)'B'));
        Assert.That(helloRead.Succeeded, Is.True);
        Assert.That(helloRead.Data.Length, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(helloRead.Data.Span[0], Is.EqualTo((byte)'H'));
    }

    /// <summary>
    /// A track/sector list that points at itself returns no bytes.
    /// </summary>
    [Test]
    public void Read_WhenTheTrackSectorListLoops_ReturnsCorruptStructure()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("KEEP", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x42 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var entry = fixture.Entries[0];
        var ts = Sector(image, entry.TsTrack, entry.TsSector);
        Assume.That(entry.TsTrack, Is.Not.Zero);
        Assume.That(ts[Dos33Layout.TrackSectorList.NextTrackOffset], Is.EqualTo(0));
        ts[Dos33Layout.TrackSectorList.NextTrackOffset] = (byte)entry.TsTrack;
        ts[Dos33Layout.TrackSectorList.NextSectorOffset] = (byte)entry.TsSector;

        var fs = Open(fixture);
        var listing = fs.ListDirectory("/");
        var read = fs.TryReadFile("keep", FileReadView.Raw);
        var logical = fs.TryReadFile("KEEP", FileReadView.Logical);

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(item => item.Name).ToArray(), Is.EqualTo(new[] { "KEEP" }));
        Assert.That(listing.Entries[0].EofBytes, Is.EqualTo(0));
        Assert.That(read.Succeeded, Is.False);
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Reason, Is.EqualTo("The track/sector list repeats a sector."));
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(logical.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(logical.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// A catalog link, list sector, or data pair past the disk is corrupt and does not throw.
    /// One bad file stays listed and the other file still reads.
    /// </summary>
    [Test]
    public void Read_WhenAPointerIsOutOfRange_ReturnsCorruptStructure()
    {
        var data = TwoFiles();
        var badList = Sector(data.Image, data.Bad.TsTrack, data.Bad.TsSector);
        var pair = Dos33Layout.TrackSectorList.PairOffset;
        Assume.That(badList[pair], Is.EqualTo((byte)data.Bad.DataSectors[0].Track));
        Assume.That(data.Fixture.TrackCount, Is.LessThan(99));
        badList[pair] = 99;

        var dataFs = Open(data.Fixture);
        var dataList = dataFs.ListDirectory(null);
        var badRead = dataFs.TryReadFile("BAD", FileReadView.Raw);
        var goodRead = dataFs.TryReadFile("GOOD", FileReadView.Raw);

        Assert.That(dataList.Succeeded, Is.True);
        Assert.That(dataList.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "GOOD", "BAD" }));
        Assert.That(dataFs.GetFileInfo("GOOD")!.EofBytes, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(dataFs.GetFileInfo("BAD")!.EofBytes, Is.EqualTo(0));
        Assert.That(badRead.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(badRead.Reason, Is.EqualTo("The track/sector list leaves the disk."));
        Assert.That(badRead.Data.Length, Is.EqualTo(0));
        Assert.That(goodRead.Succeeded, Is.True);
        Assert.That(goodRead.Data.Span[0], Is.EqualTo(0x11));

        var list = TwoFiles();
        var listSector = Sector(list.Image, list.Bad.TsTrack, list.Bad.TsSector);
        listSector[Dos33Layout.TrackSectorList.NextTrackOffset] = 1;
        listSector[Dos33Layout.TrackSectorList.NextSectorOffset] = 99;
        var listFs = Open(list.Fixture);
        var listResult = listFs.ListDirectory("/");
        var listRead = listFs.TryReadFile("BAD", FileReadView.Raw);

        Assert.That(listResult.Succeeded, Is.True);
        Assert.That(listFs.GetFileInfo("GOOD")!.EofBytes, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(listFs.GetFileInfo("BAD")!.EofBytes, Is.EqualTo(0));
        Assert.That(listRead.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listRead.Reason, Is.EqualTo("The track/sector list leaves the disk."));
        Assert.That(listFs.TryReadFile("GOOD", FileReadView.Raw).Data.Span[0], Is.EqualTo(0x11));

        var catalogBuilder = new Dos33ImageBuilder();
        for (var i = 0; i < Dos33Layout.Catalog.EntriesPerSector; i++)
        {
            catalogBuilder.AddFile(
                "C" + i.ToString(CultureInfo.InvariantCulture),
                Dos33FileType.Text,
                false,
                false,
                new byte[]?[] { new byte[] { (byte)i } });
        }

        var catalogFixture = catalogBuilder.Build();
        var catalogImage = MutableImage(catalogFixture);
        var catalogEntry = catalogFixture.Entries[0];
        Assume.That(catalogFixture.Entries, Has.Count.EqualTo(Dos33Layout.Catalog.EntriesPerSector));
        Assume.That(catalogFixture.Entries[6].CatalogSector, Is.EqualTo(catalogEntry.CatalogSector));
        var catalogSector = Sector(catalogImage, catalogEntry.CatalogTrack, catalogEntry.CatalogSector);
        Assume.That(catalogSector[Dos33Layout.Catalog.NextTrackOffset], Is.EqualTo(0));
        catalogSector[Dos33Layout.Catalog.NextTrackOffset] = Dos33Layout.VtocTrack;
        catalogSector[Dos33Layout.Catalog.NextSectorOffset] = 99;
        var catalogFs = Open(catalogFixture);
        var catalogList = catalogFs.ListDirectory(null);
        var catalogRead = catalogFs.TryReadFile("C0", FileReadView.Raw);

        Assert.That(catalogList.Succeeded, Is.False);
        Assert.That(catalogList.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(catalogList.Reason, Is.EqualTo("The DOS catalog chain leaves the disk."));
        Assert.That(catalogList.Entries, Is.Empty);
        Assert.That(catalogFs.GetFileInfo("C0"), Is.Null);
        Assert.That(catalogRead.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(catalogRead.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// A list sector whose base is not its position times 122 is corrupt. <c>$FFFF</c> is not a 16MB file.
    /// </summary>
    [Test]
    public void Read_WhenTheSectorOffsetDisagrees_ReturnsCorruptStructure()
    {
        var wide = TwoFiles();
        var wideList = Sector(wide.Image, wide.Bad.TsTrack, wide.Bad.TsSector);
        Assume.That(wideList[Dos33Layout.TrackSectorList.SectorBaseOffset], Is.EqualTo(0));
        Assume.That(wideList[Dos33Layout.TrackSectorList.SectorBaseOffset + 1], Is.EqualTo(0));
        wideList[Dos33Layout.TrackSectorList.SectorBaseOffset] = 0xFF;
        wideList[Dos33Layout.TrackSectorList.SectorBaseOffset + 1] = 0xFF;

        var wideFs = Open(wide.Fixture);
        var wideRead = wideFs.TryReadFile("BAD", FileReadView.Raw);

        Assert.That(wideFs.ListDirectory(null).Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "GOOD", "BAD" }));
        Assert.That(wideFs.GetFileInfo("BAD")!.EofBytes, Is.EqualTo(0));
        Assert.That(wideRead.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(wideRead.Reason, Is.EqualTo("The track/sector list sector offset is wrong."));
        Assert.That(wideRead.Data.Length, Is.EqualTo(0));
        Assert.That(wideFs.TryReadFile("GOOD", FileReadView.Raw).Succeeded, Is.True);
        Assert.That(wideFs.TryReadFile("GOOD", FileReadView.Raw).Data.Span[0], Is.EqualTo(0x11));

        var builder = new Dos33ImageBuilder();
        builder.AddFile("GOOD", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        builder.AddFile("CHAIN", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x21 }, new byte[] { 0x22 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var chain = fixture.Entries[1];
        var ts = Sector(image, chain.TsTrack, chain.TsSector);
        Assume.That(TryFindZeroSector(image, fixture.TrackCount, out var linkTrack, out var linkSector), Is.True);
        var pair = Dos33Layout.TrackSectorList.PairOffset;
        ts[Dos33Layout.TrackSectorList.NextTrackOffset] = (byte)linkTrack;
        ts[Dos33Layout.TrackSectorList.NextSectorOffset] = (byte)linkSector;
        var link = Sector(image, linkTrack, linkSector);
        link[pair] = ts[pair + 2];
        link[pair + 1] = ts[pair + 3];
        ts[pair + 2] = 0;
        ts[pair + 3] = 0;
        Assume.That(link[Dos33Layout.TrackSectorList.SectorBaseOffset], Is.EqualTo(0));

        var fs = Open(fixture);
        var read = fs.TryReadFile("CHAIN", FileReadView.Raw);

        Assert.That(fs.GetFileInfo("GOOD")!.EofBytes, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(fs.GetFileInfo("CHAIN")!.EofBytes, Is.EqualTo(0));
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Reason, Is.EqualTo("The track/sector list sector offset is wrong."));
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.TryReadFile("GOOD", FileReadView.Raw).Data.Span[0], Is.EqualTo(0x11));
    }

    /// <summary>
    /// A logical file longer than the disk is corrupt and is not allocated.
    /// </summary>
    [Test]
    public void Read_WhenTheLogicalLengthExceedsTheDisk_ReturnsCorruptStructure()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("GOOD", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        builder.AddFile("LONG", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x22 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var diskSectors = fixture.TrackCount * Dos33Layout.DefaultSectorsPerTrack;
        var origin = 2 * Dos33Layout.TrackSectorList.PairsPerSector;
        var slot = diskSectors - origin;
        Assume.That(slot, Is.GreaterThanOrEqualTo(0));
        Assume.That(slot, Is.LessThan(Dos33Layout.TrackSectorList.PairsPerSector));
        var entry = fixture.Entries.Single(item => item.Name == "LONG");
        var data = entry.DataSectors[0];
        Assume.That(data.Allocated, Is.True);
        Assume.That(TryFindZeroSector(image, fixture.TrackCount, out var middleTrack, out var middleSector), Is.True);
        image[((middleTrack * Dos33Layout.DefaultSectorsPerTrack) + middleSector) * Dos33Layout.SectorSize] = 1;
        Assume.That(TryFindZeroSector(image, fixture.TrackCount, out var lastTrack, out var lastSector), Is.True);
        Assume.That(middleTrack != lastTrack || middleSector != lastSector, Is.True);

        var ts = Sector(image, entry.TsTrack, entry.TsSector);
        Assume.That(ts[Dos33Layout.TrackSectorList.SectorBaseOffset], Is.EqualTo(0));
        ts[Dos33Layout.TrackSectorList.NextTrackOffset] = (byte)middleTrack;
        ts[Dos33Layout.TrackSectorList.NextSectorOffset] = (byte)middleSector;
        var middle = Sector(image, middleTrack, middleSector);
        middle.Clear();
        middle[Dos33Layout.TrackSectorList.SectorBaseOffset] = (byte)Dos33Layout.TrackSectorList.PairsPerSector;
        middle[Dos33Layout.TrackSectorList.NextTrackOffset] = (byte)lastTrack;
        middle[Dos33Layout.TrackSectorList.NextSectorOffset] = (byte)lastSector;
        var last = Sector(image, lastTrack, lastSector);
        last[Dos33Layout.TrackSectorList.SectorBaseOffset] = (byte)origin;
        var pair = Dos33Layout.TrackSectorList.PairOffset + (slot * 2);
        last[pair] = (byte)data.Track;
        last[pair + 1] = (byte)data.Sector;

        var fs = Open(fixture);
        var read = fs.TryReadFile("LONG", FileReadView.Raw);

        Assert.That(fs.ListDirectory(null).Entries.Select(item => item.Name).ToArray(), Is.EqualTo(new[] { "GOOD", "LONG" }));
        Assert.That(fs.GetFileInfo("LONG")!.EofBytes, Is.EqualTo(0));
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Reason, Is.EqualTo("The track/sector list is longer than the disk."));
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.TryReadFile("GOOD", FileReadView.Raw).Succeeded, Is.True);
        Assert.That(fs.TryReadFile("GOOD", FileReadView.Raw).Data.Span[0], Is.EqualTo(0x11));
    }

    /// <summary>
    /// A track byte of zero ends the catalog. A later live entry, including one on the next sector, is not listed.
    /// </summary>
    [Test]
    public void Catalog_StopsAtTheFirstUnusedTrackByte()
    {
        var builder = new Dos33ImageBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.AddFile(
                "F" + i.ToString(CultureInfo.InvariantCulture),
                Dos33FileType.Text,
                false,
                false,
                new byte[]?[] { new byte[] { (byte)(0x30 + i) } });
        }

        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var hole = fixture.Entries[2];
        var after = fixture.Entries[3];
        var nextSector = fixture.Entries[7];
        Assume.That(hole.CatalogSector, Is.EqualTo(Dos33Layout.DefaultFirstCatalogSector));
        Assume.That(after.CatalogSector, Is.EqualTo(hole.CatalogSector));
        Assume.That(after.CatalogSlot, Is.GreaterThan(hole.CatalogSlot));
        Assume.That(nextSector.CatalogSector, Is.EqualTo(14));
        Assume.That(image[EntryOffset(hole) + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo((byte)hole.TsTrack));
        Assume.That(hole.TsTrack, Is.Not.Zero);
        image[EntryOffset(hole) + Dos33Layout.CatalogEntry.TrackOffset] = Dos33Layout.UnusedTrack;
        Assume.That(image[EntryOffset(after) + Dos33Layout.CatalogEntry.TrackOffset], Is.Not.EqualTo(Dos33Layout.UnusedTrack));

        var fs = Open(fixture);
        var listing = fs.ListDirectory(null);

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "F0", "F1" }));
        Assert.That(fs.GetFileInfo("F3"), Is.Null);
        Assert.That(fs.GetFileInfo("F7"), Is.Null);
        Assert.That(fs.TryReadFile("F3", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        var kept = fs.TryReadFile("F0", FileReadView.Raw);
        Assert.That(kept.Succeeded, Is.True);
        Assert.That(kept.Data.Span[0], Is.EqualTo(0x30));
    }

    /// <summary>
    /// A live entry whose name is only padding is skipped. The files around it stay readable.
    /// </summary>
    [Test]
    public void Catalog_SkipsABlankNameWithoutFailingTheVolume()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("KEEP", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        builder.AddFile("BLANK", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x22 } });
        builder.AddFile("TAIL", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x33 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var blank = fixture.Entries[1];
        var tail = fixture.Entries[2];
        Assume.That(blank.CatalogSlot, Is.LessThan(tail.CatalogSlot));
        Assume.That(image[EntryOffset(blank) + Dos33Layout.CatalogEntry.TrackOffset], Is.EqualTo((byte)blank.TsTrack));
        Assume.That(blank.TsTrack, Is.Not.EqualTo(Dos33Layout.UnusedTrack));
        var nameAt = EntryOffset(blank) + Dos33Layout.CatalogEntry.NameOffset;
        for (var i = 0; i < Dos33Layout.CatalogEntry.NameLength; i++)
        {
            image[nameAt + i] = Dos33Layout.CatalogEntry.HighBitSpace;
        }

        var fs = Open(fixture);
        var listing = fs.ListDirectory(null);
        var tailRead = fs.TryReadFile("TAIL", FileReadView.Raw);

        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.Success));
        Assert.That(listing.Entries.Select(entry => entry.Name).ToArray(), Is.EqualTo(new[] { "KEEP", "TAIL" }));
        Assert.That(fs.GetFileInfo("BLANK"), Is.Null);
        Assert.That(fs.TryReadFile("BLANK", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(tailRead.Succeeded, Is.True);
        Assert.That(tailRead.Data.Span[0], Is.EqualTo(0x33));
        Assert.That(fs.TryReadFile("KEEP", FileReadView.Raw).Data.Span[0], Is.EqualTo(0x11));
    }

    /// <summary>
    /// A catalog sector that points at itself is not an empty volume and not a partial list.
    /// </summary>
    [Test]
    public void List_WhenTheCatalogChainLoops_ReturnsCorruptStructure()
    {
        var builder = new Dos33ImageBuilder();
        for (var i = 0; i < Dos33Layout.Catalog.EntriesPerSector; i++)
        {
            builder.AddFile(
                "F" + i.ToString(CultureInfo.InvariantCulture),
                Dos33FileType.Text,
                false,
                false,
                new byte[]?[] { new byte[] { (byte)i } });
        }

        var fixture = builder.Build();
        var image = MutableImage(fixture);
        var entry = fixture.Entries[0];
        Assume.That(fixture.Entries, Has.Count.EqualTo(Dos33Layout.Catalog.EntriesPerSector));
        Assume.That(fixture.Entries[6].CatalogSector, Is.EqualTo(entry.CatalogSector));
        var catalog = Sector(image, entry.CatalogTrack, entry.CatalogSector);
        Assume.That(entry.CatalogSector, Is.EqualTo(Dos33Layout.DefaultFirstCatalogSector));
        Assume.That(catalog[Dos33Layout.Catalog.NextTrackOffset], Is.EqualTo(0));
        catalog[Dos33Layout.Catalog.NextTrackOffset] = (byte)entry.CatalogTrack;
        catalog[Dos33Layout.Catalog.NextSectorOffset] = (byte)entry.CatalogSector;

        var fs = Open(fixture);
        var listing = fs.ListDirectory(null);
        var read = fs.TryReadFile("F0", FileReadView.Raw);

        Assert.That(listing.Succeeded, Is.False);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Reason, Is.EqualTo("The DOS catalog chain repeats a sector."));
        Assert.That(listing.Entries, Is.Empty);
        Assert.That(fs.GetFileInfo("F0"), Is.Null);
        Assert.That(read.Succeeded, Is.False);
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.TryReadFile("/", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
    }

    /// <summary>
    /// The logical view strips Applesoft, Integer, and binary headers and trims to the length word.
    /// Text is unchanged. A second read still returns the raw header.
    /// </summary>
    [Test]
    public void Read_LogicalViewStripsApplesoftIntegerAndBinaryHeaders()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var fs = Open(fixture);
        AssertLogical(fs, fixture, "LOCKED", new byte[] { (byte)'O', (byte)'K' });
        AssertLogical(fs, fixture, "BASIC", new byte[] { 0x11, 0x22 });
        AssertLogical(fs, fixture, "INTBAS", new byte[] { 0x33 });

        var hello = fs.TryReadFile("HELLO", FileReadView.Logical);
        Assert.That(hello.Succeeded, Is.True);
        Assert.That(hello.Data.Length, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(hello.Data.Span[0], Is.EqualTo((byte)'H'));
        Assert.That(hello.Data.ToArray(), Is.EqualTo(fs.TryReadFile("HELLO", FileReadView.Raw).Data.ToArray()));

        var locked = fs.TryReadFile("LOCKED", FileReadView.Logical);
        Assert.That(locked.Data.ToArray(), Is.EqualTo(new byte[] { (byte)'O', (byte)'K' }));
        var lockedRaw = fs.TryReadFile("LOCKED", FileReadView.Raw);
        Assert.That(lockedRaw.Succeeded, Is.True);
        Assert.That(lockedRaw.Data.Length, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(lockedRaw.Data.Span[0], Is.EqualTo(0xA0));
        Assert.That(lockedRaw.Data.Span[4], Is.EqualTo((byte)'O'));

        var builder = new Dos33ImageBuilder();
        builder.AddFile(
            "MORE",
            Dos33FileType.AdditionalBinary,
            false,
            false,
            new byte[]?[] { new byte[] { 0x34, 0x12, 0x03, 0x00, 0xAA, 0xBB, 0xCC } });
        builder.AddFile(
            "MOREA",
            Dos33FileType.AdditionalApplesoft,
            false,
            false,
            new byte[]?[] { new byte[] { 0x02, 0x00, 0x7E, 0x7F } });
        builder.AddFile(
            "ZERO",
            Dos33FileType.Binary,
            false,
            false,
            new byte[]?[] { new byte[] { 0x00, 0x08, 0x00, 0x00, 0xFF } });
        var extra = Open(builder.Build());
        var more = extra.TryReadFile("MORE", FileReadView.Logical);
        var moreA = extra.TryReadFile("MOREA", FileReadView.Logical);
        var zero = extra.TryReadFile("ZERO", FileReadView.Logical);

        Assert.That(more.Succeeded, Is.True);
        Assert.That(more.Data.ToArray(), Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
        Assert.That(moreA.Succeeded, Is.True);
        Assert.That(moreA.Data.ToArray(), Is.EqualTo(new byte[] { 0x7E, 0x7F }));
        Assert.That(zero.Succeeded, Is.True);
        Assert.That(zero.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// A binary or BASIC file with no data sectors is shorter than its header.
    /// </summary>
    [Test]
    public void Read_WhenTheFileIsShorterThanItsHeader_ReturnsCorruptStructure()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("NOTE", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x01 } });
        builder.AddFile("SHORT", Dos33FileType.Binary, false, false, new byte[]?[] { new byte[] { 0x01, 0x02, 0x03, 0x04 } });
        var fixture = builder.Build();
        var image = MutableImage(fixture);
        Assume.That(fixture.Entries, Has.Count.EqualTo(2));
        foreach (var entry in fixture.Entries)
        {
            Sector(image, entry.TsTrack, entry.TsSector).Clear();
        }

        var fs = Open(fixture);
        var text = fs.TryReadFile("NOTE", FileReadView.Logical);
        var binary = fs.TryReadFile("SHORT", FileReadView.Logical);
        var raw = fs.TryReadFile("SHORT", FileReadView.Raw);

        Assert.That(text.Succeeded, Is.True);
        Assert.That(text.Data.Length, Is.EqualTo(0));
        Assert.That(raw.Succeeded, Is.True);
        Assert.That(raw.Data.Length, Is.EqualTo(0));
        Assert.That(binary.Succeeded, Is.False);
        Assert.That(binary.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(binary.Reason, Is.EqualTo("The file is shorter than its DOS header."));
        Assert.That(binary.Data.Length, Is.EqualTo(0));
    }

    /// <summary>
    /// The volume name is the volume number. A blank catalog is an empty root.
    /// </summary>
    [Test]
    public void VolumeName_IsTheDecimalVolumeNumber()
    {
        var blank = Dos33Fixture.CreateBlank(7);
        var fs = Open(blank);
        var listing = fs.ListDirectory(null);

        Assert.That(fs.VolumeName, Is.EqualTo("7"));
        Assert.That(fs.Kind, Is.EqualTo(AppleFileSystemKind.Dos33));
        Assert.That(Open(Dos33Fixture.CreateCatalogSample()).VolumeName, Is.EqualTo("254"));
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries, Is.Empty);
        Assert.That(fs.GetFileInfo("/7"), Is.Null);
        Assert.That(fs.TryReadFile("/7", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
    }

    /// <summary>
    /// DOS names are one flat segment. A slash stays inside the name, and lookup ignores case.
    /// </summary>
    [Test]
    public void Paths_ResolveFlatNamesWithoutADirectoryTree()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var image = MutableImage(fixture);
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        Assume.That(fixture.VolumeName, Is.EqualTo("254"));
        WriteName(image, hello, "A/B");
        var fs = Open(fixture);
        var live = fixture.Entries.Count(entry => !entry.IsDeleted);

        Assert.That(fs.GetFileInfo("a/b")!.Name, Is.EqualTo("A/B"));
        Assert.That(fs.GetFileInfo("/A/B")!.Path, Is.EqualTo("A/B"));
        Assert.That(fs.TryReadFile("A/B", FileReadView.Raw).Succeeded, Is.True);
        Assert.That(fs.ListDirectory("A/B").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.GetFileInfo("/254"), Is.Null);
        Assert.That(fs.GetFileInfo(string.Empty), Is.Null);
        Assert.That(fs.GetFileInfo("/"), Is.Null);
        Assert.That(fs.GetFileInfo("254"), Is.Null);
        Assert.That(fs.GetFileInfo("254/HELLO"), Is.Null);
        Assert.That(fs.TryReadFile("/254/HELLO", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile(string.Empty, FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile("/", FileReadView.Logical).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.ListDirectory("missing").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.ListDirectory("///").Succeeded, Is.True);
        Assert.That(fs.ListDirectory("///").Entries, Has.Count.EqualTo(live));
        Assert.That(fs.ListDirectory("A\0B").Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.TryReadFile("A\0B", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.GetFileInfo("A\0B"), Is.Null);
        Assert.Throws<ArgumentNullException>(() => fs.GetFileInfo(null!));
        Assert.Throws<ArgumentNullException>(() => fs.TryReadFile(null!, FileReadView.Raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.TryReadFile("A/B", (FileReadView)99));
    }

    /// <summary>
    /// A DOS VTOC opens. Anything else is a rejection, and a sector read failure is not one.
    /// </summary>
    [Test]
    public void Probe_AcceptsADosVtocAndRejectsOtherBytes()
    {
        var probe = new Dos33FileSystemProbe(Logger());
        var fixture = Dos33Fixture.CreateCatalogSample();
        var opened = probe.TryOpen(new FileSystemSource(null, fixture.CreateSectorReader()));
        var blocksOnly = probe.TryOpen(new FileSystemSource(new MemoryBlockReader(new byte[512], 1, 512), null));
        var zeros = probe.TryOpen(new FileSystemSource(null, new MemorySectorReader(new byte[18 * 16 * 256], 18, 16, 256)));
        var narrow = probe.TryOpen(new FileSystemSource(null, new MemorySectorReader(new byte[18 * 16 * 128], 18, 16, 128)));
        var shortDisk = probe.TryOpen(new FileSystemSource(null, new MemorySectorReader(new byte[16 * 256], 1, 16, 256)));
        var image = MutableImage(Dos33Fixture.CreateBlank());
        var versionAt = (Dos33Layout.VtocTrack * Dos33Layout.DefaultSectorsPerTrack * Dos33Layout.SectorSize) + Dos33Layout.Vtoc.DosVersionOffset;
        Assume.That(image[versionAt], Is.EqualTo(Dos33Layout.Vtoc.Version33));
        image[versionAt] = 2;
        var wrongVersion = probe.TryOpen(new FileSystemSource(null, new MemorySectorReader(image, 18, 16, 256)));

        Assert.That(opened.Recognized, Is.True);
        Assert.That(opened.Reason, Is.EqualTo("DOS 3.3 VTOC version is 3."));
        Assert.That(opened.FileSystem!.Kind, Is.EqualTo(AppleFileSystemKind.Dos33));
        Assert.That(opened.FileSystem.VolumeName, Is.EqualTo(fixture.VolumeName));
        Assert.That(opened.FileSystem.ListDirectory(null).Entries, Has.Count.EqualTo(5));
        Assert.That(blocksOnly.Recognized, Is.False);
        Assert.That(blocksOnly.Reason, Is.EqualTo("DOS 3.3 needs a sector reader."));
        Assert.That(blocksOnly.FileSystem, Is.Null);
        Assert.That(zeros.Recognized, Is.False);
        Assert.That(zeros.Reason, Is.EqualTo("Track 17 sector 0 is not a DOS 3.3 VTOC."));
        Assert.That(narrow.Reason, Is.EqualTo("DOS 3.3 needs 16 sectors of 256 bytes."));
        Assert.That(shortDisk.Reason, Is.EqualTo("The image has no track 17."));
        Assert.That(wrongVersion.Recognized, Is.False);
        Assert.That(wrongVersion.Reason, Is.EqualTo("Track 17 sector 0 is not a DOS 3.3 VTOC."));

        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { probe });
        var detected = detector.Detect(new FileSystemSource(null, fixture.CreateSectorReader()));
        Assert.That(detected.IsRecognized, Is.True);
        Assert.That(detected.FileSystem!.VolumeName, Is.EqualTo("254"));
        Assert.That(detected.Attempts[0].Kind, Is.EqualTo(AppleFileSystemKind.Dos33));
        var thrown = Assert.Throws<IOException>(() => probe.TryOpen(new FileSystemSource(null, new ThrowingSectorReader())));
        Assert.That(thrown!.Message, Is.EqualTo("unreadable sector"));
        Assert.Throws<ArgumentNullException>(() => probe.TryOpen(null!));
        Assert.Throws<ArgumentNullException>(() => new Dos33FileSystemProbe(null!));
    }

    /// <summary>
    /// The constructor rejects a reader that skipped the VTOC check.
    /// </summary>
    [Test]
    public void Constructor_ThrowsWhenTheVtocCheckWasSkipped()
    {
        var zeros = new MemorySectorReader(new byte[18 * 16 * 256], 18, 16, 256);
        var narrow = new MemorySectorReader(new byte[18 * 16 * 128], 18, 16, 128);
        var shortDisk = new MemorySectorReader(new byte[16 * 256], 1, 16, 256);
        var logger = Logger();

        var missing = Assert.Throws<ArgumentException>(() => new Dos33FileSystem(zeros, logger));
        Assert.That(missing!.Message, Does.Contain("VTOC"));
        Assert.Throws<ArgumentException>(() => new Dos33FileSystem(narrow, logger));
        Assert.Throws<ArgumentException>(() => new Dos33FileSystem(shortDisk, logger));
        Assert.Throws<ArgumentNullException>(() => new Dos33FileSystem(null!, logger));
        Assert.Throws<ArgumentNullException>(() => new Dos33FileSystem(Dos33Fixture.CreateBlank().CreateSectorReader(), null!));
        Assert.That(new Dos33FileSystem(Dos33Fixture.CreateBlank(12).CreateSectorReader(), logger).VolumeName, Is.EqualTo("12"));
    }

    /// <summary>
    /// An I/O failure from a data sector is not turned into a catalog status.
    /// </summary>
    [Test]
    public void Read_WhenTheSectorReaderThrows_PropagatesTheException()
    {
        var fixture = Dos33Fixture.CreateCatalogSample();
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        var data = hello.DataSectors[0];
        Assume.That(data.Allocated, Is.True);
        Assume.That(data.Track == hello.TsTrack && data.Sector == hello.TsSector, Is.False);
        Assume.That(data.Track, Is.Not.EqualTo(Dos33Layout.VtocTrack));
        var fs = new Dos33FileSystem(
            new FailSectorReader(fixture.CreateSectorReader(), data.Track, data.Sector),
            Logger());

        Assert.That(fs.ListDirectory(null).Succeeded, Is.True);
        var error = Assert.Throws<IOException>(() => fs.TryReadFile("HELLO", FileReadView.Raw));
        Assert.That(error!.Message, Is.EqualTo("failed data sector"));
    }

    private static void AssertLogical(Dos33FileSystem fs, Dos33Fixture fixture, string name, byte[] expected)
    {
        var entry = fixture.Entries.Single(item => item.Name == name);
        Assume.That(entry.LogicalBytes.ToArray(), Is.EqualTo(expected), name);
        var result = fs.TryReadFile(name, FileReadView.Logical);
        Assert.That(result.Succeeded, Is.True, name);
        Assert.That(result.Data.ToArray(), Is.EqualTo(expected), name);
    }

    private static Dos33FileSystem Open(Dos33Fixture fixture) =>
        new(fixture.CreateSectorReader(), Logger());

    private static (Dos33Fixture Fixture, byte[] Image, Dos33Fixture.Entry Good, Dos33Fixture.Entry Bad) TwoFiles()
    {
        var builder = new Dos33ImageBuilder();
        builder.AddFile("GOOD", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        builder.AddFile("BAD", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x22 } });
        var fixture = builder.Build();
        var good = fixture.Entries[0];
        var bad = fixture.Entries[1];
        Assume.That(good.TsTrack, Is.Not.Zero);
        Assume.That(bad.TsTrack, Is.Not.Zero);
        Assume.That(bad.DataSectors[0].Allocated, Is.True);
        return (fixture, MutableImage(fixture), good, bad);
    }

    private static ILogger Logger() => Generator.Log().Object;

    private static byte[] MutableImage(Dos33Fixture fixture)
    {
        Assume.That(MemoryMarshal.TryGetArray(fixture.Image, out var segment), Is.True);
        Assume.That(segment.Array, Is.Not.Null);
        Assume.That(segment.Offset, Is.EqualTo(0));
        Assume.That(segment.Count, Is.EqualTo(fixture.Image.Length));
        return segment.Array!;
    }

    private static Span<byte> Sector(byte[] image, int track, int sector)
    {
        var offset = ((track * Dos33Layout.DefaultSectorsPerTrack) + sector) * Dos33Layout.SectorSize;
        return image.AsSpan(offset, Dos33Layout.SectorSize);
    }

    private static int EntryOffset(Dos33Fixture.Entry entry) =>
        EntryOffset(entry.CatalogTrack, entry.CatalogSector, entry.CatalogSlot);

    private static int EntryOffset(int track, int sector, int slot)
    {
        var sectorOffset = ((track * Dos33Layout.DefaultSectorsPerTrack) + sector) * Dos33Layout.SectorSize;
        return sectorOffset + Dos33Layout.Catalog.HeaderLength + (slot * Dos33Layout.CatalogEntry.Length);
    }

    private static void WriteEntry(byte[] image, int track, int sector, int slot, byte tsTrack, byte tsSector, byte typeByte, string name)
    {
        var entry = EntryOffset(track, sector, slot);
        image[entry + Dos33Layout.CatalogEntry.TrackOffset] = tsTrack;
        image[entry + Dos33Layout.CatalogEntry.SectorOffset] = tsSector;
        image[entry + Dos33Layout.CatalogEntry.TypeOffset] = typeByte;
        var nameAt = entry + Dos33Layout.CatalogEntry.NameOffset;
        for (var i = 0; i < Dos33Layout.CatalogEntry.NameLength; i++)
        {
            image[nameAt + i] = Dos33Layout.CatalogEntry.HighBitSpace;
        }

        for (var i = 0; i < name.Length; i++)
        {
            image[nameAt + i] = (byte)(name[i] | 0x80);
        }

        image[entry + Dos33Layout.CatalogEntry.SectorCountOffset] = 2;
    }

    private static void WriteName(byte[] image, Dos33Fixture.Entry entry, string name)
    {
        var nameAt = EntryOffset(entry) + Dos33Layout.CatalogEntry.NameOffset;
        for (var i = 0; i < Dos33Layout.CatalogEntry.NameLength; i++)
        {
            image[nameAt + i] = Dos33Layout.CatalogEntry.HighBitSpace;
        }

        for (var i = 0; i < name.Length; i++)
        {
            image[nameAt + i] = (byte)(name[i] | 0x80);
        }
    }

    private static bool TryFindZeroSector(byte[] image, int trackCount, out int track, out int sector)
    {
        for (track = 1; track < trackCount; track++)
        {
            if (track == Dos33Layout.VtocTrack)
            {
                continue;
            }

            for (sector = 0; sector < Dos33Layout.DefaultSectorsPerTrack; sector++)
            {
                var empty = true;
                var span = Sector(image, track, sector);
                for (var i = 0; i < span.Length; i++)
                {
                    if (span[i] != 0)
                    {
                        empty = false;
                        break;
                    }
                }

                if (empty)
                {
                    return true;
                }
            }
        }

        track = 0;
        sector = 0;
        return false;
    }

    private sealed class ThrowingSectorReader : ISectorReader
    {
        public int BytesPerSector => Dos33Layout.SectorSize;

        public int TrackCount => 18;

        public int SectorsPerTrack => Dos33Layout.DefaultSectorsPerTrack;

        public void ReadSector(int track, int sector, Span<byte> destination) =>
            throw new IOException("unreadable sector");
    }

    private sealed class FailSectorReader : ISectorReader
    {
        private readonly ISectorReader inner;
        private readonly int failTrack;
        private readonly int failSector;

        public FailSectorReader(ISectorReader inner, int failTrack, int failSector)
        {
            this.inner = inner;
            this.failTrack = failTrack;
            this.failSector = failSector;
        }

        public int BytesPerSector => this.inner.BytesPerSector;

        public int TrackCount => this.inner.TrackCount;

        public int SectorsPerTrack => this.inner.SectorsPerTrack;

        public void ReadSector(int track, int sector, Span<byte> destination)
        {
            if (track == this.failTrack && sector == this.failSector)
            {
                throw new IOException("failed data sector");
            }

            this.inner.ReadSector(track, sector, destination);
        }
    }
}