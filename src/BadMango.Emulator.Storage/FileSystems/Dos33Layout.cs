// <copyright file="Dos33Layout.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// DOS 3.3 offsets the reader and the synthetic fixtures share.
/// </summary>
/// <remarks>
/// <para>
/// The VTOC is track <see cref="VtocTrack"/> sector <see cref="VtocSector"/>. The type byte
/// and the lock bit are at <see cref="CatalogEntry.TypeOffset"/>, not offset 0.
/// Offset 0 is the track of the track/sector list: <see cref="UnusedTrack"/> has never been
/// used, and <see cref="DeletedTrack"/> is a deleted file. Neither is a live file.
/// </para>
/// <para>
/// Track/sector pairs start at <see cref="TrackSectorList.PairOffset"/>. The next catalog
/// sector is <see cref="Catalog.NextTrackOffset"/>, and the next list sector is
/// <see cref="TrackSectorList.NextTrackOffset"/>. Pair 0 describes the file sector numbered
/// by the little-endian word at <see cref="TrackSectorList.SectorBaseOffset"/>. A <c>$00 $00</c>
/// pair that still has an occupied pair after it is a sparse hole, not the end of the list.
/// Readers follow the catalog chain and that file's list. They do not rescan track 17 for
/// file data. A repeated next pointer is a corrupt loop.
/// </para>
/// <para>
/// The free-sector bitmap starts at <see cref="Vtoc.BitmapOffset"/>, four bytes per track.
/// Those four bytes are one big-endian value, and a set bit means free. The highest
/// sector is the high bit: sector 15 is byte 0 bit 7, sector 8 is byte 0 bit 0, sector 7
/// is byte 1 bit 7, and sector 0 is byte 1 bit 0. <see cref="IsSectorFree"/> is that mapping.
/// A deleted catalog entry stores its original track/sector-list track byte, unchanged,
/// in <see cref="CatalogEntry.OriginalTrackOffset"/>.
/// </para>
/// </remarks>
public static class Dos33Layout
{
    /// <summary>Decoded bytes in one DOS 3.3 sector.</summary>
    public const int SectorSize = 256;

    /// <summary>Sectors per track on a 16-sector disk.</summary>
    public const int DefaultSectorsPerTrack = 16;

    /// <summary>Track that holds the VTOC and the catalog.</summary>
    public const int VtocTrack = 17;

    /// <summary>Sector of the VTOC.</summary>
    public const int VtocSector = 0;

    /// <summary>First catalog sector DOS 3.3 INIT writes.</summary>
    public const int DefaultFirstCatalogSector = 15;

    /// <summary>Track value of a deleted catalog entry.</summary>
    public const byte DeletedTrack = 0xFF;

    /// <summary>Track value of a catalog slot that has never been used.</summary>
    public const byte UnusedTrack = 0x00;

    /// <summary>Lock bit in the catalog type byte. It is not part of <see cref="Dos33FileType"/>.</summary>
    public const byte LockedMask = 0x80;

    /// <summary>File-type bits in the catalog type byte.</summary>
    public const byte FileTypeMask = 0x7F;

    /// <summary>Address and length header on a binary file, in bytes.</summary>
    public const int BinaryHeaderLength = 4;

    /// <summary>Length header on an Applesoft or Integer BASIC file, in bytes.</summary>
    public const int BasicHeaderLength = 2;

    /// <summary>
    /// Returns how many leading raw bytes the logical view removes for a DOS file type.
    /// </summary>
    /// <param name="fileType">Catalog type. The lock bit is ignored. An unknown type returns 0.</param>
    /// <returns>4 for binary, 2 for Applesoft or Integer BASIC, otherwise 0.</returns>
    public static int LogicalHeaderLength(byte fileType)
    {
        var kind = (Dos33FileType)(fileType & FileTypeMask);
        return Enum.IsDefined(kind) ? LogicalHeaderLength(kind) : 0;
    }

