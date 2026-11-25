// <copyright file="VolumeFileMapTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;

using Serilog;

/// <summary>
/// Block and track/sector maps over synthetic ProDOS and DOS 3.3 volumes.
/// </summary>
[TestFixture]
public class VolumeFileMapTests
{
    /// <summary>
    /// Data blocks map to the file. Index, directory, and extended blocks do not.
    /// </summary>
    [Test]
    public void ProdosSample_MapsDataBlocksAndSkipsIndexes()
    {
        var fixture = ProdosFixture.CreateCatalogSample();
        var map = VolumeFileMap.TryBuild(fixture.CreateBlockReader(), sectors: null, Logger());

        Assert.That(map.Recognized, Is.True);
        Assert.That(map.Kind, Is.EqualTo(AppleFileSystemKind.Prodos));
        Assert.That(map.BootImageSectorCount, Is.Zero);
        Assert.That(map.TryLocateBlock(0, out _), Is.False);
        Assert.That(map.TryLocateBlock(ProdosLayout.VolumeKeyBlock, out _), Is.False);

        foreach (var entry in fixture.Entries)
        {
            if (entry.IsDirectory)
            {
                Assert.That(map.TryLocateBlock(entry.KeyBlock, out _), Is.False, entry.Path);
                var note = entry.Children.Single();
                Assert.That(map.TryLocateBlock(note.KeyBlock, out var child), Is.True);
                Assert.That(child.Path, Is.EqualTo(note.Path));
                Assert.That(child.FileLength, Is.EqualTo(note.EofBytes));
                continue;
            }

            if (entry.StorageKind == FileStorageKind.ProdosExtended)
            {
                Assert.That(Located(map, fixture.BlockCount, entry.Path), Is.Empty);
                continue;
            }

            if (entry.StorageKind == FileStorageKind.ProdosSeedling)
            {
                Assert.That(map.TryLocateBlock(entry.KeyBlock, out var seedling), Is.True);
                Assert.That(seedling.Path, Is.EqualTo(entry.Path));
                Assert.That(seedling.Offset, Is.Zero);
                Assert.That(seedling.Length, Is.EqualTo(Math.Min(entry.EofBytes, ProdosLayout.BlockSize)));
                Assert.That(seedling.FileLength, Is.EqualTo(entry.EofBytes));
                var sectorTrack = entry.KeyBlock / 8;
                var logical = (entry.KeyBlock % 8) * 2;
                var physical = SectorSkew.LogicalToPhysical(SectorOrder.ProDos, logical);
                Assert.That(map.TryLocatePhysicalSector(sectorTrack, physical, out var sectorHit, out _, out _), Is.True);
                Assert.That(sectorHit.Path, Is.EqualTo(entry.Path));
                Assert.That(map.TryLocatePhysicalSector(sectorTrack + (1 << 29), physical, out _, out _, out _), Is.False);
                Assert.That(map.TryLocatePhysicalSector(map.BlockCount, physical, out _, out _, out _), Is.False);
                continue;
            }

            Assert.That(map.TryLocateBlock(entry.KeyBlock, out _), Is.False, entry.Path);
            var pointers = entry.StorageKind == FileStorageKind.ProdosTree
                ? entry.ChildIndexPointers
                : entry.IndexPointers;
            var mapped = 0;
            foreach (var pointer in pointers)
            {
                if (pointer == 0)
                {
                    continue;
                }

                Assert.That(map.TryLocateBlock(pointer, out var data), Is.True, entry.Path);
                Assert.That(data.Path, Is.EqualTo(entry.Path));
                Assert.That(data.FileLength, Is.EqualTo(entry.EofBytes));
                mapped++;
            }

            Assert.That(mapped, Is.GreaterThan(0), entry.Path);
            Assert.That(map.TryGetFile(entry.Path, out var size), Is.True);
            Assert.That(size.Units, Is.EqualTo(mapped));
            Assert.That(size.Length, Is.EqualTo(entry.EofBytes));
        }
    }

