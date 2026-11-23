// <copyright file="Dos33ImageBuilder.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Builds a tiny DOS 3.3 image in memory. The VTOC stays on track 17.
/// </summary>
/// <remarks>
/// File data is not placed on track 0 or the catalog track. A catalog track byte of zero
/// means the slot has never been used, so a live track/sector list cannot be on track 0.
/// Names are uppercase letters, digits, and periods. The image contains no DOS boot code
/// and no commercial software.
/// </remarks>
public sealed class Dos33ImageBuilder
{
    private const int SectorsPerTrack = Dos33Layout.DefaultSectorsPerTrack;
    private const int SectorSize = Dos33Layout.SectorSize;

    private readonly int trackCount;
    private readonly byte volume;
    private readonly List<FileSpec> files = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Dos33ImageBuilder"/> class.
    /// </summary>
    /// <param name="trackCount">Tracks. Must be greater than 17 so the VTOC exists. At most 80.</param>
    /// <param name="volume">Volume number, 1–254.</param>
    /// <exception cref="ArgumentOutOfRangeException">If the geometry or volume is outside those bounds.</exception>
    public Dos33ImageBuilder(int trackCount = 18, byte volume = 254)
    {
        if (trackCount <= Dos33Layout.VtocTrack || trackCount > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(trackCount), "Track count must be 18 through 80 so track 17 exists.");
        }

