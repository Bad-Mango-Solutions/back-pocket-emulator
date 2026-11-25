// <copyright file="VolumeFileMap.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;

using Serilog;

/// <summary>
/// Block-to-file and track/sector-to-file map for one mounted volume.
/// </summary>
/// <remarks>
/// <para>
/// Built from <see cref="ProdosFileSystem"/> or <see cref="Dos33FileSystem"/>. A ProDOS
/// map records data blocks only: directory blocks, bitmap blocks, and sapling or tree
/// index blocks are not file bytes. A DOS 3.3 map records data sectors from each
/// track/sector list. Tracks 0–2 are the boot image, not catalog files.
/// </para>
/// <para>
/// An unrecognized or unreadable volume is an empty map. Lookups return false and do
/// not throw. The static empty map is never mutated.
/// </para>
/// </remarks>
public sealed class VolumeFileMap
{
    /// <summary>Tracks DOS loads as the boot image before any catalog file.</summary>
    public const int DosBootTrackCount = 3;

    /// <summary>Sectors on one DOS 3.3 track.</summary>
    public const int DosSectorsPerTrack = 16;

    private static readonly Dictionary<int, FileUnitLocation> EmptyUnits = [];

    private static readonly Dictionary<string, FileLoadSize> EmptySizes = [];

    private readonly Dictionary<int, FileUnitLocation> units;

    private readonly Dictionary<string, FileLoadSize> sizes;

    private VolumeFileMap(
        AppleFileSystemKind? kind,
        Dictionary<int, FileUnitLocation> units,
        Dictionary<string, FileLoadSize> sizes,
        int blockCount,
        int trackCount)
    {
        this.Kind = kind;
        this.units = units;
        this.sizes = sizes;
        this.BlockCount = blockCount;
        this.TrackCount = trackCount;
    }

    /// <summary>Gets a map that locates nothing.</summary>
    /// <value>The shared empty map. Lookups return false.</value>
    public static VolumeFileMap Unrecognized { get; } = new(null, EmptyUnits, EmptySizes, 0, 0);

    /// <summary>Gets the file system the map was built from.</summary>
    /// <value><see langword="null"/> when the volume is not ProDOS or DOS 3.3.</value>
    public AppleFileSystemKind? Kind { get; }

    /// <summary>Gets whether <see cref="Kind"/> names a file system.</summary>
    /// <value><see langword="true"/> for a ProDOS or DOS 3.3 map, including an empty catalog.</value>
    public bool Recognized => this.Kind is not null;

    /// <summary>Gets the ProDOS block count used to size the map.</summary>
    /// <value>Zero when the map is not ProDOS.</value>
    public int BlockCount { get; }

    /// <summary>Gets the DOS track count used to size the map.</summary>
    /// <value>Zero when the map is not DOS 3.3.</value>
    public int TrackCount { get; }

    /// <summary>Gets how many boot-image sectors a DOS 3.3 map reports.</summary>
    /// <value>48 on DOS 3.3. Zero otherwise.</value>
    public int BootImageSectorCount =>
        this.Kind == AppleFileSystemKind.Dos33 ? DosBootTrackCount * DosSectorsPerTrack : 0;

    /// <summary>
    /// Builds a map from the readers the volume exposes. A failure becomes <see cref="Unrecognized"/>.
    /// </summary>
    /// <param name="blocks">ProDOS blocks, or <see langword="null"/> when the volume has none.</param>
    /// <param name="sectors">DOS logical sectors, or <see langword="null"/> when the volume has none.</param>
    /// <param name="logger">Logger for a volume that is not ProDOS or DOS 3.3. Not used per unit.</param>
    /// <returns>The map. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public static VolumeFileMap TryBuild(IBlockReader? blocks, ISectorReader? sectors, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (blocks is not null)
        {
            try
            {
                var prodos = new ProdosFileSystem(blocks, logger);
                var located = new Dictionary<int, FileUnitLocation>();
                var files = new Dictionary<string, FileLoadSize>();
                prodos.CollectDataBlocks(located, files);
                return new VolumeFileMap(AppleFileSystemKind.Prodos, located, files, blocks.BlockCount, 0);
            }
            catch (Exception ex) when (IsReaderFailure(ex))
            {
                logger.Debug(ex, "ProDOS block map was not built.");
            }
        }

        if (sectors is not null)
        {
            try
            {
                var dos = new Dos33FileSystem(sectors, logger);
                var located = new Dictionary<int, FileUnitLocation>();
                var files = new Dictionary<string, FileLoadSize>();
                dos.CollectDataSectors(located, files);
                return new VolumeFileMap(AppleFileSystemKind.Dos33, located, files, 0, sectors.TrackCount);
            }
            catch (Exception ex) when (IsReaderFailure(ex))
            {
                logger.Debug(ex, "DOS 3.3 sector map was not built.");
            }
        }