    /// <summary>
    /// DOS data sectors map through the physical address-field sector. Tracks 0–2 are the boot image.
    /// </summary>
    [Test]
    public void Dos33_MapsFileSectorsAndBootTracks()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var builder = new Dos33ImageBuilder(35, 254);
        for (var i = 0; i < 16; i++)
        {
            builder.AddFile($"F{i:00}", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body });
        }

        var fixture = builder.AddFile("HELLO", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body }).Build();
        var map = VolumeFileMap.TryBuild(blocks: null, new MemorySectorReader(fixture.Image, fixture.TrackCount, 16, 256), Logger());
        var hello = fixture.Entries.Single(entry => entry.Name == "HELLO");
        var data = hello.DataSectors.Single(sector => sector.Allocated);

        Assert.That(map.Kind, Is.EqualTo(AppleFileSystemKind.Dos33));
        Assert.That(map.BootImageSectorCount, Is.EqualTo(48));
        Assert.That(data.Track, Is.GreaterThanOrEqualTo(VolumeFileMap.DosBootTrackCount));
        Assert.That(map.IsBootImageSector(0, 0), Is.True);
        Assert.That(map.IsBootImageSector(2, 15), Is.True);
        Assert.That(map.IsBootImageSector(data.Track, 0), Is.False);
        Assert.That(map.TryLocatePhysicalSector(0, 0, out _, out _, out _), Is.False);

        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);
        Assert.That(map.TryLocatePhysicalSector(data.Track, physical, out var located, out var block, out var half), Is.True);
        Assert.That(located.Path, Is.EqualTo("HELLO"));
        Assert.That(located.Length, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(located.FileLength, Is.EqualTo(Dos33Layout.SectorSize));
        Assert.That(block, Is.EqualTo(-1));
        Assert.That(half, Is.EqualTo(-1));
        Assert.That(map.TryLocateBlock(0, out _), Is.False);
        Assert.That(map.TryLocatePhysicalSector(fixture.TrackCount, physical, out _, out _, out _), Is.False);
        Assert.That(map.TryLocatePhysicalSector(data.Track + 0x01000000, physical, out _, out _, out _), Is.False);
    }

    /// <summary>
    /// A volume that is not ProDOS or DOS 3.3, or that cannot be read, is an empty map.
    /// </summary>
    [Test]
    public void UnreadableVolume_IsUnrecognizedAndDoesNotThrow()
    {
        var zeros = new MemoryBlockReader(new byte[8 * ProdosLayout.BlockSize], 8, ProdosLayout.BlockSize);
        var blank = VolumeFileMap.TryBuild(zeros, sectors: null, Logger());
        var broken = VolumeFileMap.TryBuild(new ThrowingBlocks(), sectors: null, Logger());

        Assert.That(blank.Recognized, Is.False);
        Assert.That(broken.Recognized, Is.False);
        Assert.That(blank.TryLocateBlock(2, out _), Is.False);
        Assert.That(broken.TryLocatePhysicalSector(0, 0, out _, out _, out _), Is.False);
        Assert.That(VolumeFileMap.Unrecognized.IsBootImageSector(0, 0), Is.False);
    }

    private static List<int> Located(VolumeFileMap map, int blockCount, string path)
    {
        var found = new List<int>();
        for (var block = 0; block < blockCount; block++)
        {
            if (map.TryLocateBlock(block, out var location) && location.Path == path)
            {
                found.Add(block);
            }
        }

        return found;
    }

    private static ILogger Logger() => Serilog.Core.Logger.None;

    private sealed class ThrowingBlocks : IBlockReader
    {
        public int BlockSize => ProdosLayout.BlockSize;

        public int BlockCount => 8;

        public void ReadBlock(int blockIndex, Span<byte> destination) => throw new IOException("unreadable");
    }
}