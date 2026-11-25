// <copyright file="BootLoadTracker.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Media;

using Serilog;

/// <summary>
/// Maps decoded block and sector reads onto files while a guest OS is loading.
/// </summary>
/// <remarks>
/// <para>
/// The map is built once per mounted volume, on the first read, from
/// <see cref="VolumeFileMap"/>. A disk swap or eject drops it. A write drops the
/// map and keeps bytes already counted. The next read builds it again.
/// An unreadable or copy-protected volume reports <see cref="UnknownReadCount"/>
/// and does not throw.
/// </para>
/// <para>
/// <see cref="IBootSequenceMonitor.GuestOsSessionEnded"/> clears progress. Nothing is attached, and the
/// Disk II read path does not call in, when this tracker is not installed.
/// </para>
/// </remarks>
public sealed class BootLoadTracker : IDecodedDiskRead
{
    private readonly ILogger logger;

    private readonly Dictionary<int, VolumeLoad> volumes = [];

    private readonly Dictionary<int, VolumeSource> mounted = [];

    private readonly Dictionary<int, DiskIIController> controllers = [];

    private IBootSequenceMonitor? session;

    private bool building;

    /// <summary>
    /// Initializes a new instance of the <see cref="BootLoadTracker"/> class.
    /// </summary>
    /// <param name="logger">Logger for a volume whose map could not be built. Not used per sector.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public BootLoadTracker(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger.ForContext<BootLoadTracker>();
    }

    /// <summary>Gets a snapshot of file and boot-image progress, oldest first.</summary>
    /// <value>The loads recorded since the last clear or disk swap.</value>
    public IReadOnlyList<TrackedLoadProgress> Loads => this.Snapshot();

    /// <summary>Gets how many decoded reads did not belong to a mapped file or the boot image.</summary>
    /// <value>The count since the last clear or disk swap.</value>
    public int UnknownReadCount
    {
        get
        {
            var count = 0;
            foreach (var volume in this.volumes.Values)
            {
                count += volume.Unknown;
            }

            return count;
        }
    }

    /// <summary>
    /// Installs one tracker on the machine, watches the boot session, and attaches Disk II cards.
    /// </summary>
    /// <param name="machine">The machine that receives the tracker.</param>
    /// <param name="logger">Logger used when a tracker is created by this call.</param>
    /// <param name="monitor">The session whose end clears load progress.</param>
    /// <returns>The tracker stored on the machine.</returns>
    /// <exception cref="ArgumentNullException">If any argument is <see langword="null"/>.</exception>
    public static BootLoadTracker Install(IMachine machine, ILogger logger, IBootSequenceMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(monitor);
        var tracker = machine.GetComponent<BootLoadTracker>();
        if (tracker is null)
        {
            tracker = new BootLoadTracker(logger);
            machine.AddComponent(tracker);
        }

        tracker.Watch(monitor);
        if (machine.GetComponent<ISlotManager>() is { } slots)
        {
            foreach (var card in slots.Slots.Values)
            {
                if (card is DiskIIController disk)
                {
                    tracker.Attach(disk);
                }
            }
        }

        return tracker;
    }

    /// <summary>
    /// Subscribes to <see cref="IBootSequenceMonitor.GuestOsSessionEnded"/>.
    /// </summary>
    /// <param name="monitor">The boot monitor. Not <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="monitor"/> is <see langword="null"/>.</exception>
    public void Watch(IBootSequenceMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (ReferenceEquals(this.session, monitor))
        {
            return;
        }

        if (this.session is not null)
        {
            this.session.GuestOsSessionEnded -= this.OnSessionEnded;
        }

        this.session = monitor;
        monitor.GuestOsSessionEnded += this.OnSessionEnded;
    }

    /// <summary>
    /// Sends decoded Disk II sectors from <paramref name="controller"/> to this tracker.
    /// </summary>
    /// <param name="controller">The controller. Not <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="controller"/> is <see langword="null"/>.</exception>
    public void Attach(DiskIIController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.SetDecodedReadObserver(this);
        this.controllers[controller.SlotNumber] = controller;
    }

    /// <summary>
    /// Supplies the readers for one drive and drops any progress already counted there.
    /// </summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="drive">Zero-based drive index.</param>
    /// <param name="blocks">ProDOS blocks, or <see langword="null"/>.</param>
    /// <param name="sectors">DOS logical sectors, or <see langword="null"/>.</param>
    public void MountVolume(int slot, int drive, IBlockReader? blocks, ISectorReader? sectors)
    {
        this.mounted[Key(slot, drive)] = new VolumeSource(blocks, sectors);
        this.Volume(slot, drive).ClearProgress();
    }

    /// <summary>
    /// Records one decoded block. An unmapped block increments <see cref="UnknownReadCount"/>.
    /// </summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="drive">Zero-based drive index.</param>
    /// <param name="block">Zero-based block number.</param>
    /// <param name="cycle">Cycle of the read. Stored on the first byte of the file.</param>
    public void ObserveBlock(int slot, int drive, int block, long cycle = 0)
    {
        try
        {
            this.NoteBlock(slot, drive, block, cycle);
        }
        catch (Exception ex) when (IsReaderFailure(ex))
        {
            this.Volume(slot, drive).Unknown++;
            this.logger.Debug(ex, "Block read was not mapped.");
        }
    }

    /// <summary>
    /// Records one decoded Disk II sector. <paramref name="physicalSector"/> is the address field.
    /// </summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="drive">Zero-based drive index.</param>
    /// <param name="track">Track from the address field.</param>
    /// <param name="physicalSector">Physical sector from the address field.</param>
    /// <param name="cycle">Cycle of the read. Stored on the first byte of the file.</param>
    public void ObserveSector(int slot, int drive, int track, int physicalSector, long cycle = 0)
    {
        try
        {
            this.NoteSector(slot, drive, track, physicalSector, cycle);
        }
        catch (Exception ex) when (IsReaderFailure(ex))
        {
            this.Volume(slot, drive).Unknown++;
            this.logger.Debug(ex, "Sector read was not mapped.");
        }
    }

    /// <summary>
    /// Clears load progress and cached maps. Mounted readers and Disk II attachments stay.
    /// </summary>
    public void Clear()
    {
        foreach (var volume in this.volumes.Values)
        {
            volume.ClearProgress();
        }
    }

    /// <inheritdoc />
    void IDecodedDiskRead.OnDecodedSector(int slot, int drive, int track, int physicalSector) =>
        this.ObserveSector(slot, drive, track, physicalSector, 0);

    /// <inheritdoc />
    void IDecodedDiskRead.OnDecodedBlock(int slot, int drive, int block) =>
        this.ObserveBlock(slot, drive, block, 0);

    /// <inheritdoc />
    void IDecodedDiskRead.OnVolumeChanged(int slot, int drive)
    {
        this.mounted.Remove(Key(slot, drive));
        if (this.volumes.TryGetValue(Key(slot, drive), out var volume))
        {
            volume.ClearProgress();
        }
    }

    /// <inheritdoc />
    void IDecodedDiskRead.OnVolumeWritten(int slot, int drive)
    {
        if (this.volumes.TryGetValue(Key(slot, drive), out var volume))
        {
            volume.DropMap();
        }
    }

    private static int Key(int slot, int drive) => (slot << 8) | (drive & 0xFF);

    private static bool IsReaderFailure(Exception ex) =>
        ex is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or ObjectDisposedException;

    private void OnSessionEnded(object? sender, GuestOsSessionEventArgs args) => this.Clear();

    private void NoteBlock(int slot, int drive, int block, long cycle)
    {
        var volume = this.Volume(slot, drive);
        var map = this.Ensure(volume, slot, drive);
        if (!map.TryLocateBlock(block, out var located))
        {
            volume.Unknown++;
            return;
        }

        this.NoteWholeBlock(volume, map, located, block, cycle);
    }

    private void NoteSector(int slot, int drive, int track, int physicalSector, long cycle)
    {
        var volume = this.Volume(slot, drive);
        var map = this.Ensure(volume, slot, drive);
        if (!map.Recognized)
        {
            volume.Unknown++;
            return;
        }

        if (map.IsBootImageSector(track, physicalSector))
        {
            this.NoteBoot(volume, map, track, physicalSector, cycle);
            return;
        }

        if (!map.TryLocatePhysicalSector(track, physicalSector, out var located, out var block, out var half))
        {
            volume.Unknown++;
            return;
        }

        if (map.Kind == AppleFileSystemKind.Prodos)
        {
            this.NoteHalf(volume, map, located, block, half, cycle);
            return;
        }

        if (!this.MarkSector(volume, map, track, physicalSector))
        {
            this.Touch(volume, located, located.Offset + located.Length);
            return;
        }

        this.Account(volume, map, located, 1, located.Length, located.Offset + located.Length, cycle);
    }

    private void NoteBoot(VolumeLoad volume, VolumeFileMap map, int track, int physicalSector, long cycle)
    {
        if (!this.MarkSector(volume, map, track, physicalSector))
        {
            return;
        }

        var load = this.Boot(volume, map, cycle);
        load.BytesRead += Dos33Layout.SectorSize;
        load.UnitsRead++;
        load.Position = load.BytesRead;
    }

    private void NoteWholeBlock(VolumeLoad volume, VolumeFileMap map, FileUnitLocation located, int block, long cycle)
    {
        this.EnsureHalves(volume, map.BlockCount);
        var len0 = Math.Min(ProdosLayout.BlockSize / 2, located.Length);
        var len1 = Math.Max(0, located.Length - (ProdosLayout.BlockSize / 2));
        var before = this.HalfSet(volume, block, 0) || this.HalfSet(volume, block, 1);
        var bytes = 0;
        if (len0 > 0 && this.MarkHalf(volume, block, 0))
        {
            bytes += len0;
        }

        if (len1 > 0 && this.MarkHalf(volume, block, 1))
        {
            bytes += len1;
        }

        var units = !before && bytes > 0 ? 1 : 0;
        this.Account(volume, map, located, units, bytes, located.Offset + located.Length, cycle);
    }

    private void NoteHalf(VolumeLoad volume, VolumeFileMap map, FileUnitLocation located, int block, int half, long cycle)
    {
        if (block < 0 || half < 0)
        {
            volume.Unknown++;
            return;
        }

        this.EnsureHalves(volume, map.BlockCount);
        var before = this.HalfSet(volume, block, 0) || this.HalfSet(volume, block, 1);
        if (!this.MarkHalf(volume, block, half))
        {
            this.Touch(volume, located, located.Offset + located.Length);
            return;
        }

        this.Account(volume, map, located, before ? 0 : 1, located.Length, located.Offset + located.Length, cycle);
    }

    private void Account(
        VolumeLoad volume,
        VolumeFileMap map,
        FileUnitLocation located,
        int units,
        int bytes,
        long position,
        long cycle)
    {
        if (located.Path is null)
        {
            volume.Unknown++;
            return;
        }

        if (!volume.ByPath.TryGetValue(located.Path, out var load))
        {
            if (bytes == 0 && units == 0)
            {
                return;
            }

            var count = map.TryGetFile(located.Path, out var size) ? size.Units : (int?)null;
            var label = map.Kind == AppleFileSystemKind.Prodos ? "blocks" : "sectors";
            load = new MutableLoad(located.Path, located.FileLength, cycle, label, count);
            volume.ByPath.Add(located.Path, load);
            volume.Order.Add(load);
        }

        load.BytesRead += bytes;
        load.UnitsRead += units;
        load.Position = position;
    }

    private void Touch(VolumeLoad volume, FileUnitLocation located, long position)
    {
        if (located.Path is not null && volume.ByPath.TryGetValue(located.Path, out var load))
        {
            load.Position = position;
        }
    }

    private MutableLoad Boot(VolumeLoad volume, VolumeFileMap map, long cycle)
    {
        if (volume.ByPath.TryGetValue(TrackedLoadProgress.BootImagePath, out var existing))
        {
            return existing;
        }

        var bytes = (long)map.BootImageSectorCount * Dos33Layout.SectorSize;
        var created = new MutableLoad(
            TrackedLoadProgress.BootImagePath,
            bytes,
            cycle,
            "sectors",
            map.BootImageSectorCount);
        volume.ByPath.Add(created.Path, created);
        volume.Order.Add(created);
        return created;
    }

    private VolumeFileMap Ensure(VolumeLoad volume, int slot, int drive)
    {
        if (volume.Ready)
        {
            return volume.Map;
        }

        if (this.building)
        {
            return VolumeFileMap.Unrecognized;
        }

        this.building = true;
        try
        {
            volume.Map = this.Build(slot, drive);
            volume.Ready = true;
            return volume.Map;
        }
        finally
        {
            this.building = false;
        }
    }

    private VolumeFileMap Build(int slot, int drive)
    {
        if (this.mounted.TryGetValue(Key(slot, drive), out var source))
        {
            return VolumeFileMap.TryBuild(source.Blocks, source.Sectors, this.logger);
        }

        if (!this.controllers.TryGetValue(slot, out var controller))
        {
            return VolumeFileMap.Unrecognized;
        }

        I525Media? media;
        try
        {
            media = controller.GetMedia(drive);
        }
        catch (Exception ex) when (IsReaderFailure(ex))
        {
            this.logger.Debug(ex, "Mounted drive {Slot}:{Drive} has no readable medium.", slot, drive + 1);
            return VolumeFileMap.Unrecognized;
        }

        if (media is not IVolumeReaders readers)
        {
            return VolumeFileMap.Unrecognized;
        }

        try
        {
            return VolumeFileMap.TryBuild(readers.Blocks, readers.Sectors, this.logger);
        }
        catch (Exception ex) when (IsReaderFailure(ex))
        {
            this.logger.Debug(ex, "Mounted volume map was not built.");
            return VolumeFileMap.Unrecognized;
        }
    }

    private VolumeLoad Volume(int slot, int drive)
    {
        var key = Key(slot, drive);
        if (this.volumes.TryGetValue(key, out var volume))
        {
            return volume;
        }

        volume = new VolumeLoad();
        this.volumes.Add(key, volume);
        return volume;
    }

    private List<TrackedLoadProgress> Snapshot()
    {
        var list = new List<TrackedLoadProgress>();
        foreach (var volume in this.volumes.Values)
        {
            foreach (var load in volume.Order)
            {
                list.Add(load.Snapshot());
            }
        }

        return list;
    }

    private void EnsureHalves(VolumeLoad volume, int blockCount)
    {
        var bytes = (blockCount * 2) + 7;
        bytes >>= 3;
        if (bytes <= 0 || (volume.Halves is not null && volume.Halves.Length >= bytes))
        {
            return;
        }

        var next = new byte[bytes];
        if (volume.Halves is not null)
        {
            volume.Halves.AsSpan().CopyTo(next);
        }

        volume.Halves = next;
    }

    private bool MarkHalf(VolumeLoad volume, int block, int half)
    {
        if (volume.Halves is null)
        {
            return false;
        }

        var index = (block * 2) + half;
        var at = index >> 3;
        if ((uint)at >= (uint)volume.Halves.Length)
        {
            return false;
        }

        var mask = (byte)(1 << (index & 7));
        if ((volume.Halves[at] & mask) != 0)
        {
            return false;
        }

        volume.Halves[at] |= mask;
        return true;
    }

    private bool HalfSet(VolumeLoad volume, int block, int half)
    {
        if (volume.Halves is null)
        {
            return false;
        }

        var index = (block * 2) + half;
        var at = index >> 3;
        if ((uint)at >= (uint)volume.Halves.Length)
        {
            return false;
        }

        var mask = (byte)(1 << (index & 7));
        return (volume.Halves[at] & mask) != 0;
    }

    private bool MarkSector(VolumeLoad volume, VolumeFileMap map, int track, int physicalSector)
    {
        if ((uint)track >= (uint)map.TrackCount || (uint)physicalSector >= (uint)VolumeFileMap.DosSectorsPerTrack)
        {
            return false;
        }

        if (volume.Sectors is null || volume.Sectors.Length < map.TrackCount)
        {
            var next = new ushort[map.TrackCount];
            if (volume.Sectors is not null)
            {
                volume.Sectors.AsSpan().CopyTo(next);
            }

            volume.Sectors = next;
        }

        var mask = (ushort)(1 << physicalSector);
        if ((volume.Sectors[track] & mask) != 0)
        {
            return false;
        }

        volume.Sectors[track] |= mask;
        return true;
    }

    private readonly record struct VolumeSource(IBlockReader? Blocks, ISectorReader? Sectors);

    private sealed class VolumeLoad
    {
        public VolumeFileMap Map { get; set; } = VolumeFileMap.Unrecognized;

        public bool Ready { get; set; }

        public byte[]? Halves { get; set; }

        public ushort[]? Sectors { get; set; }

        public Dictionary<string, MutableLoad> ByPath { get; } = [];

        public List<MutableLoad> Order { get; } = [];

        public int Unknown { get; set; }

        public void ClearProgress()
        {
            this.ByPath.Clear();
            this.Order.Clear();
            this.Unknown = 0;
            this.Halves = null;
            this.Sectors = null;
            this.Ready = false;
            this.Map = VolumeFileMap.Unrecognized;
        }

        public void DropMap()
        {
            this.Ready = false;
            this.Map = VolumeFileMap.Unrecognized;
        }
    }

    private sealed class MutableLoad
    {
        public MutableLoad(string path, long fileLength, long openCycle, string unitLabel, int? unitCount)
        {
            this.Path = path;
            this.Size = fileLength;
            this.OpenCycle = openCycle;
            this.UnitLabel = unitLabel;
            this.UnitCount = unitCount;
        }

        public string Path { get; }

        public long? Size { get; }

        public long BytesRead { get; set; }

        public long Position { get; set; }

        public long OpenCycle { get; }

        public int UnitsRead { get; set; }

        public int? UnitCount { get; }

        public string UnitLabel { get; }

        public TrackedLoadProgress Snapshot() => new(this.Path, this.Size, this.BytesRead, this.Position, this.OpenCycle)
        {
            UnitsRead = this.UnitsRead,
            UnitCount = this.UnitCount,
            UnitLabel = this.UnitLabel,
        };
    }
}