        if (volume is < 1 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "DOS 3.3 volume numbers are 1 through 254.");
        }

        this.trackCount = trackCount;
        this.volume = volume;
    }

    /// <summary>
    /// Adds a catalog entry.
    /// </summary>
    /// <param name="name">File name, 1–30 characters.</param>
    /// <param name="fileType">Type without the lock bit.</param>
    /// <param name="locked">Whether to set the lock bit at entry offset 2.</param>
    /// <param name="deleted">Whether the track byte is <c>$FF</c>. The list and data are still written.</param>
    /// <param name="dataSectors">
    /// On-disk sector bodies in list order. A <see langword="null"/> entry is a sparse hole.
    /// Each body is at most 256 bytes and is padded with zeros. The caller includes any A/I/B header.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">If the name or sector list is not usable.</exception>
    public Dos33ImageBuilder AddFile(string name, Dos33FileType fileType, bool locked, bool deleted, IReadOnlyList<byte[]?> dataSectors)
    {
        this.files.Add(new FileSpec(NormalizeName(name), fileType, locked, deleted, CopySectors(dataSectors), null));
        return this;
    }

    /// <summary>
    /// Adds a catalog entry whose name bytes are written as given, including a clear high bit.
    /// </summary>
    /// <param name="catalogName">One to 30 on-disk name bytes. Trailing <c>$A0</c> padding is added.</param>
    /// <param name="fileType">Type without the lock bit.</param>
    /// <param name="locked">Whether to set the lock bit at entry offset 2.</param>
    /// <param name="deleted">Whether the track byte is <c>$FF</c>. The list and data are still written.</param>
    /// <param name="dataSectors">
    /// On-disk sector bodies in list order. A <see langword="null"/> entry is a sparse hole.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">If the name bytes are empty, longer than 30, or only padding.</exception>
    public Dos33ImageBuilder AddCatalogName(ReadOnlySpan<byte> catalogName, Dos33FileType fileType, bool locked, bool deleted, IReadOnlyList<byte[]?> dataSectors)
    {
        var copy = catalogName.ToArray();
        if (copy.Length is < 1 or > Dos33Layout.CatalogEntry.NameLength)
        {
            throw new ArgumentException("A DOS catalog name is 1 to 30 bytes.", nameof(catalogName));
        }

        if (!AppleCatalogName.TryDecodeDos(copy, out var name, out _))
        {
            throw new ArgumentException("The catalog name is empty after padding is removed.", nameof(catalogName));
        }

        this.files.Add(new FileSpec(name, fileType, locked, deleted, CopySectors(dataSectors), copy));
        return this;
    }

    /// <summary>
    /// Writes the VTOC, catalog chain, track/sector lists, and file bytes.
    /// </summary>
    /// <returns>A new image. Repeated calls return independent buffers.</returns>
    /// <exception cref="InvalidOperationException">If the catalog track or the data tracks run out of sectors.</exception>
    public Dos33Fixture Build()
    {
        var image = new byte[this.trackCount * SectorsPerTrack * SectorSize];
        var used = new bool[this.trackCount, SectorsPerTrack];
        var entries = new List<Dos33Fixture.Entry>(this.files.Count);
        var placed = new List<PlacedFile>(this.files.Count);

        foreach (var file in this.files)
        {
            var ts = Allocate(used);
            var data = new Dos33Fixture.SectorRef[file.Sectors.Length];
            for (var i = 0; i < file.Sectors.Length; i++)
            {
                if (file.Sectors[i] is null)
                {
                    data[i] = new Dos33Fixture.SectorRef(0, 0, false);
                    continue;
                }

                var sector = Allocate(used);
                WriteSector(image, sector.Track, sector.Sector, file.Sectors[i]!);
                data[i] = new Dos33Fixture.SectorRef(sector.Track, sector.Sector, true);
            }

            WriteTsList(image, ts.Track, ts.Sector, data);
            placed.Add(new PlacedFile(file, ts, data));
        }

        WriteCatalog(image, used, placed, entries);
        WriteVtoc(image, used);
        return new Dos33Fixture(image, this.trackCount, this.volume, entries);
    }

    private (int Track, int Sector) Allocate(bool[,] used)
    {
        for (var track = 0; track < this.trackCount; track++)
        {
            if (track == 0 || track == Dos33Layout.VtocTrack)
            {
                continue;
            }

            for (var sector = 0; sector < SectorsPerTrack; sector++)
            {
                if (!used[track, sector])
                {
                    used[track, sector] = true;
                    return (track, sector);
                }
            }
        }

        throw new InvalidOperationException("The DOS image ran out of data sectors.");
    }

    private void WriteCatalog(byte[] image, bool[,] used, List<PlacedFile> placed, List<Dos33Fixture.Entry> entries)
    {
        var chunkCount = Math.Max(1, (placed.Count + Dos33Layout.Catalog.EntriesPerSector - 1) / Dos33Layout.Catalog.EntriesPerSector);
        var first = Dos33Layout.DefaultFirstCatalogSector;
        if (first - chunkCount + 1 < 1)
        {
            throw new InvalidOperationException("The DOS catalog track does not have enough sectors.");
        }

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var sector = first - chunk;
            var catalog = new byte[SectorSize];
            var last = chunk == chunkCount - 1;
            if (!last)
            {
                catalog[Dos33Layout.Catalog.NextTrackOffset] = Dos33Layout.VtocTrack;
                catalog[Dos33Layout.Catalog.NextSectorOffset] = (byte)(sector - 1);
            }

            var start = chunk * Dos33Layout.Catalog.EntriesPerSector;
            var count = Math.Min(Dos33Layout.Catalog.EntriesPerSector, placed.Count - start);
            for (var slot = 0; slot < count; slot++)
            {
                var file = placed[start + slot];
                var entry = catalog.AsSpan(Dos33Layout.Catalog.HeaderLength + (slot * Dos33Layout.CatalogEntry.Length), Dos33Layout.CatalogEntry.Length);
                entry[Dos33Layout.CatalogEntry.TrackOffset] = file.Spec.Deleted ? Dos33Layout.DeletedTrack : (byte)file.Ts.Track;
                entry[Dos33Layout.CatalogEntry.SectorOffset] = (byte)file.Ts.Sector;
                entry[Dos33Layout.CatalogEntry.TypeOffset] = (byte)((byte)file.Spec.FileType | (file.Spec.Locked ? Dos33Layout.LockedMask : 0));
                WriteName(entry.Slice(Dos33Layout.CatalogEntry.NameOffset, Dos33Layout.CatalogEntry.NameLength), file.Spec);
                if (file.Spec.Deleted)
                {
                    entry[Dos33Layout.CatalogEntry.OriginalTrackOffset] = (byte)file.Ts.Track;
                }

                var allocated = 1 + file.Data.Count(item => item.Allocated);
                entry[Dos33Layout.CatalogEntry.SectorCountOffset] = (byte)allocated;
                entry[Dos33Layout.CatalogEntry.SectorCountOffset + 1] = (byte)(allocated >> 8);
                var raw = BuildRaw(file);
                entries.Add(new Dos33Fixture.Entry(
                    file.Spec.Name,
                    file.Spec.FileType,
                    file.Spec.Locked,
                    file.Spec.Deleted,
                    Dos33Layout.VtocTrack,
                    sector,
                    slot,
                    file.Ts.Track,
                    file.Ts.Sector,
                    file.Data,
                    raw,
                    allocated));
            }

            WriteSector(image, Dos33Layout.VtocTrack, sector, catalog);
            used[Dos33Layout.VtocTrack, sector] = true;
        }
    }

    private void WriteVtoc(byte[] image, bool[,] used)
    {
        var vtoc = new byte[SectorSize];
        vtoc[Dos33Layout.Vtoc.CatalogTrackOffset] = Dos33Layout.VtocTrack;
        vtoc[Dos33Layout.Vtoc.CatalogSectorOffset] = Dos33Layout.DefaultFirstCatalogSector;
        vtoc[Dos33Layout.Vtoc.DosVersionOffset] = Dos33Layout.Vtoc.Version33;
        vtoc[Dos33Layout.Vtoc.VolumeOffset] = this.volume;
        vtoc[Dos33Layout.Vtoc.MaxPairsOffset] = Dos33Layout.TrackSectorList.PairsPerSector;
        vtoc[0x31] = 1;
        vtoc[Dos33Layout.Vtoc.TrackCountOffset] = (byte)this.trackCount;
        vtoc[Dos33Layout.Vtoc.SectorsPerTrackOffset] = SectorsPerTrack;
        vtoc[Dos33Layout.Vtoc.BytesPerSectorOffset + 1] = 1;
        for (var catalogSector = 0; catalogSector < SectorsPerTrack; catalogSector++)
        {
            used[Dos33Layout.VtocTrack, catalogSector] = true;
        }

        var lastTrack = 0;
        for (var track = 0; track < this.trackCount; track++)
        {
            for (var sector = 0; sector < SectorsPerTrack; sector++)
            {
                if (used[track, sector])
                {
                    lastTrack = track;
                    continue;
                }

                Dos33Layout.SetSectorFree(vtoc, track, sector, true);
            }
        }

        vtoc[0x30] = (byte)lastTrack;
        WriteSector(image, Dos33Layout.VtocTrack, Dos33Layout.VtocSector, vtoc);
    }

    private void WriteTsList(byte[] image, int track, int sector, IReadOnlyList<Dos33Fixture.SectorRef> data)
    {
        if (data.Count > Dos33Layout.TrackSectorList.PairsPerSector)
        {
            throw new InvalidOperationException("A DOS fixture track/sector list holds at most 122 pairs.");
        }

        var list = new byte[SectorSize];
        for (var i = 0; i < data.Count; i++)
        {
            var pair = data[i];
            var offset = Dos33Layout.TrackSectorList.PairOffset + (i * 2);
            if (!pair.Allocated)
            {
                continue;
            }

            list[offset] = (byte)pair.Track;
            list[offset + 1] = (byte)pair.Sector;
        }

        WriteSector(image, track, sector, list);
    }

    private byte[] BuildRaw(PlacedFile file)
    {
        var last = -1;
        for (var i = 0; i < file.Data.Length; i++)
        {
            if (file.Data[i].Allocated)
            {
                last = i;
            }
        }

        if (last < 0)
        {
            return [];
        }

        var raw = new byte[(last + 1) * SectorSize];
        for (var i = 0; i <= last; i++)
        {
            var body = file.Spec.Sectors[i];
            if (body is null)
            {
                continue;
            }

            body.CopyTo(raw.AsSpan(i * SectorSize));
        }

        return raw;
    }

    private void WriteSector(byte[] image, int track, int sector, ReadOnlySpan<byte> data)
    {
        var offset = ((track * SectorsPerTrack) + sector) * SectorSize;
        var padded = new byte[SectorSize];
        data[..Math.Min(data.Length, SectorSize)].CopyTo(padded);
        padded.CopyTo(image.AsSpan(offset));
    }

    private void WriteName(Span<byte> destination, FileSpec file)
    {
        destination.Fill(Dos33Layout.CatalogEntry.HighBitSpace);
        if (file.CatalogName is byte[] raw)
        {
            raw.CopyTo(destination);
            return;
        }

        var name = file.Name;
        for (var i = 0; i < name.Length; i++)
        {
            destination[i] = (byte)(name[i] | 0x80);
        }
    }

    private string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var upper = name.ToUpperInvariant();
        if (upper.Length > Dos33Layout.CatalogEntry.NameLength)
        {
            throw new ArgumentException("DOS 3.3 names are at most 30 characters.", nameof(name));
        }

        foreach (var ch in upper)
        {
            var letter = ch is >= 'A' and <= 'Z';
            var digit = ch is >= '0' and <= '9';
            if (!letter && !digit && ch != '.')
            {
                throw new ArgumentException("DOS 3.3 fixture names are letters, digits, and periods.", nameof(name));
            }
        }

        return upper;
    }

    private byte[]?[] CopySectors(IReadOnlyList<byte[]?> dataSectors)
    {
        ArgumentNullException.ThrowIfNull(dataSectors);
        if (dataSectors.Count == 0 || dataSectors.Count > Dos33Layout.TrackSectorList.PairsPerSector)
        {
            throw new ArgumentException("A DOS fixture file needs 1 to 122 track/sector pairs.", nameof(dataSectors));
        }

        var copy = new byte[]?[dataSectors.Count];
        var occupied = false;
        for (var i = 0; i < dataSectors.Count; i++)
        {
            var sector = dataSectors[i];
            if (sector is null)
            {
                continue;
            }

            if (sector.Length > SectorSize)
            {
                throw new ArgumentException("A DOS sector body is at most 256 bytes.", nameof(dataSectors));
            }

            copy[i] = (byte[])sector.Clone();
            occupied = true;
        }

        if (!occupied)
        {
            throw new ArgumentException("A DOS fixture file needs one occupied sector.", nameof(dataSectors));
        }

        return copy;
    }

    private readonly record struct PlacedFile(FileSpec Spec, (int Track, int Sector) Ts, Dos33Fixture.SectorRef[] Data);

    private sealed class FileSpec
    {
        public FileSpec(string name, Dos33FileType fileType, bool locked, bool deleted, byte[]?[] sectors, byte[]? catalogName)
        {
            this.Name = name;
            this.FileType = fileType;
            this.Locked = locked;
            this.Deleted = deleted;
            this.Sectors = sectors;
            this.CatalogName = catalogName;
        }

        public string Name { get; }

        public byte[]? CatalogName { get; }

        public Dos33FileType FileType { get; }

        public bool Locked { get; }

        public bool Deleted { get; }

        public byte[]?[] Sectors { get; }
    }
}