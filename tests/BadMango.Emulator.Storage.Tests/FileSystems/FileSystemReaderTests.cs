// <copyright file="FileSystemReaderTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

/// <summary>
/// Adapters that wrap in-memory bytes and <see cref="SectorImageMedia"/> without taking callbacks.
/// </summary>
[TestFixture]
public class FileSystemReaderTests
{
    /// <summary>
    /// A memory block reader returns the requested block and rejects a bad index or length.
    /// </summary>
    [Test]
    public void MemoryBlockReader_ReadsOneBlock()
    {
        var image = new byte[1024];
        image[512] = 0x44;
        var reader = new MemoryBlockReader(image, blockCount: 2, blockSize: 512);
        var destination = new byte[512];

        reader.ReadBlock(1, destination);

        Assert.That(destination[0], Is.EqualTo(0x44));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBlock(2, destination));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBlock(0, new byte[511]));
    }

    /// <summary>
    /// A memory sector reader addresses DOS logical sectors in track order.
    /// </summary>
    [Test]
    public void MemorySectorReader_ReadsDosLogicalSector()
    {
        var image = new byte[18 * 16 * 256];
        image[((17 * 16) + 0) * 256] = 0x03;
        var reader = new MemorySectorReader(image, 18, 16, 256);
        var destination = new byte[256];

        reader.ReadSector(17, 0, destination);

        Assert.That(destination[0], Is.EqualTo(0x03));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadSector(18, 0, destination));
    }

    /// <summary>
    /// <see cref="BlockMediaReader"/> forwards a read to <see cref="IBlockMedia"/>.
    /// </summary>
    [Test]
    public void BlockMediaReader_ForwardsToBlockMedia()
    {
        var payload = new byte[512];
        payload[0] = 0x21;
        using var backend = new RamStorageBackend(payload);
        var media = new BlockImageMedia(backend, blockCount: 1, blockSize: 512);
        var reader = new BlockMediaReader(media);
        var destination = new byte[512];

        reader.ReadBlock(0, destination);

        Assert.That(reader.BlockCount, Is.EqualTo(1));
        Assert.That(destination[0], Is.EqualTo(0x21));
    }

    /// <summary>
    /// DOS logical sector 2 is read from a DOS-ordered image and from a ProDOS-ordered image.
    /// </summary>
    [Test]
    public void SectorImageSectorReader_ReadsDosLogicalSectorsInEitherOrder()
    {
        AssertDosLogicalByte(SectorOrder.Dos33, 0x11, storedAtLogical: 2);
        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, 2);
        var proDosLogical = SectorSkew.PhysicalToLogical(SectorOrder.ProDos, physical);
        AssertDosLogicalByte(SectorOrder.ProDos, 0x22, storedAtLogical: proDosLogical);
    }

    /// <summary>
    /// A source needs at least one reader.
    /// </summary>
    [Test]
    public void FileSystemSource_RequiresAReader()
    {
        var blocks = new MemoryBlockReader(new byte[512], 1, 512);
        var source = new FileSystemSource(blocks, null);

        Assert.That(source.Blocks, Is.SameAs(blocks));
        Assert.That(source.Sectors, Is.Null);
        Assert.Throws<ArgumentException>(() => new FileSystemSource(null, null));
    }

    private static void AssertDosLogicalByte(SectorOrder order, byte marker, int storedAtLogical)
    {
        using var backend = new RamStorageBackend(18 * 16 * 256);
        var image = new SectorImageMedia(backend, new DiskGeometry(18, 16, 256, order));
        var offset = ((1 * 16L) + storedAtLogical) * 256;
        backend.Write(offset, new byte[] { marker });
        var reader = new SectorImageSectorReader(image);
        var destination = new byte[256];

        reader.ReadSector(1, 2, destination);

        Assert.That(destination[0], Is.EqualTo(marker));
    }
}