        return Unrecognized;
    }

    /// <summary>
    /// Locates a ProDOS data block.
    /// </summary>
    /// <param name="block">Zero-based block number.</param>
    /// <param name="location">The file unit when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the block is not a mapped data block.</returns>
    public bool TryLocateBlock(int block, out FileUnitLocation location)
    {
        location = default;
        if (this.Kind != AppleFileSystemKind.Prodos || block < 0)
        {
            return false;
        }

        return this.units.TryGetValue(block, out location);
    }

    /// <summary>
    /// Locates a decoded Disk II sector. <paramref name="physicalSector"/> is the address-field sector.
    /// </summary>
    /// <param name="track">Track number from the address field.</param>
    /// <param name="physicalSector">Physical sector from the address field, 0–15.</param>
    /// <param name="location">The file unit when the method returns <see langword="true"/>.</param>
    /// <param name="block">The ProDOS block, or -1 for a DOS sector.</param>
    /// <param name="half">0 or 1 for the half of a ProDOS block, or -1 for a DOS sector.</param>
    /// <returns>
    /// <see langword="false"/> for a boot-image sector, an unknown sector, or a sector past end-of-file.
    /// </returns>
    public bool TryLocatePhysicalSector(
        int track,
        int physicalSector,
        out FileUnitLocation location,
        out int block,
        out int half)
    {
        location = default;
        block = -1;
        half = -1;
        if (track < 0 || (uint)physicalSector >= (uint)SectorSkew.SectorsPerTrack || this.IsBootImageSector(track, physicalSector))
        {
            return false;
        }

        if (this.Kind == AppleFileSystemKind.Dos33)
        {
            if ((uint)track >= (uint)this.TrackCount || track > (int.MaxValue >> 8))
            {
                return false;
            }

            var logical = SectorSkew.PhysicalToLogical(SectorOrder.Dos33, physicalSector);
            return this.units.TryGetValue(SectorKey(track, logical), out location);
        }

        if (this.Kind != AppleFileSystemKind.Prodos || this.BlockCount <= 0)
        {
            return false;
        }

        var maxTrack = (this.BlockCount - 1) / 8;
        if (track > maxTrack)
        {
            return false;
        }

        var prodosLogical = SectorSkew.PhysicalToLogical(SectorOrder.ProDos, physicalSector);
        block = (track * 8) + (prodosLogical / 2);
        half = prodosLogical & 1;
        if ((uint)block >= (uint)this.BlockCount || !this.units.TryGetValue(block, out var whole))
        {
            block = -1;
            half = -1;
            return false;
        }

        var start = half * (ProdosLayout.BlockSize / 2);
        if (start >= whole.Length)
        {
            block = -1;
            half = -1;
            return false;
        }

        var length = Math.Min(ProdosLayout.BlockSize / 2, whole.Length - start);
        location = new FileUnitLocation(whole.Path, whole.Offset + start, length, whole.FileLength);
        return true;
    }

    /// <summary>
    /// Returns whether a decoded sector is part of the DOS 3.3 boot image on tracks 0–2.
    /// </summary>
    /// <param name="track">Track number.</param>
    /// <param name="physicalSector">Physical sector, 0–15.</param>
    /// <returns><see langword="true"/> for tracks 0–2 on a DOS 3.3 map.</returns>
    public bool IsBootImageSector(int track, int physicalSector) =>
        this.Kind == AppleFileSystemKind.Dos33
        && (uint)track < (uint)DosBootTrackCount
        && (uint)physicalSector < (uint)DosSectorsPerTrack;

    /// <summary>
    /// Gets the recorded size of one mapped file.
    /// </summary>
    /// <param name="path">Volume-relative path stored in the map.</param>
    /// <param name="size">The size when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the path was not collected.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    public bool TryGetFile(string path, out FileLoadSize size)
    {
        ArgumentNullException.ThrowIfNull(path);
        return this.sizes.TryGetValue(path, out size);
    }

    /// <summary>Packs a DOS logical track and sector into one map key.</summary>
    /// <param name="track">Track number.</param>
    /// <param name="sector">DOS logical sector.</param>
    /// <returns>The key used by the DOS sector dictionary.</returns>
    internal static int SectorKey(int track, int sector) => (track << 8) | sector;

    private static bool IsReaderFailure(Exception ex) =>
        ex is (ArgumentException and not ArgumentNullException)
            or InvalidOperationException
            or IOException
            or ObjectDisposedException;
}