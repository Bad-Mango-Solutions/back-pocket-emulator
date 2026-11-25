// <copyright file="BootLoadTrackerTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;
using BadMango.Emulator.Storage.Backends;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;
using BadMango.Emulator.Storage.Tests.FileSystems;

using Serilog;

/// <summary>
/// Boot-load progress from decoded block and sector reads on synthetic volumes.
/// </summary>
[TestFixture]
public class BootLoadTrackerTests
{
    private const int Slot = 6;

    /// <summary>
    /// A ProDOS boot reports PRODOS by blocks and bytes. A repeated block is not counted twice.
    /// </summary>
    [Test]
    public void ProdosBlocks_ReportFileProgress()
    {
        var (tracker, blocks) = Prodos();
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, cycle: 40);
        var first = Load(tracker, "PRODOS");

        Assert.That(first.OpenCycle, Is.EqualTo(40));
        Assert.That(first.BytesRead, Is.EqualTo(512));
        Assert.That(first.UnitsRead, Is.EqualTo(1));
        Assert.That(first.Size, Is.EqualTo(1536));
        Assert.That(first.Describe(), Is.EqualTo("PRODOS 1/3 blocks, 512/1536 (33%)"));

        tracker.ObserveBlock(Slot, 0, blocks[0].Block, cycle: 90);
        tracker.ObserveBlock(Slot, 0, blocks[1].Block, cycle: 90);
        tracker.ObserveBlock(Slot, 0, blocks[2].Block, cycle: 90);
        var done = Load(tracker, "PRODOS");

