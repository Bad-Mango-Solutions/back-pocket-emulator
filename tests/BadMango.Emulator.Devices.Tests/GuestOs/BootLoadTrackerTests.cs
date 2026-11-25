// <copyright file="BootLoadTrackerTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.GuestOs;
using BadMango.Emulator.Storage.Backends;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;
using BadMango.Emulator.Storage.Tests.FileSystems;

using Moq;
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
        var (tracker, files, blocks) = Prodos();
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, cycle: 40);
        var first = Load(files, "PRODOS");

        Assert.That(first.OpenedAtCycle, Is.EqualTo(40));
        Assert.That(first.BytesRead, Is.EqualTo(512));
        Assert.That(first.Size, Is.EqualTo(1536));
        Assert.That(first.Active, Is.True);
        Assert.That(first.RefNum, Is.Null);
        Assert.That(first.FormatLine(), Is.EqualTo("PRODOS 512/1536 (33%)"));

        tracker.ObserveBlock(Slot, 0, blocks[0].Block, cycle: 90);
        tracker.ObserveBlock(Slot, 0, blocks[1].Block, cycle: 90);
        tracker.ObserveBlock(Slot, 0, blocks[2].Block, cycle: 90);
        var done = Load(files, "PRODOS");

        Assert.That(done.OpenedAtCycle, Is.EqualTo(40));
        Assert.That(done.BytesRead, Is.EqualTo(1536));
        Assert.That(done.Position, Is.EqualTo(1536));
        Assert.That(done.Active, Is.False);
        Assert.That(done.FormatLine(), Is.EqualTo("PRODOS 1536/1536 (100%)"));
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
        var (tracker, files) = Pair();
        tracker.MountVolume(Slot, 0, built.Reader, sectors: null);
        tracker.ObserveBlock(Slot, 0, hello.Block, 5);

        var track = prodos.Block / 8;
        var within = prodos.Block % 8;
        var lower = SectorSkew.LogicalToPhysical(SectorOrder.ProDos, within * 2);
        var upper = SectorSkew.LogicalToPhysical(SectorOrder.ProDos, (within * 2) + 1);
        tracker.ObserveSector(Slot, 0, track, lower, 11);
        var half = Load(files, "PRODOS");
        tracker.ObserveSector(Slot, 0, track, upper, 12);
        var whole = Load(files, "PRODOS");

        Assert.That(Load(files, "HELLO").BytesRead, Is.EqualTo(4));
        Assert.That(Load(files, "HELLO").OpenedAtCycle, Is.EqualTo(5));
        Assert.That(Load(files, "HELLO").FormatLine(), Is.EqualTo("HELLO 4/4 (100%)"));
        Assert.That(half.BytesRead, Is.EqualTo(256));
        Assert.That(whole.BytesRead, Is.EqualTo(512));
        Assert.That(whole.OpenedAtCycle, Is.EqualTo(11));
        Assert.That(whole.FormatLine(), Is.EqualTo("PRODOS 512/1536 (33%)"));
        Assert.That(files.Files, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// An MLI row and a pre-MLI block read share one tracker and one line format.
    /// </summary>
    [Test]
    public void MliAndBlockReads_ShareOneTimeline()
    {
        var (tracker, files, blocks) = Prodos();
        files.Report(new FileLoadProgress("/DISK/BASIC.SYSTEM", 10240, 6656, 6656, 1, 1, true));
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 40);

        Assert.That(files.Files.Select(row => row.FormatLine()).ToArray(), Is.EqualTo(new[]
        {
            "BASIC.SYSTEM 6656/10240 (65%)",
            "PRODOS 512/1536 (33%)",
        }));
        Assert.That(files.Current!.Value.Path, Is.EqualTo("PRODOS"));
    }

    /// <summary>
    /// DOS tracks 0–2 report boot-image sector progress. A catalog file reports its own sectors.
    /// </summary>
    [Test]
    public void DosBoot_ReportsBootImageAndFileProgress()
    {
        var (tracker, files, data) = Dos();
        tracker.ObserveSector(Slot, 0, 0, 0, 1);
        tracker.ObserveSector(Slot, 0, 0, 1, 2);
        tracker.ObserveSector(Slot, 0, 1, 4, 3);
        tracker.ObserveSector(Slot, 0, 0, 0, 4);
        var boot = Load(files, BootLoadTracker.BootImagePath);

        Assert.That(boot.FormatLine(), Is.EqualTo("boot image 768/12288 (6%)"));
        Assert.That(boot.BytesRead, Is.EqualTo(3 * Dos33Layout.SectorSize));
        Assert.That(boot.Size, Is.EqualTo(48 * Dos33Layout.SectorSize));
        Assert.That(boot.OpenedAtCycle, Is.EqualTo(1));

        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 8);
        var hello = Load(files, "HELLO");

        Assert.That(hello.FormatLine(), Is.EqualTo("HELLO 256/256 (100%)"));
        Assert.That(hello.OpenedAtCycle, Is.EqualTo(8));
        Assert.That(hello.Position, Is.EqualTo(256));
    }

    /// <summary>
    /// Replacing the volume drops the previous file. The next read uses the new map.
    /// </summary>
    [Test]
    public void DiskSwap_InvalidatesTheMap()
    {
        var (tracker, files, blocks) = Prodos();
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 1);
        Assert.That(Load(files, "PRODOS").BytesRead, Is.EqualTo(512));

        var other = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "OTHER")
            .AddSeedling("BASIC", ProdosFileType.Text, 0, new byte[] { 9, 8, 7 }, locked: false)
            .Build();
        var otherReader = other.CreateBlockReader();
        var otherMap = VolumeFileMap.TryBuild(otherReader, sectors: null, Log());
        var basic = Blocks(otherMap, "BASIC", other.BlockCount).Single();
        tracker.MountVolume(Slot, 0, otherReader, sectors: null);

        Assert.That(files.Files, Is.Empty);
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 2);
        tracker.ObserveBlock(Slot, 0, basic.Block, 3);

        Assert.That(files.Files.Select(row => row.Path), Does.Not.Contain("PRODOS"));
        Assert.That(Load(files, "BASIC").FormatLine(), Is.EqualTo("BASIC 3/3 (100%)"));
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
        var (tracker, files) = Pair();
        tracker.MountVolume(Slot, 0, counting, sectors: null);
        tracker.ObserveBlock(Slot, 0, block, 1);
        var afterBuild = counting.Reads;

        ((IDecodedDiskRead)tracker).OnVolumeWritten(Slot, 0);
        tracker.ObserveBlock(Slot, 0, block, 2);

        Assert.That(counting.Reads, Is.GreaterThan(afterBuild));
        Assert.That(Load(files, "PRODOS").BytesRead, Is.EqualTo(512));
        Assert.That(Load(files, "PRODOS").FormatLine(), Is.EqualTo("PRODOS 512/1536 (33%)"));
    }

    /// <summary>
    /// The guest OS session end clears progress. The mounted volume is read again on the next boot.
    /// </summary>
    [Test]
    public void SessionEnd_ClearsProgress()
    {
        var (tracker, files, blocks) = Prodos();
        var monitor = new SessionMonitor();
        tracker.Watch(monitor);
        tracker.ObserveBlock(Slot, 0, 0, 1);
        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 2);
        monitor.End();

        Assert.That(files.Files, Is.Empty);
        Assert.That(files.Current, Is.Null);

        tracker.ObserveBlock(Slot, 0, blocks[0].Block, 9);
        Assert.That(Load(files, "PRODOS").BytesRead, Is.EqualTo(512));
        Assert.That(Load(files, "PRODOS").OpenedAtCycle, Is.EqualTo(9));
    }

    /// <summary>
    /// Copy protection, a missing volume, and a reader that throws are reported as unknown.
    /// </summary>
    [Test]
    public void UnmappableRead_IsUnknownAndDoesNotThrow()
    {
        var (tracker, files) = Pair();
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.ObserveSector(Slot, 0, 0, 0, 1);
        tracker.MountVolume(Slot, 0, new MemoryBlockReader(new byte[8 * 512], 8, 512), sectors: null);
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.MountVolume(Slot, 0, new ThrowingBlocks(), sectors: null);
        tracker.ObserveBlock(Slot, 0, 2, 1);
        tracker.ObserveBlock(Slot, 0, -1, 1);
        tracker.ObserveSector(Slot, 0, -1, 99, 1);

        var unknown = files.Files.Single();
        Assert.That(unknown.Path, Is.EqualTo(string.Empty));
        Assert.That(unknown.FormatLine(), Is.EqualTo("unknown 1280"));
        Assert.That(unknown.RefNum, Is.Null);
    }

    /// <summary>
    /// A Disk II sector image is mapped from its decoded readers, and eject drops that map.
    /// </summary>
    [Test]
    public void MountedDisk_UsesDecodedReadersAndEjectDropsTheMap()
    {
        var (_, _, data, image) = DosImage();
        var media = new SectorImageMedia(new RamStorageBackend(image.Image.ToArray()), DiskGeometry.Standard525Dos);
        var controller = new DiskIIController(Log())
        {
            SlotNumber = Slot,
        };
        var (tracker, files) = Pair();
        tracker.Attach(controller);
        controller.Mount(0, media.As525Media());
        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, data.Sector);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 4);

        Assert.That(Load(files, "HELLO").FormatLine(), Is.EqualTo("HELLO 256/256 (100%)"));

        Assert.That(controller.Eject(0), Is.True);
        Assert.That(files.Files, Is.Empty);
        tracker.ObserveSector(Slot, 0, data.Track, physical, 5);
        Assert.That(files.Files.Single().FormatLine(), Is.EqualTo("unknown 256"));
    }

    private static (BootLoadTracker Tracker, IFileLoadTracker Files, List<Located> Blocks) Prodos()
    {
        var built = ProdosImage();
        var map = VolumeFileMap.TryBuild(built.Reader, sectors: null, Log());
        var blocks = Blocks(map, "PRODOS", built.Fixture.BlockCount);
        var (tracker, files) = Pair();
        tracker.MountVolume(Slot, 0, built.Reader, sectors: null);
        return (tracker, files, blocks);
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

    private static (BootLoadTracker Tracker, IFileLoadTracker Files, Dos33Fixture.SectorRef Data) Dos()
    {
        var (tracker, files, data, _) = DosImage();
        return (tracker, files, data);
    }

    private static (BootLoadTracker Tracker, IFileLoadTracker Files, Dos33Fixture.SectorRef Data, Dos33Fixture Image) DosImage()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var builder = new Dos33ImageBuilder(35, 254);
        for (var i = 0; i < 16; i++)
        {
            builder.AddFile($"F{i:00}", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body });
        }

        var image = builder.AddFile("HELLO", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { body }).Build();
        var data = image.Entries.Single(entry => entry.Name == "HELLO").DataSectors.Single(sector => sector.Allocated);
        var (tracker, files) = Pair();
        tracker.MountVolume(Slot, 0, blocks: null, new MemorySectorReader(image.Image, image.TrackCount, 16, 256));
        return (tracker, files, data, image);
    }

    private static (BootLoadTracker Boot, IFileLoadTracker Files) Pair()
    {
        var cpu = new Mock<ICpu>();
        var memory = new CpuGuestMemoryReader(cpu.Object);
        var detector = new GuestOsDetector(Log(), [], memory);
        var monitor = new BootSequenceMonitor(Log(), detector, []);
        var shared = new ProDosFileLoadTracker(NullTrapRegistry.Instance, cpu.Object, monitor);
        return (new BootLoadTracker(Log(), shared), shared);
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

    private static FileLoadProgress Load(IFileLoadTracker files, string path) =>
        files.Files.Single(row => row.Path == path);

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