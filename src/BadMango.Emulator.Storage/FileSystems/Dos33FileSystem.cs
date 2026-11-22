// <copyright file="Dos33FileSystem.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using System.Globalization;

using Serilog;

/// <summary>
/// Read-only DOS 3.3 catalog over an <see cref="ISectorReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// The volume name is the VTOC volume number as an invariant decimal. DOS 3.3 has no
/// text volume name. The catalog chain starts at the VTOC's first catalog sector and
/// follows <see cref="Dos33Layout.Catalog.NextTrackOffset"/>. It does not scan the rest
/// of track 17. The type and the lock bit are the byte at
/// <see cref="Dos33Layout.CatalogEntry.TypeOffset"/>. Track
/// <see cref="Dos33Layout.UnusedTrack"/> and track <see cref="Dos33Layout.DeletedTrack"/>
/// are not files.
/// </para>
/// <para>
/// File bytes come from that file's track/sector list, including later list sectors.
/// Pair 0 of a list describes the file sector at
/// <see cref="Dos33Layout.TrackSectorList.SectorBaseOffset"/>. A <c>$00 $00</c> pair
/// before the last occupied pair is 256 zero bytes, not the end of the file. A repeated
/// catalog sector fails <see cref="ListDirectory"/>. A repeated track/sector-list sector
/// fails <see cref="TryReadFile"/> with <see cref="FileAccessStatus.CorruptStructure"/>.
/// The catalog entry is still listed, with an end-of-file of zero.
/// </para>
/// <para>
/// <see cref="FileReadView.Logical"/> removes an A, I, or B header through
/// <see cref="Dos33Layout.LogicalPayloadLength(byte, ReadOnlySpan{byte})"/>. One 256-byte
/// sector buffer is allocated for the life of the reader and reused. Media exceptions
/// are not caught.
/// </para>
/// </remarks>
public sealed class Dos33FileSystem : IAppleFileSystem
{
    private readonly ISectorReader sectors;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private readonly byte[] sectorBuffer = new byte[Dos33Layout.SectorSize];
    private readonly int firstCatalogTrack;
    private readonly int firstCatalogSector;
    private CatalogSnapshot? snapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="Dos33FileSystem"/> class.
    /// </summary>
    /// <param name="sectors">DOS logical sectors. Track 17 sector 0 must be a DOS 3.3 VTOC.</param>
    /// <param name="logger">Logger for a corrupt catalog or track/sector list. Not used per sector.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="sectors"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If the geometry is not DOS 3.3 or track 17 sector 0 is not a VTOC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If the sector reader rejects the VTOC read.</exception>
    /// <exception cref="InvalidOperationException">If the sector reader refuses the VTOC read.</exception>
    /// <exception cref="IOException">If the VTOC sector cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">If <paramref name="sectors"/> has been disposed.</exception>
    public Dos33FileSystem(ISectorReader sectors, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sectors);
        ArgumentNullException.ThrowIfNull(logger);
        if (sectors.BytesPerSector != Dos33Layout.SectorSize
            || sectors.SectorsPerTrack != Dos33Layout.DefaultSectorsPerTrack)
        {
            throw new ArgumentException("DOS 3.3 reads 16 sectors of 256 bytes.", nameof(sectors));
        }

        if (sectors.TrackCount <= Dos33Layout.VtocTrack)
        {
            throw new ArgumentException("DOS 3.3 needs track 17.", nameof(sectors));
        }

        var vtoc = new byte[Dos33Layout.SectorSize];
        sectors.ReadSector(Dos33Layout.VtocTrack, Dos33Layout.VtocSector, vtoc);
        if (!Dos33Layout.IsVtoc(vtoc, sectors.TrackCount, sectors.SectorsPerTrack))
        {
            throw new ArgumentException("Track 17 sector 0 is not a DOS 3.3 VTOC.", nameof(sectors));
        }

        this.sectors = sectors;
        this.logger = logger.ForContext<Dos33FileSystem>();
        this.firstCatalogTrack = vtoc[Dos33Layout.Vtoc.CatalogTrackOffset];
        this.firstCatalogSector = vtoc[Dos33Layout.Vtoc.CatalogSectorOffset];
        this.VolumeName = vtoc[Dos33Layout.Vtoc.VolumeOffset].ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the volume number as an invariant decimal.</summary>
    /// <value>The VTOC volume, with no prefix and no leading zeros.</value>
    public string VolumeName { get; }

    /// <summary>Gets the DOS 3.3 volume type.</summary>
    /// <value><see cref="AppleFileSystemKind.Dos33"/>.</value>
    public AppleFileSystemKind Kind => AppleFileSystemKind.Dos33;