        Assert.That(done.OpenCycle, Is.EqualTo(40));
        Assert.That(done.BytesRead, Is.EqualTo(1536));
        Assert.That(done.UnitsRead, Is.EqualTo(3));
        Assert.That(done.Position, Is.EqualTo(1536));
        Assert.That(done.Describe(), Is.EqualTo("PRODOS 3/3 blocks, 1536/1536 (100%)"));
    }

    /// <summary>
    /// HELLO is tracked on its own, and two physical sectors are one ProDOS block.
    /// </summary>
    [Test]
    public void ProdosSectors_CountOneBlockAndKeepFilesApart()
    {
        var built = ProdosImage();
        var map = VolumeFileMap.TryBuild(built.Reader, sectors: null, Log());
        var hello = Blocks(map, "HELLO", built.Fixture.BlockCount).Single();
        var prodos = Blocks(map, "PRODOS", built.Fixture.BlockCount)[0];
        var tracker = new BootLoadTracker(Log());
        tracker.MountVolume(Slot, 0, built.Reader, sectors: null);
        tracker.ObserveBlock(Slot, 0, hello.Block, 5);

        var track = prodos.Block / 8;
        var within = prodos.Block % 8;
        var lower = SectorSkew.LogicalToPhysical(SectorOrder.ProDos, within * 2);
        var upper = SectorSkew.LogicalToPhysical(SectorOrder.ProDos, (within * 2) + 1);
        tracker.ObserveSector(Slot, 0, track, lower, 11);
        var half = Load(tracker, "PRODOS");
        tracker.ObserveSector(Slot, 0, track, upper, 12);
        var whole = Load(tracker, "PRODOS");

        Assert.That(Load(tracker, "HELLO").BytesRead, Is.EqualTo(4));
        Assert.That(Load(tracker, "HELLO").OpenCycle, Is.EqualTo(5));
        Assert.That(half.UnitsRead, Is.EqualTo(1));
        Assert.That(half.BytesRead, Is.EqualTo(256));
        Assert.That(whole.UnitsRead, Is.EqualTo(1));
        Assert.That(whole.BytesRead, Is.EqualTo(512));
        Assert.That(whole.OpenCycle, Is.EqualTo(11));
        Assert.That(tracker.Loads, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// DOS tracks 0–2 report boot-image sector progress. A catalog file reports its own sectors.
    /// </summary>
    [Test]
    public void DosBoot_ReportsBootImageAndFileProgress()
    {
        var (tracker, data) = Dos();
        tracker.ObserveSector(Slot, 0, 0, 0, 1);
        tracker.ObserveSector(Slot, 0, 0, 1, 2);
        tracker.ObserveSector(Slot, 0, 1, 4, 3);
        tracker.ObserveSector(Slot, 0, 0, 0, 4);
        var boot = Load(tracker, TrackedLoadProgress.BootImagePath);

        Assert.That(boot.Describe(), Is.EqualTo("boot image sectors read 3/48"));
        Assert.That(boot.BytesRead, Is.EqualTo(3 * Dos33Layout.SectorSize));
        Assert.That(boot.Size, Is.EqualTo(48 * Dos33Layout.SectorSize));

        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 8);
        var hello = Load(tracker, "HELLO");

        Assert.That(hello.Describe(), Is.EqualTo("HELLO 1/1 sectors, 256/256 (100%)"));
        Assert.That(hello.OpenCycle, Is.EqualTo(8));
        Assert.That(hello.Position, Is.EqualTo(256));
    }

    /// <summary>
    /// Replacing the volume drops the previous file. The next read uses the new map.
    /// </summary>
    [Test]
    public void DiskSwap_InvalidatesTheMap()
    {
        var (tracker, blocks) = Prodos();
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 1);
        Assert.That(Load(tracker, "PRODOS").BytesRead, Is.EqualTo(512));

        var other = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "OTHER")
            .AddSeedling("BASIC", ProdosFileType.Text, 0, new byte[] { 9, 8, 7 }, locked: false)
            .Build();
        var otherReader = other.CreateBlockReader();
        var otherMap = VolumeFileMap.TryBuild(otherReader, sectors: null, Log());
        var basic = Blocks(otherMap, "BASIC", other.BlockCount).Single();
        tracker.MountVolume(Slot, 0, otherReader, sectors: null);

        Assert.That(tracker.Loads, Is.Empty);
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 2);
        tracker.ObserveBlock(Slot, 0, basic.Block, 3);

        Assert.That(tracker.Loads.Select(load => load.Path), Does.Not.Contain("PRODOS"));
        Assert.That(Load(tracker, "BASIC").BytesRead, Is.EqualTo(3));
    }

    /// <summary>
    /// A write drops the cached map. Bytes already counted are not added again.
    /// </summary>
    [Test]
    public void Write_DropsTheMapWithoutDoubleCounting()
    {
        var built = ProdosImage();
        var map = VolumeFileMap.TryBuild(built.Reader, sectors: null, Log());
        var block = Blocks(map, "PRODOS", built.Fixture.BlockCount)[0].Block;
        var counting = new CountingBlocks(built.Reader);
        var tracker = new BootLoadTracker(Log());
        tracker.MountVolume(Slot, 0, counting, sectors: null);
        tracker.ObserveBlock(Slot, 0, block, 1);
        var afterBuild = counting.Reads;

        ((IDecodedDiskRead)tracker).OnVolumeWritten(Slot, 0);
        tracker.ObserveBlock(Slot, 0, block, 2);

        Assert.That(counting.Reads, Is.GreaterThan(afterBuild));
        Assert.That(Load(tracker, "PRODOS").BytesRead, Is.EqualTo(512));
        Assert.That(Load(tracker, "PRODOS").UnitsRead, Is.EqualTo(1));
    }

    /// <summary>
    /// The guest OS session end clears progress. The mounted volume is read again on the next boot.
    /// </summary>
    [Test]
    public void SessionEnd_ClearsProgress()
    {
        var (tracker, blocks) = Prodos();
        var monitor = new SessionMonitor();
        tracker.Watch(monitor);
        tracker.ObserveBlock(Slot, 0, 0, 1);
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 2);
        monitor.End();

        Assert.That(tracker.Loads, Is.Empty);
        Assert.That(tracker.UnknownReadCount, Is.Zero);

        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 9);
        Assert.That(Load(tracker, "PRODOS").BytesRead, Is.EqualTo(512));
        Assert.That(Load(tracker, "PRODOS").OpenCycle, Is.EqualTo(9));
    }

    /// <summary>
    /// Copy protection, a missing volume, and a reader that throws are reported as unknown.
    /// </summary>
    [Test]
    public void UnmappableRead_IsUnknownAndDoesNotThrow()
    {
        var tracker = new BootLoadTracker(Log());
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.ObserveSector(Slot, 0, 0, 0, 1);
        tracker.MountVolume(Slot, 0, new MemoryBlockReader(new byte[8 * 512], 8, 512), sectors: null);
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.MountVolume(Slot, 0, new ThrowingBlocks(), sectors: null);
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.ObserveBlock(Slot, 0, -1, 1);
        tracker.ObserveSector(Slot, 0, -1, 99, 1);

        Assert.That(tracker.UnknownReadCount, Is.GreaterThanOrEqualTo(3));
        Assert.That(tracker.Loads, Is.Empty);
    }

    /// <summary>
    /// A Disk II sector image is mapped from its decoded readers, and eject drops that map.
    /// </summary>
    [Test]
    public void MountedDisk_UsesDecodedReadersAndEjectDropsTheMap()
    {
        var (_, data, image) = DosImage();
        var media = new SectorImageMedia(new RamStorageBackend(image.Image.ToArray()), DiskGeometry.Standard525Dos);
        var controller = new DiskIIController(Log())
        {
            SlotNumber = Slot,
        };
        var tracker = new BootLoadTracker(Log());
        tracker.Attach(controller);
        controller.Mount(0, media.As525Media());
        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 4);

        Assert.That(Load(tracker, "HELLO").BytesRead, Is.EqualTo(256));

        Assert.That(controller.Eject(0), Is.True);
        Assert.That(tracker.Loads, Is.Empty);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 5);
        Assert.That(tracker.UnknownReadCount, Is.EqualTo(1));
        Assert.That(tracker.Loads, Is.Empty);
    }

    private static (BootLoadTracker Tracker, List<Located> Blocks) Prodos()
    {
        var built = ProdosImage();
        var map = VolumeFileMap.TryBuild(built.Reader, sectors: null, Log());
        var blocks = Blocks(map, "PRODOS", built.Fixture.BlockCount);
        var tracker = new BootLoadTracker(Log());
        tracker.MountVolume(Slot, 0, built.Reader, sectors: null);
        return (tracker, blocks);
    }

    private static (ProdosFixture Fixture, IBlockReader Reader) ProdosImage()
    {
        var chunk = new byte[ProdosLayout.BlockSize];
        var fixture = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "BOOT")
            .AddSapling("PRODOS", ProdosFileType.Sys, 0, new byte[]?[] { chunk, chunk, chunk }, locked: false)
            .AddSeedling("HELLO", ProdosFileType.Text, 0, new byte[] { 1, 2, 3, 4 }, locked: false)
            .Build();
        return (fixture, fixture.CreateBlockReader());
    }

    private static (BootLoadTracker Tracker, Dos33Fixture.SectorRef Data) Dos()
    {
        var (tracker, data, _) = DosImage();
        return (tracker, data);
    }

    private static (BootLoadTracker Tracker, Dos33Fixture.SectorRef Data, Dos33Fixture Image) DosImage()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var builder = new Dos33ImageBuilder(35, 254);
        for (var i = 0; i < 16; i++)
        {
            builder.AddFile($"F{i:00}", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body });
        }

        var image = builder.AddFile("HELLO", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body }).Build();
        var data = image.Entries.Single(entry => entry.Name == "HELLO").DataSectors.Single(sector => sector.Allocated);
        var tracker = new BootLoadTracker(Log());
        tracker.MountVolume(Slot, 0, blocks: null, new MemorySectorReader(image.Image, image.TrackCount, 16, 256));
        return (tracker, data, image);
    }

    private static List<Located> Blocks(VolumeFileMap map, string path, int blockCount)
    {
        var found = new List<Located>();
        for (var block = 0; block < blockCount; block++)
        {
            if (map.TryLocateBlock(block, out var location) && location.Path == path)
            {
                found.Add(new Located(block, location.Offset));
            }
        }

        found.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        return found;
    }

    private static TrackedLoadProgress Load(BootLoadTracker tracker, string path) =>
        tracker.Loads.Single(load => load.Path == path);

    private static ILogger Log() => Serilog.Core.Logger.None;

    private readonly record struct Located(int Block, long Offset);

    private sealed class CountingBlocks : IBlockReader
    {
        private readonly IBlockReader inner;

        public CountingBlocks(IBlockReader inner) => this.inner = inner;

        public int Reads { get; private set; }

        public int BlockSize => this.inner.BlockSize;

        public int BlockCount => this.inner.BlockCount;

        public void ReadBlock(int blockIndex, Span<byte> destination)
        {
            this.Reads++;
            this.inner.ReadBlock(blockIndex, destination);
        }
    }

    private sealed class ThrowingBlocks : IBlockReader
    {
        public int BlockSize => 512;

        public int BlockCount => 8;

        public void ReadBlock(int blockIndex, Span<byte> destination) => throw new IOException("unreadable");
    }

    private sealed class SessionMonitor : IBootSequenceMonitor
    {
        private EventHandler<GuestOsBootStageChangedEventArgs>? stageChanged;

        private EventHandler<BootMilestone>? milestoneReached;

        private EventHandler<GuestOsSessionEventArgs>? started;

        private EventHandler<GuestOsSessionEventArgs>? ended;

        public event EventHandler<GuestOsBootStageChangedEventArgs>? StageChanged
        {
            add => this.stageChanged += value;
            remove => this.stageChanged -= value;
        }

        public event EventHandler<BootMilestone>? MilestoneReached
        {
            add => this.milestoneReached += value;
            remove => this.milestoneReached -= value;
        }

        public event EventHandler<GuestOsSessionEventArgs>? GuestOsSessionStarted
        {
            add => this.started += value;
            remove => this.started -= value;
        }

        public event EventHandler<GuestOsSessionEventArgs>? GuestOsSessionEnded
        {
            add => this.ended += value;
            remove => this.ended -= value;
        }

        public GuestOsBootStage Stage => GuestOsBootStage.Init;

        public IReadOnlyList<BootMilestone> Milestones => [];

        public void End() => this.ended?.Invoke(this, new GuestOsSessionEventArgs(GuestOsId.ProDos8, GuestOsBootStage.Init));
    }
}