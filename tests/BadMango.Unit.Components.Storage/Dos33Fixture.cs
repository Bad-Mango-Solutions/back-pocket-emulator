// <copyright file="Dos33Fixture.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using System.Globalization;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// A DOS 3.3 sector image built in the test process. No bytes are read from <c>disks/</c>.
/// </summary>
public sealed class Dos33Fixture
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Dos33Fixture"/> class.
    /// </summary>
    /// <param name="image">DOS-ordered sector bytes.</param>
    /// <param name="trackCount">Tracks represented in <paramref name="image"/>.</param>
    /// <param name="volume">Volume number stored in the VTOC.</param>
    /// <param name="entries">Catalog entries, including deleted ones, in on-disk order.</param>
    public Dos33Fixture(ReadOnlyMemory<byte> image, int trackCount, byte volume, IReadOnlyList<Entry> entries)
    {
        this.Image = image;
        this.TrackCount = trackCount;
        this.Volume = volume;
        this.Entries = entries;
    }

    /// <summary>Gets the DOS-ordered image.</summary>
    /// <value>The sector bytes.</value>
    public ReadOnlyMemory<byte> Image { get; }

    /// <summary>Gets the track count.</summary>
    /// <value>The number of tracks.</value>
    public int TrackCount { get; }

    /// <summary>Gets the VTOC volume number.</summary>
    /// <value>The volume, 1–254.</value>
    public byte Volume { get; }

    /// <summary>Gets the volume name a DOS reader must report.</summary>
    /// <value>The volume number as an invariant decimal with no prefix.</value>
    public string VolumeName => this.Volume.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the catalog entries in on-disk order.</summary>
    /// <value>Live and deleted entries. Unused slots are not included.</value>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>
    /// Builds an empty formatted volume. Track 17 holds a VTOC and one empty catalog sector.
    /// </summary>
    /// <param name="volume">Volume number. Defaults to 254.</param>
    /// <param name="trackCount">Tracks. Defaults to 18 so track 17 exists.</param>
    /// <returns>A blank image.</returns>
    public static Dos33Fixture CreateBlank(byte volume = 254, int trackCount = 18) =>
        new Dos33ImageBuilder(trackCount, volume).Build();

    /// <summary>
    /// Builds a catalog that covers the DOS reader cases: type at offset 2, a lock bit,
    /// A/I/B headers, a sparse track/sector hole, and a deleted <c>$FF</c> entry.
    /// </summary>
    /// <param name="volume">Volume number. Defaults to 254.</param>
    /// <param name="trackCount">Tracks. Defaults to 18.</param>
    /// <returns>The sample image.</returns>
    public static Dos33Fixture CreateCatalogSample(byte volume = 254, int trackCount = 18)
    {
        var builder = new Dos33ImageBuilder(trackCount, volume);
        builder.AddFile("HELLO", Dos33FileType.Text, locked: false, deleted: false, new byte[]?[] { new byte[] { (byte)'H', (byte)'I' } });
        builder.AddFile("LOCKED", Dos33FileType.Binary, locked: true, deleted: false, new byte[]?[] { new byte[] { 0xA0, 0x08, 0x02, 0x00, (byte)'O', (byte)'K' } });
        builder.AddFile("BASIC", Dos33FileType.Applesoft, locked: false, deleted: false, new byte[]?[] { new byte[] { 0x02, 0x00, 0x11, 0x22 } });
        builder.AddFile("INTBAS", Dos33FileType.Integer, locked: false, deleted: false, new byte[]?[] { new byte[] { 0x01, 0x00, 0x33 } });
        builder.AddFile(
            "SPARSE",
            Dos33FileType.Text,
            locked: false,
            deleted: false,
            new byte[]?[] { new byte[] { (byte)'A', (byte)'A' }, null, new byte[] { (byte)'B', (byte)'B' } });
        builder.AddFile("GONE", Dos33FileType.Text, locked: false, deleted: true, new byte[]?[] { new byte[] { (byte)'X' } });
        return builder.Build();
    }

    /// <summary>
    /// Opens a sector reader over <see cref="Image"/>.
    /// </summary>
    /// <returns>A DOS logical sector reader.</returns>
    public ISectorReader CreateSectorReader() =>
        new MemorySectorReader(this.Image, this.TrackCount, Dos33Layout.DefaultSectorsPerTrack, Dos33Layout.SectorSize);

    /// <summary>A track/sector pair, or a sparse hole.</summary>
    /// <param name="Track">DOS track. Zero when <paramref name="Allocated"/> is <see langword="false"/>.</param>
    /// <param name="Sector">DOS logical sector. Zero when <paramref name="Allocated"/> is <see langword="false"/>.</param>
    /// <param name="Allocated">Whether the pair addresses a sector.</param>
    public readonly record struct SectorRef(int Track, int Sector, bool Allocated);

    /// <summary>One catalog entry written into a <see cref="Dos33Fixture"/>.</summary>
    public sealed class Entry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class.
        /// </summary>
        /// <param name="name">Catalog name.</param>
        /// <param name="fileType">Type without the lock bit.</param>
        /// <param name="isLocked">Whether the lock bit is set.</param>
        /// <param name="isDeleted">Whether the track byte is <c>$FF</c>.</param>
        /// <param name="catalogTrack">Catalog track.</param>
        /// <param name="catalogSector">Catalog sector.</param>
        /// <param name="catalogSlot">Slot within the catalog sector, 0–6.</param>
        /// <param name="tsTrack">Track/sector-list track.</param>
        /// <param name="tsSector">Track/sector-list sector.</param>
        /// <param name="dataSectors">Data pairs. A hole has <see cref="SectorRef.Allocated"/> clear.</param>
        /// <param name="rawBytes">Raw file bytes, including holes and a DOS header.</param>
        /// <param name="allocatedUnits">Catalog sector count, including the track/sector list.</param>
        public Entry(
            string name,
            Dos33FileType fileType,
            bool isLocked,
            bool isDeleted,
            int catalogTrack,
            int catalogSector,
            int catalogSlot,
            int tsTrack,
            int tsSector,
            IReadOnlyList<SectorRef> dataSectors,
            ReadOnlyMemory<byte> rawBytes,
            int allocatedUnits)
        {
            this.Name = name;
            this.FileType = fileType;
            this.IsLocked = isLocked;
            this.IsDeleted = isDeleted;
            this.CatalogTrack = catalogTrack;
            this.CatalogSector = catalogSector;
            this.CatalogSlot = catalogSlot;
            this.TsTrack = tsTrack;
            this.TsSector = tsSector;
            this.DataSectors = dataSectors;
            this.RawBytes = rawBytes;
            this.AllocatedUnits = allocatedUnits;
            var payload = Dos33Layout.LogicalPayloadLength(fileType, rawBytes.Span);
            var header = Dos33Layout.LogicalHeaderLength(fileType);
            this.LogicalBytes = payload < 0 ? ReadOnlyMemory<byte>.Empty : rawBytes.Slice(header, payload);
        }

        /// <summary>Gets the catalog name.</summary>
        /// <value>The name without high bits.</value>
        public string Name { get; }

        /// <summary>Gets the file type without the lock bit.</summary>
        /// <value>The type byte a reader stores on <see cref="FileEntry"/>.</value>
        public Dos33FileType FileType { get; }

        /// <summary>Gets a value indicating whether the lock bit is set.</summary>
        /// <value><see langword="true"/> when the catalog type byte has bit 7 set.</value>
        public bool IsLocked { get; }

        /// <summary>Gets a value indicating whether the entry is deleted.</summary>
        /// <value><see langword="true"/> when the track byte is <c>$FF</c>.</value>
        public bool IsDeleted { get; }

        /// <summary>Gets the catalog track.</summary>
        /// <value>Track 17.</value>
        public int CatalogTrack { get; }

        /// <summary>Gets the catalog sector.</summary>
        /// <value>15 for the first sector, then 14, 13, and so on.</value>
        public int CatalogSector { get; }

        /// <summary>Gets the slot in the catalog sector.</summary>
        /// <value>0 through 6.</value>
        public int CatalogSlot { get; }

        /// <summary>Gets the track/sector-list track.</summary>
        /// <value>The track stored at entry offset 0 when the file is not deleted.</value>
        public int TsTrack { get; }

        /// <summary>Gets the track/sector-list sector.</summary>
        /// <value>The sector stored at entry offset 1.</value>
        public int TsSector { get; }

        /// <summary>Gets the data pairs in list order.</summary>
        /// <value>Holes are unallocated refs between occupied pairs.</value>
        public IReadOnlyList<SectorRef> DataSectors { get; }

        /// <summary>Gets the raw file bytes a reader returns for <see cref="FileReadView.Raw"/>.</summary>
        /// <value>Full sectors through the last occupied pair, with holes as zeros.</value>
        public ReadOnlyMemory<byte> RawBytes { get; }

        /// <summary>Gets the logical bytes a reader returns for <see cref="FileReadView.Logical"/>.</summary>
        /// <value>The header is removed and the payload is trimmed to the length word.</value>
        public ReadOnlyMemory<byte> LogicalBytes { get; }

        /// <summary>Gets the catalog sector count.</summary>
        /// <value>Data sectors plus the track/sector list, excluding holes.</value>
        public int AllocatedUnits { get; }

        /// <summary>Gets a value indicating whether a hole precedes the last occupied pair.</summary>
        /// <value><see langword="true"/> when an unallocated pair still has an occupied pair after it.</value>
        public bool HasSparseHole
        {
            get
            {
                var occupiedLater = false;
                for (var i = this.DataSectors.Count - 1; i >= 0; i--)
                {
                    if (this.DataSectors[i].Allocated)
                    {
                        occupiedLater = true;
                    }
                    else if (occupiedLater)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}