    /// <summary>
    /// Lists the volume root, or reports that a file is not a directory.
    /// </summary>
    /// <param name="path">
    /// <see langword="null"/>, empty, <c>/</c>, or <c>/{volume}</c> lists the root.
    /// Any other legal path is one file name.
    /// </param>
    /// <returns>
    /// Live catalog entries in on-disk order, or a failure when the path is illegal,
    /// missing, a file, or the catalog chain repeats a sector.
    /// </returns>
    public DirectoryListing ListDirectory(string? path)
    {
        lock (this.gate)
        {
            return this.List(path);
        }
    }

    /// <summary>
    /// Reads one file from its track/sector list.
    /// </summary>
    /// <param name="path">Volume-relative file name. Not <see langword="null"/>.</param>
    /// <param name="view">Raw sectors, or the logical view with the A/I/B header removed.</param>
    /// <returns>
    /// The file bytes, or a failure when the path is missing, the root, illegal, shorter
    /// than its header, or the track/sector list repeats a sector.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="view"/> is not a defined view.</exception>
    public FileReadResult TryReadFile(string path, FileReadView view)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (view is not (FileReadView.Raw or FileReadView.Logical))
        {
            throw new ArgumentOutOfRangeException(nameof(view), view, "The file view is not raw or logical.");
        }

