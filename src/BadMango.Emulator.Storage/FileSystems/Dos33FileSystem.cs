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
/// of track 17. A track byte of <see cref="Dos33Layout.UnusedTrack"/> ends the catalog,
/// including later sectors in the chain. A track byte of <see cref="Dos33Layout.DeletedTrack"/>
/// is skipped, and so is a live entry whose name is only padding. The type and the lock
/// bit are the byte at <see cref="Dos33Layout.CatalogEntry.TypeOffset"/>.
/// </para>
/// <para>
/// File bytes come from that file's track/sector list, including later list sectors.
/// The little-endian word at <see cref="Dos33Layout.TrackSectorList.SectorBaseOffset"/>
/// must be that list's position times <see cref="Dos33Layout.TrackSectorList.PairsPerSector"/>.
/// A <c>$00 $00</c> pair before the last occupied pair is 256 zero bytes, not the end of
/// the file. A repeated catalog sector, or a catalog link past the disk, fails
/// <see cref="ListDirectory"/>. A repeated track/sector list, a list or data pair past
/// the disk, a sector base that disagrees with the list position, or a logical length
/// longer than the disk fails
/// <see cref="TryReadFile"/> with <see cref="FileAccessStatus.CorruptStructure"/>.
/// That catalog entry is still listed, with an end-of-file of zero.
/// </para>
/// <para>
/// <see cref="FileReadView.Logical"/> removes an A, I, or B header through
/// <see cref="Dos33Layout.LogicalPayloadLength(byte, ReadOnlySpan{byte})"/>.
/// A text file's logical view is the raw bytes. It is not cut at the first <c>$00</c>.
/// DOS text is random-access and may contain interior zeros.
/// </para>
/// <para>
/// One <see cref="Lock"/> guards the sector buffer and the cache, so concurrent calls are safe.
/// The catalog and the file bytes are cached for the lifetime of the reader. Reopen the
/// volume after the image changes. DOS stores catalog case and does not fold it.
/// Lookup tries <see cref="StringComparison.Ordinal"/> first. A case-insensitive match
/// is used only when exactly one entry matches. Several matches are
/// <see cref="FileAccessStatus.Ambiguous"/> and the reason lists those names.
/// Catalog bytes normally have the high bit set. A clear high bit is inverse or flashing.
/// <see cref="FileEntry.Name"/> is the visible glyph and <see cref="FileEntry.DisplayName"/>
/// escapes those characters (<see cref="AppleCatalogName"/>).
/// Media exceptions are not caught.
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
        this.Allocation = Dos33Layout.CountFreeSectors(vtoc, sectors.TrackCount);
    }

    /// <summary>Gets the volume number as an invariant decimal.</summary>
    /// <value>The VTOC volume, with no prefix and no leading zeros.</value>
    public string VolumeName { get; }

    /// <summary>Gets the DOS 3.3 volume type.</summary>
    /// <value><see cref="AppleFileSystemKind.Dos33"/>.</value>
    public AppleFileSystemKind Kind => AppleFileSystemKind.Dos33;

    /// <summary>Gets free, used, and total sectors from the VTOC bitmap.</summary>
    /// <value>A set bitmap bit is a free sector. Total is tracks times 16.</value>
    public VolumeSpace Allocation { get; }

    /// <summary>
    /// Lists the volume root, or reports that a file is not a directory.
    /// </summary>
    /// <param name="path">
    /// <see langword="null"/>, empty, <c>/</c>, or <c>/{volume}</c> lists the root.
    /// Any other legal path is one file name.
    /// </param>
    /// <returns>
    /// Live catalog entries in on-disk order, or a failure when the path is illegal,
    /// missing, a file, or the catalog chain repeats a sector or leaves the disk.
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
    /// than its header, or the track/sector list repeats, leaves the disk, is longer than the disk, or has a bad sector base.
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
    /// The entry, or <see langword="null"/> when the path is missing, illegal, ambiguous,
    /// the volume root, or the catalog chain repeats a sector or leaves the disk.
    /// An exact name wins. A case-insensitive name is returned only when it is unique.
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

    private static bool TryDecodeName(ReadOnlySpan<byte> bytes, out string name, out string displayName) =>
        AppleCatalogName.TryDecodeDos(bytes, out name, out displayName);

    private static bool TryFind(CatalogSnapshot catalog, string name, out LocatedFile? file, out string? ambiguity)
    {
        file = null;
        ambiguity = null;
        LocatedFile? exact = null;
        var folded = new List<LocatedFile>();
        foreach (var entry in catalog.Files)
        {
            if (entry.Entry.Name.Equals(name, StringComparison.Ordinal))
            {
                exact = entry;
                break;
            }

            if (entry.Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                folded.Add(entry);
            }
        }

        if (exact is not null)
        {
            file = exact;
            return true;
        }

        if (folded.Count == 1)
        {
            file = folded[0];
            return true;
        }

        if (folded.Count > 1)
        {
            ambiguity = AmbiguousName(name, folded);
        }

        return false;
    }

    private static string AmbiguousName(string name, List<LocatedFile> matches)
    {
        var listed = new string[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            listed[i] = matches[i].Entry.Name;
        }

        return $"The name '{name}' matches more than one file: {string.Join(", ", listed)}.";
    }

    private static TrackSectorWalk FailedList(string reason) =>
        new(reason, new List<TrackSectorPair>(), -1);

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

        if (!TryFind(catalog, segments[0], out _, out var ambiguity))
        {
            return ambiguity is null
                ? DirectoryListing.Failure(FileAccessStatus.NotFound, "The path was not found.")
                : DirectoryListing.Failure(FileAccessStatus.Ambiguous, ambiguity);
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

        if (!TryFind(catalog, segments[0], out var file, out var ambiguity) || file is null)
        {
            return ambiguity is null
                ? FileReadResult.Failure(FileAccessStatus.NotFound, "The file was not found.")
                : FileReadResult.Failure(FileAccessStatus.Ambiguous, ambiguity);
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

        return TryFind(catalog, segments[0], out var file, out _) ? file?.Entry : null;
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
            if (!this.InRange(track, sector))
            {
                this.logger.Warning(
                    "DOS catalog chain left the disk at track {Track} sector {Sector}.",
                    track,
                    sector);
                return this.Remember(CatalogSnapshot.Failed("The DOS catalog chain leaves the disk."));
            }

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
            this.CopyEntries(out var pending, out var endOfCatalog);

            foreach (var item in pending)
            {
                files.Add(this.Locate(item));
            }

            if (endOfCatalog)
            {
                break;
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

    private void CopyEntries(out List<PendingEntry> pending, out bool endOfCatalog)
    {
        pending = new List<PendingEntry>(Dos33Layout.Catalog.EntriesPerSector);
        endOfCatalog = false;
        for (var slot = 0; slot < Dos33Layout.Catalog.EntriesPerSector; slot++)
        {
            var offset = Dos33Layout.Catalog.HeaderLength + (slot * Dos33Layout.CatalogEntry.Length);
            var entry = this.sectorBuffer.AsSpan(offset, Dos33Layout.CatalogEntry.Length);
            var tsTrack = entry[Dos33Layout.CatalogEntry.TrackOffset];
            if (tsTrack == Dos33Layout.UnusedTrack)
            {
                endOfCatalog = true;
                return;
            }

            if (tsTrack == Dos33Layout.DeletedTrack)
            {
                continue;
            }

            if (!TryDecodeName(entry.Slice(Dos33Layout.CatalogEntry.NameOffset, Dos33Layout.CatalogEntry.NameLength), out var name, out var displayName))
            {
                continue;
            }

            var typeByte = entry[Dos33Layout.CatalogEntry.TypeOffset];
            var allocated = (ushort)(entry[Dos33Layout.CatalogEntry.SectorCountOffset]
                | (entry[Dos33Layout.CatalogEntry.SectorCountOffset + 1] << 8));
            pending.Add(new PendingEntry(
                tsTrack,
                entry[Dos33Layout.CatalogEntry.SectorOffset],
                typeByte,
                allocated,
                name,
                displayName));
        }
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
            FileStorageKind.DosTrackSectorList,
            pending.DisplayName);
        return new LocatedFile(entry, walk.Pairs, walk.LastIndex, walk.Failure);
    }

    private TrackSectorWalk WalkTrackSectorList(byte startTrack, byte startSector)
    {
        var pairs = new List<TrackSectorPair>();
        var visited = new HashSet<(int Track, int Sector)>();
        var lastIndex = -1;
        var ordinal = 0;
        var track = (int)startTrack;
        var sector = (int)startSector;
        while (track != 0)
        {
            if (!this.InRange(track, sector))
            {
                this.logger.Warning(
                    "DOS track/sector list left the disk at track {Track} sector {Sector}.",
                    track,
                    sector);
                return FailedList("The track/sector list leaves the disk.");
            }

            if (!visited.Add((track, sector)))
            {
                this.logger.Warning(
                    "DOS track/sector list repeated track {Track} sector {Sector}.",
                    track,
                    sector);
                return FailedList("The track/sector list repeats a sector.");
            }

            this.ReadIntoBuffer(track, sector);
            var nextTrack = this.sectorBuffer[Dos33Layout.TrackSectorList.NextTrackOffset];
            var nextSector = this.sectorBuffer[Dos33Layout.TrackSectorList.NextSectorOffset];
            var origin = this.sectorBuffer[Dos33Layout.TrackSectorList.SectorBaseOffset]
                | (this.sectorBuffer[Dos33Layout.TrackSectorList.SectorBaseOffset + 1] << 8);
            if (origin != ordinal * Dos33Layout.TrackSectorList.PairsPerSector)
            {
                this.logger.Warning(
                    "DOS track/sector list ordinal {Ordinal} has sector offset {Origin}.",
                    ordinal,
                    origin);
                return FailedList("The track/sector list sector offset is wrong.");
            }

            for (var pair = 0; pair < Dos33Layout.TrackSectorList.PairsPerSector; pair++)
            {
                var at = Dos33Layout.TrackSectorList.PairOffset + (pair * 2);
                var dataTrack = this.sectorBuffer[at];
                var dataSector = this.sectorBuffer[at + 1];
                if (dataTrack == 0 && dataSector == 0)
                {
                    continue;
                }

                if (!this.InRange(dataTrack, dataSector))
                {
                    this.logger.Warning(
                        "DOS track/sector list named track {Track} sector {Sector}, which is off the disk.",
                        dataTrack,
                        dataSector);
                    return FailedList("The track/sector list leaves the disk.");
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
            ordinal++;
        }

        var diskSectors = (long)this.sectors.TrackCount * this.sectors.SectorsPerTrack;
        if (lastIndex >= 0 && lastIndex + 1L > diskSectors)
        {
            this.logger.Warning(
                "DOS track/sector list describes {SectorCount} sectors on a {DiskSectors}-sector disk.",
                lastIndex + 1L,
                diskSectors);
            return FailedList("The track/sector list is longer than the disk.");
        }

        return new TrackSectorWalk(null, pairs, lastIndex);
    }

    private bool InRange(int track, int sector) =>
        (uint)track < (uint)this.sectors.TrackCount && (uint)sector < (uint)this.sectors.SectorsPerTrack;

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

    private readonly record struct PendingEntry(byte TsTrack, byte TsSector, byte TypeByte, ushort AllocatedUnits, string Name, string DisplayName);

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