    /// <summary>
    /// Returns how many leading raw bytes the logical view removes for a DOS file type.
    /// </summary>
    /// <param name="fileType">Catalog type without the lock bit.</param>
    /// <returns>4 for binary, 2 for Applesoft or Integer BASIC, otherwise 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="fileType"/> is not a defined value.</exception>
    public static int LogicalHeaderLength(Dos33FileType fileType) => fileType switch
    {
        Dos33FileType.Binary or Dos33FileType.AdditionalBinary => BinaryHeaderLength,
        Dos33FileType.Applesoft or Dos33FileType.Integer or Dos33FileType.AdditionalApplesoft => BasicHeaderLength,
        Dos33FileType.Text or Dos33FileType.S or Dos33FileType.Relocatable => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(fileType), (byte)fileType, "The DOS file type is not defined."),
    };

    /// <summary>
    /// Returns how many payload bytes the logical view keeps after the DOS header.
    /// </summary>
    /// <param name="fileType">Catalog type. The lock bit is ignored.</param>
    /// <param name="raw">Raw file bytes, including the header and sector padding.</param>
    /// <returns>
    /// The full raw length for text and other types. For binary, Applesoft, and Integer,
    /// the minimum of the header's length word and the bytes after the header.
    /// <c>-1</c> when the buffer is shorter than the header.
    /// </returns>
    public static int LogicalPayloadLength(byte fileType, ReadOnlySpan<byte> raw) =>
        LogicalPayloadLength(LogicalHeaderLength(fileType), raw);

    /// <summary>
    /// Returns how many payload bytes the logical view keeps after the DOS header.
    /// </summary>
    /// <param name="fileType">Catalog type without the lock bit.</param>
    /// <param name="raw">Raw file bytes, including the header and sector padding.</param>
    /// <returns>
    /// The full raw length for text and other types. For binary, Applesoft, and Integer,
    /// the minimum of the header's length word and the bytes after the header.
    /// <c>-1</c> when the buffer is shorter than the header.
    /// </returns>
    public static int LogicalPayloadLength(Dos33FileType fileType, ReadOnlySpan<byte> raw) =>
        LogicalPayloadLength(LogicalHeaderLength(fileType), raw);

    /// <summary>
    /// Returns whether a sector is a DOS 3.3 VTOC for an image of the given geometry.
    /// </summary>
    /// <param name="sector">Track 17 sector 0. A short buffer is not a VTOC.</param>
    /// <param name="trackCount">Tracks in the image. Must be greater than <see cref="VtocTrack"/>.</param>
    /// <param name="sectorsPerTrack">Sectors per track. DOS 3.3 recognition requires 16.</param>
    /// <returns>
    /// <see langword="true"/> when the version is 3, the volume is 1–254, the catalog track is 17,
    /// and the track, sector, and byte counts match the image.
    /// </returns>
    public static bool IsVtoc(ReadOnlySpan<byte> sector, int trackCount, int sectorsPerTrack)
    {
        if (sector.Length < SectorSize || trackCount <= VtocTrack || sectorsPerTrack != DefaultSectorsPerTrack)
        {
            return false;
        }

        if (sector[Vtoc.CatalogTrackOffset] != VtocTrack || sector[Vtoc.DosVersionOffset] != Vtoc.Version33)
        {
            return false;
        }

        var catalogSector = sector[Vtoc.CatalogSectorOffset];
        if (catalogSector == 0 || catalogSector >= sectorsPerTrack)
        {
            return false;
        }

        var volume = sector[Vtoc.VolumeOffset];
        if (volume is < 1 or > 254 || sector[Vtoc.MaxPairsOffset] == 0)
        {
            return false;
        }

        if (sector[Vtoc.TrackCountOffset] != trackCount || sector[Vtoc.SectorsPerTrackOffset] != sectorsPerTrack)
        {
            return false;
        }

        var bytesPerSector = sector[Vtoc.BytesPerSectorOffset] | (sector[Vtoc.BytesPerSectorOffset + 1] << 8);
        return bytesPerSector == SectorSize;
    }

    /// <summary>
    /// Returns whether the VTOC bitmap marks a sector free.
    /// </summary>
    /// <param name="vtoc">The VTOC sector.</param>
    /// <param name="track">Track number.</param>
    /// <param name="sector">DOS logical sector number, 0–15.</param>
    /// <returns><see langword="true"/> when the sector's bit is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the bitmap byte would fall outside <paramref name="vtoc"/> or <paramref name="sector"/> is not 0–15.</exception>
    public static bool IsSectorFree(ReadOnlySpan<byte> vtoc, int track, int sector)
    {
        var located = LocateSectorBit(vtoc, track, sector);
        return (vtoc[located.Offset] & located.Mask) != 0;
    }

    /// <summary>
    /// Sets or clears one free-sector bit. A set bit means the sector is free.
    /// </summary>
    /// <param name="vtoc">The VTOC sector.</param>
    /// <param name="track">Track number.</param>
    /// <param name="sector">DOS logical sector number, 0–15.</param>
    /// <param name="free"><see langword="true"/> to mark the sector free.</param>
    /// <exception cref="ArgumentOutOfRangeException">If the bitmap byte would fall outside <paramref name="vtoc"/> or <paramref name="sector"/> is not 0–15.</exception>
    public static void SetSectorFree(Span<byte> vtoc, int track, int sector, bool free)
    {
        var located = LocateSectorBit(vtoc, track, sector);
        if (free)
        {
            vtoc[located.Offset] |= located.Mask;
        }
        else
        {
            vtoc[located.Offset] = (byte)(vtoc[located.Offset] & ~located.Mask);
        }
    }

    private static int LogicalPayloadLength(int header, ReadOnlySpan<byte> raw)
    {
        if (header == 0)
        {
            return raw.Length;
        }

        if (raw.Length < header)
        {
            return -1;
        }

        var declared = raw[header - 2] | (raw[header - 1] << 8);
        var available = raw.Length - header;
        return Math.Min(declared, available);
    }

    private static (int Offset, byte Mask) LocateSectorBit(ReadOnlySpan<byte> vtoc, int track, int sector)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfNegative(sector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sector, DefaultSectorsPerTrack);
        var fromHighSector = (DefaultSectorsPerTrack - 1) - sector;
        var offset = Vtoc.BitmapOffset + (track * Vtoc.BitmapBytesPerTrack) + (fromHighSector >> 3);
        if ((uint)offset >= (uint)vtoc.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(track), "The bitmap byte is outside the VTOC.");
        }

        var bit = 7 - (fromHighSector & 7);
        return (offset, (byte)(1 << bit));
    }

    /// <summary>Bytes of one catalog sector that are not file entries.</summary>
    public static class Catalog
    {
        /// <summary>Bytes before the first catalog entry.</summary>
        public const int HeaderLength = 0x0B;

        /// <summary>Catalog entries in one sector.</summary>
        public const int EntriesPerSector = 7;

        /// <summary>Offset of the next catalog track. Zero means this is the last sector.</summary>
        public const int NextTrackOffset = 1;

        /// <summary>Offset of the next catalog sector.</summary>
        public const int NextSectorOffset = 2;
    }

    /// <summary>One 35-byte catalog entry.</summary>
    public static class CatalogEntry
    {
        /// <summary>Bytes in one catalog entry.</summary>
        public const int Length = 35;

        /// <summary>Offset of the track/sector-list track.</summary>
        public const int TrackOffset = 0;

        /// <summary>Offset of the track/sector-list sector.</summary>
        public const int SectorOffset = 1;

        /// <summary>Offset of the file type and lock bit. Not offset 0.</summary>
        public const int TypeOffset = 2;

        /// <summary>Offset of the 30-byte high-bit name.</summary>
        public const int NameOffset = 3;

        /// <summary>Bytes in a catalog name, padded with <see cref="HighBitSpace"/>.</summary>
        public const int NameLength = 30;

        /// <summary>
        /// Offset of the last name byte. A deleted file stores its original track byte here, unchanged.
        /// </summary>
        public const int OriginalTrackOffset = 0x20;

        /// <summary>Offset of the little-endian sector count.</summary>
        public const int SectorCountOffset = 0x21;

        /// <summary>Catalog name pad: ASCII space with the high bit set.</summary>
        public const byte HighBitSpace = 0xA0;
    }

    /// <summary>One track/sector-list sector.</summary>
    public static class TrackSectorList
    {
        /// <summary>Offset of the next list track. Zero means this is the last sector.</summary>
        public const int NextTrackOffset = 1;

        /// <summary>Offset of the next list sector.</summary>
        public const int NextSectorOffset = 2;

        /// <summary>Offset of the little-endian file-sector number described by pair 0.</summary>
        public const int SectorBaseOffset = 5;

        /// <summary>Offset of the first track/sector pair.</summary>
        public const int PairOffset = 0x0C;

        /// <summary>Pairs stored in one list sector.</summary>
        public const int PairsPerSector = 122;
    }

    /// <summary>Offsets within the VTOC sector.</summary>
    public static class Vtoc
    {
        /// <summary>Offset of the first catalog track.</summary>
        public const int CatalogTrackOffset = 0x01;

        /// <summary>Offset of the first catalog sector.</summary>
        public const int CatalogSectorOffset = 0x02;

        /// <summary>Offset of the DOS version. DOS 3.3 stores <see cref="Version33"/>.</summary>
        public const int DosVersionOffset = 0x03;

        /// <summary>Offset of the volume number.</summary>
        public const int VolumeOffset = 0x06;

        /// <summary>Offset of the maximum pairs per track/sector list.</summary>
        public const int MaxPairsOffset = 0x27;

        /// <summary>Offset of the track count.</summary>
        public const int TrackCountOffset = 0x34;

        /// <summary>Offset of the sectors-per-track count.</summary>
        public const int SectorsPerTrackOffset = 0x35;

        /// <summary>Offset of the little-endian bytes-per-sector count.</summary>
        public const int BytesPerSectorOffset = 0x36;

        /// <summary>Offset of the free-sector bitmap.</summary>
        public const int BitmapOffset = 0x38;

        /// <summary>Bitmap bytes reserved for one track.</summary>
        public const int BitmapBytesPerTrack = 4;

        /// <summary>DOS release byte written by DOS 3.3.</summary>
        public const byte Version33 = 0x03;
    }

    /// <summary>The DOS 3.3 boot sector at track 0 sector 0.</summary>
    public static class BootSector
    {
        private static readonly byte[] SignatureBytes =
        [
            0x01, 0xA5, 0x27, 0xC9, 0x09,
        ];

        /// <summary>Gets the boot-sector prefix <c>01 A5 27 C9 09</c>.</summary>
        /// <value>The five bytes at the start of track 0 sector 0.</value>
        public static ReadOnlySpan<byte> Signature => SignatureBytes;
    }
}