        lock (this.gate)
        {
            return this.Read(path, view);
        }
    }

    /// <summary>
    /// Gets one live catalog entry.
    /// </summary>
    /// <param name="path">Volume-relative file name. Not <see langword="null"/>.</param>
    /// <returns>
    /// The entry, or <see langword="null"/> when the path is missing, illegal, the volume
    /// root, or the catalog chain repeats a sector.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    public FileEntry? GetFileInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (this.gate)
        {
            return this.FindInfo(path);
        }
    }

    private static string? DecodeName(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        var length = bytes.Length;
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)(bytes[i] & 0x7F);
        }

        while (length > 0 && chars[length - 1] == ' ')
        {
            length--;
        }

        if (length == 0)
        {
            return null;
        }

        return new string(chars[..length]);
    }

    private static LocatedFile? Find(CatalogSnapshot catalog, string name)
    {
        foreach (var file in catalog.Files)
        {
            if (file.Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private DirectoryListing List(string? path)
    {
        if (!this.TryParse(path, out var segments, out var reason))
        {
            return DirectoryListing.Failure(FileAccessStatus.InvalidPath, reason);
        }

        var catalog = this.LoadCatalog();
        if (catalog.Corrupt)
        {
            return DirectoryListing.Failure(FileAccessStatus.CorruptStructure, catalog.Reason);
        }

        if (segments.Count == 0)
        {
            var entries = new FileEntry[catalog.Files.Length];
            for (var i = 0; i < catalog.Files.Length; i++)
            {
                entries[i] = catalog.Files[i].Entry;
            }

            return DirectoryListing.Success(entries);
        }

        if (Find(catalog, segments[0]) is null)
        {
            return DirectoryListing.Failure(FileAccessStatus.NotFound, "The path was not found.");
        }

        return DirectoryListing.Failure(FileAccessStatus.NotADirectory, "DOS 3.3 has no subdirectories.");
    }

    private FileReadResult Read(string path, FileReadView view)
    {
        if (!this.TryParse(path, out var segments, out var reason))
        {
            return FileReadResult.Failure(FileAccessStatus.InvalidPath, reason);
        }

        if (segments.Count == 0)
        {
            return FileReadResult.Failure(FileAccessStatus.IsDirectory, "The path is the volume directory.");
        }

        var catalog = this.LoadCatalog();
        if (catalog.Corrupt)
        {
            return FileReadResult.Failure(FileAccessStatus.CorruptStructure, catalog.Reason);
        }

        var file = Find(catalog, segments[0]);
        if (file is null)
        {
            return FileReadResult.Failure(FileAccessStatus.NotFound, "The file was not found.");
        }

        return this.ReadLocated(file, view);
    }

    private FileEntry? FindInfo(string path)
    {
        if (!this.TryParse(path, out var segments, out _) || segments.Count == 0)
        {
            return null;
        }

        var catalog = this.LoadCatalog();
        if (catalog.Corrupt)
        {
            return null;
        }

        return Find(catalog, segments[0])?.Entry;
    }

    private bool TryParse(string? path, out IReadOnlyList<string> segments, out string reason) =>
        AppleFileSystemPaths.TryParse(
            path,
            this.VolumeName,
            AppleFileSystemPathStyle.Flat,
            out segments,
            out reason);

    private CatalogSnapshot LoadCatalog()
    {
        if (this.snapshot is not null)
        {
            return this.snapshot;
        }

        var files = new List<LocatedFile>();
        var visited = new HashSet<(int Track, int Sector)>();
        var track = this.firstCatalogTrack;
        var sector = this.firstCatalogSector;
        while (track != 0)
        {
            if (!visited.Add((track, sector)))
            {
                this.logger.Warning(
                    "DOS catalog chain repeated track {Track} sector {Sector}.",
                    track,
                    sector);
                return this.Remember(CatalogSnapshot.Failed("The DOS catalog chain repeats a sector."));
            }

            this.ReadIntoBuffer(track, sector);
            var nextTrack = this.sectorBuffer[Dos33Layout.Catalog.NextTrackOffset];
            var nextSector = this.sectorBuffer[Dos33Layout.Catalog.NextSectorOffset];
            if (!this.TryCopyEntries(out var pending))
            {
                this.logger.Warning(
                    "DOS catalog entry on track {Track} sector {Sector} has an empty name.",
                    track,
                    sector);
                return this.Remember(CatalogSnapshot.Failed("A DOS catalog entry has an empty name."));
            }

            foreach (var item in pending)
            {
                files.Add(this.Locate(item));
            }

            track = nextTrack;
            sector = nextSector;
        }

        return this.Remember(CatalogSnapshot.Ok(files));
    }

    private CatalogSnapshot Remember(CatalogSnapshot catalog)
    {
        this.snapshot = catalog;
        return catalog;
    }

    private bool TryCopyEntries(out List<PendingEntry> pending)
    {
        pending = new List<PendingEntry>(Dos33Layout.Catalog.EntriesPerSector);
        for (var slot = 0; slot < Dos33Layout.Catalog.EntriesPerSector; slot++)
        {
            var offset = Dos33Layout.Catalog.HeaderLength + (slot * Dos33Layout.CatalogEntry.Length);
            var entry = this.sectorBuffer.AsSpan(offset, Dos33Layout.CatalogEntry.Length);
            var tsTrack = entry[Dos33Layout.CatalogEntry.TrackOffset];
            if (tsTrack == Dos33Layout.UnusedTrack || tsTrack == Dos33Layout.DeletedTrack)
            {
                continue;
            }

            var name = DecodeName(entry.Slice(Dos33Layout.CatalogEntry.NameOffset, Dos33Layout.CatalogEntry.NameLength));
            if (name is null)
            {
                return false;
            }

            var typeByte = entry[Dos33Layout.CatalogEntry.TypeOffset];
            var allocated = (ushort)(entry[Dos33Layout.CatalogEntry.SectorCountOffset]
                | (entry[Dos33Layout.CatalogEntry.SectorCountOffset + 1] << 8));
            pending.Add(new PendingEntry(
                tsTrack,
                entry[Dos33Layout.CatalogEntry.SectorOffset],
                typeByte,
                allocated,
                name));
        }

        return true;
    }

    private LocatedFile Locate(PendingEntry pending)
    {
        var walk = this.WalkTrackSectorList(pending.TsTrack, pending.TsSector);
        var eof = 0;
        if (walk.Intact && walk.LastIndex >= 0)
        {
            eof = (walk.LastIndex + 1) * Dos33Layout.SectorSize;
        }

        var entry = new FileEntry(
            pending.Name,
            pending.Name,
            (byte)(pending.TypeByte & Dos33Layout.FileTypeMask),
            0,
            eof,
            pending.AllocatedUnits,
            Dos33Layout.SectorSize,
            (pending.TypeByte & Dos33Layout.LockedMask) != 0,
            false,
            FileStorageKind.DosTrackSectorList);
        return new LocatedFile(entry, walk.Pairs, walk.LastIndex, walk.Failure);
    }

    private TrackSectorWalk WalkTrackSectorList(byte startTrack, byte startSector)
    {
        var pairs = new List<TrackSectorPair>();
        var visited = new HashSet<(int Track, int Sector)>();
        var lastIndex = -1;
        var track = (int)startTrack;
        var sector = (int)startSector;
        while (track != 0)
        {
            if (!visited.Add((track, sector)))
            {
                this.logger.Warning(
                    "DOS track/sector list repeated track {Track} sector {Sector}.",
                    track,
                    sector);
                return new TrackSectorWalk("The track/sector list repeats a sector.", new List<TrackSectorPair>(), -1);
            }

            this.ReadIntoBuffer(track, sector);
            var nextTrack = this.sectorBuffer[Dos33Layout.TrackSectorList.NextTrackOffset];
            var nextSector = this.sectorBuffer[Dos33Layout.TrackSectorList.NextSectorOffset];
            var origin = this.sectorBuffer[Dos33Layout.TrackSectorList.SectorBaseOffset]
                | (this.sectorBuffer[Dos33Layout.TrackSectorList.SectorBaseOffset + 1] << 8);
            for (var pair = 0; pair < Dos33Layout.TrackSectorList.PairsPerSector; pair++)
            {
                var at = Dos33Layout.TrackSectorList.PairOffset + (pair * 2);
                var dataTrack = this.sectorBuffer[at];
                var dataSector = this.sectorBuffer[at + 1];
                if (dataTrack == 0 && dataSector == 0)
                {
                    continue;
                }

                var index = origin + pair;
                pairs.Add(new TrackSectorPair(index, dataTrack, dataSector));
                if (index > lastIndex)
                {
                    lastIndex = index;
                }
            }

            track = nextTrack;
            sector = nextSector;
        }

        if (lastIndex >= 0 && ((long)lastIndex + 1) * Dos33Layout.SectorSize > int.MaxValue)
        {
            this.logger.Warning("DOS track/sector list length does not fit in a byte array.");
            return new TrackSectorWalk("The track/sector list does not fit in memory.", new List<TrackSectorPair>(), -1);
        }

        return new TrackSectorWalk(null, pairs, lastIndex);
    }

    private FileReadResult ReadLocated(LocatedFile file, FileReadView view)
    {
        if (file.TsFailure is not null)
        {
            return FileReadResult.Failure(FileAccessStatus.CorruptStructure, file.TsFailure);
        }

        var raw = file.Raw;
        if (raw is null)
        {
            raw = this.ReadRaw(file);
            file.Raw = raw;
        }

        if (view == FileReadView.Raw)
        {
            return FileReadResult.Success(raw);
        }

        var payload = Dos33Layout.LogicalPayloadLength(file.Entry.FileType, raw);
        if (payload < 0)
        {
            return FileReadResult.Failure(FileAccessStatus.CorruptStructure, "The file is shorter than its DOS header.");
        }

        var header = Dos33Layout.LogicalHeaderLength(file.Entry.FileType);
        return FileReadResult.Success(raw.AsMemory(header, payload));
    }

    private byte[] ReadRaw(LocatedFile file)
    {
        if (file.LastIndex < 0)
        {
            return [];
        }

        var raw = new byte[(file.LastIndex + 1) * Dos33Layout.SectorSize];
        foreach (var pair in file.Pairs)
        {
            this.ReadIntoBuffer(pair.Track, pair.Sector);
            var start = pair.Index * Dos33Layout.SectorSize;
            this.sectorBuffer.AsSpan().CopyTo(raw.AsSpan(start, Dos33Layout.SectorSize));
        }

        return raw;
    }

    private void ReadIntoBuffer(int track, int sector) =>
        this.sectors.ReadSector(track, sector, this.sectorBuffer);

    private readonly record struct PendingEntry(byte TsTrack, byte TsSector, byte TypeByte, ushort AllocatedUnits, string Name);

    private readonly record struct TrackSectorPair(int Index, byte Track, byte Sector);

    private readonly record struct TrackSectorWalk(string? Failure, List<TrackSectorPair> Pairs, int LastIndex)
    {
        public bool Intact => this.Failure is null;
    }

    private sealed class CatalogSnapshot
    {
        private CatalogSnapshot(bool corrupt, string reason, LocatedFile[] files)
        {
            this.Corrupt = corrupt;
            this.Reason = reason;
            this.Files = files;
        }

        public bool Corrupt { get; }

        public string Reason { get; }

        public LocatedFile[] Files { get; }

        public static CatalogSnapshot Ok(List<LocatedFile> files) =>
            new(false, string.Empty, files.ToArray());

        public static CatalogSnapshot Failed(string reason) =>
            new(true, reason, []);
    }

    private sealed class LocatedFile
    {
        public LocatedFile(FileEntry entry, List<TrackSectorPair> pairs, int lastIndex, string? tsFailure)
        {
            this.Entry = entry;
            this.Pairs = pairs;
            this.LastIndex = lastIndex;
            this.TsFailure = tsFailure;
        }

        public FileEntry Entry { get; }

        public List<TrackSectorPair> Pairs { get; }

        public int LastIndex { get; }

        public string? TsFailure { get; }

        public byte[]? Raw { get; set; }
    }
}