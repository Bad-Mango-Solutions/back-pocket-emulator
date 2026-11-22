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
/// and the lock bit are at catalog entry offset <see cref="EntryTypeOffset"/>, not offset 0.
/// Offset 0 is the track of the track/sector list: <see cref="UnusedTrack"/> has never been
/// used, and <see cref="DeletedTrack"/> is a deleted file. Neither is a live file.
/// </para>
/// <para>
/// Track/sector pairs start at <see cref="TsPairOffset"/>. A <c>$00 $00</c> pair that still
/// has an occupied pair after it is a sparse hole, not the end of the list. Readers follow
/// the catalog chain and that file's list. They do not rescan track 17 for file data.
/// A repeated next pointer is a corrupt loop.
/// </para>
/// <para>
/// The free-sector bitmap starts at <see cref="VtocBitmapOffset"/>, four bytes per track.
/// Those four bytes are one big-endian value, and a set bit means free. The highest
/// sector is the high bit: sector 15 is byte 0 bit 7, sector 8 is byte 0 bit 0, sector 7
/// is byte 1 bit 7, and sector 0 is byte 1 bit 0. <see cref="IsSectorFree"/> is that mapping.
/// A deleted catalog entry stores its original track/sector-list track byte, unchanged,
/// in the last name byte (<see cref="EntryOriginalTrackOffset"/>).
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

    /// <summary>Bytes before the first catalog entry in a catalog sector.</summary>
    public const int CatalogHeaderLength = 0x0B;

    /// <summary>Bytes in one catalog entry.</summary>
    public const int CatalogEntryLength = 35;

    /// <summary>Catalog entries in one sector.</summary>
    public const int CatalogEntriesPerSector = 7;

    /// <summary>Entry offset of the track/sector-list track.</summary>
    public const int EntryTrackOffset = 0;

    /// <summary>Entry offset of the track/sector-list sector.</summary>
    public const int EntrySectorOffset = 1;

    /// <summary>Entry offset of the file type and lock bit. Not offset 0.</summary>
    public const int EntryTypeOffset = 2;

    /// <summary>Entry offset of the 30-byte high-bit name.</summary>
    public const int EntryNameOffset = 3;

    /// <summary>
    /// Entry offset of the last name byte. A deleted file stores its original track byte
    /// here, unchanged.
    /// </summary>
    public const int EntryOriginalTrackOffset = 0x20;

    /// <summary>Bytes in a catalog name, padded with <see cref="HighBitSpace"/>.</summary>
    public const int EntryNameLength = 30;

    /// <summary>Entry offset of the little-endian sector count.</summary>
    public const int EntrySectorCountOffset = 0x21;

    /// <summary>Track value of a deleted catalog entry.</summary>
    public const byte DeletedTrack = 0xFF;

    /// <summary>Track value of a catalog slot that has never been used.</summary>
    public const byte UnusedTrack = 0x00;

    /// <summary>Lock bit in the type byte at <see cref="EntryTypeOffset"/>.</summary>
    public const byte LockedMask = 0x80;

    /// <summary>File-type bits in the type byte. The lock bit is not part of the type.</summary>
    public const byte FileTypeMask = 0x7F;

    /// <summary>Text file type.</summary>
    public const byte FileTypeText = 0x00;

    /// <summary>Integer BASIC file type.</summary>
    public const byte FileTypeInteger = 0x01;

    /// <summary>Applesoft BASIC file type.</summary>
    public const byte FileTypeApplesoft = 0x02;

    /// <summary>Binary file type.</summary>
    public const byte FileTypeBinary = 0x04;

    /// <summary>Offset of the first track/sector pair in a track/sector list.</summary>
    public const int TsPairOffset = 0x0C;

    /// <summary>Pairs stored in one track/sector-list sector.</summary>
    public const int TsPairsPerSector = 122;

    /// <summary>Address and length header on a binary file, in bytes.</summary>
    public const int BinaryHeaderLength = 4;

    /// <summary>Length header on an Applesoft or Integer BASIC file, in bytes.</summary>
    public const int BasicHeaderLength = 2;

    /// <summary>VTOC offset of the first catalog track.</summary>
    public const int VtocCatalogTrackOffset = 0x01;

    /// <summary>VTOC offset of the first catalog sector.</summary>
    public const int VtocCatalogSectorOffset = 0x02;

    /// <summary>VTOC offset of the DOS version. DOS 3.3 stores <see cref="DosVersion33"/>.</summary>
    public const int VtocDosVersionOffset = 0x03;

    /// <summary>VTOC offset of the volume number.</summary>
    public const int VtocVolumeOffset = 0x06;

    /// <summary>VTOC offset of the maximum pairs per track/sector list.</summary>
    public const int VtocMaxPairsOffset = 0x27;

    /// <summary>VTOC offset of the track count.</summary>
    public const int VtocTrackCountOffset = 0x34;

    /// <summary>VTOC offset of the sectors-per-track count.</summary>
    public const int VtocSectorsPerTrackOffset = 0x35;

    /// <summary>VTOC offset of the little-endian bytes-per-sector count.</summary>
    public const int VtocBytesPerSectorOffset = 0x36;

    /// <summary>VTOC offset of the free-sector bitmap.</summary>
    public const int VtocBitmapOffset = 0x38;

    /// <summary>Bitmap bytes reserved for one track.</summary>
    public const int BitmapBytesPerTrack = 4;

    /// <summary>DOS release byte written by DOS 3.3.</summary>
    public const byte DosVersion33 = 0x03;

    /// <summary>Catalog name pad: ASCII space with the high bit set.</summary>
    public const byte HighBitSpace = 0xA0;

    /// <summary>
    /// Returns how many leading raw bytes the logical view removes for a DOS file type.
    /// </summary>
    /// <param name="fileType">Catalog type. The lock bit is ignored.</param>
    /// <returns>4 for binary, 2 for Applesoft or Integer BASIC, otherwise 0.</returns>
    public static int LogicalHeaderLength(byte fileType)
    {
        var kind = (byte)(fileType & FileTypeMask);
        if (kind == FileTypeBinary)
        {
            return BinaryHeaderLength;
        }

        if (kind == FileTypeApplesoft || kind == FileTypeInteger)
        {
            return BasicHeaderLength;
        }

        return 0;
    }

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
    public static int LogicalPayloadLength(byte fileType, ReadOnlySpan<byte> raw)
    {
        var header = LogicalHeaderLength(fileType);
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

        if (sector[VtocCatalogTrackOffset] != VtocTrack || sector[VtocDosVersionOffset] != DosVersion33)
        {
            return false;
        }

        var catalogSector = sector[VtocCatalogSectorOffset];
        if (catalogSector == 0 || catalogSector >= sectorsPerTrack)
        {
            return false;
        }

        var volume = sector[VtocVolumeOffset];
        if (volume is < 1 or > 254 || sector[VtocMaxPairsOffset] == 0)
        {
            return false;
        }

        if (sector[VtocTrackCountOffset] != trackCount || sector[VtocSectorsPerTrackOffset] != sectorsPerTrack)
        {
            return false;
        }

        var bytesPerSector = sector[VtocBytesPerSectorOffset] | (sector[VtocBytesPerSectorOffset + 1] << 8);
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

    private static (int Offset, byte Mask) LocateSectorBit(ReadOnlySpan<byte> vtoc, int track, int sector)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfNegative(sector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sector, DefaultSectorsPerTrack);
        var fromHighSector = (DefaultSectorsPerTrack - 1) - sector;
        var offset = VtocBitmapOffset + (track * BitmapBytesPerTrack) + (fromHighSector >> 3);
        if ((uint)offset >= (uint)vtoc.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(track), "The bitmap byte is outside the VTOC.");
        }

        var bit = 7 - (fromHighSector & 7);
        return (offset, (byte)(1 << bit));
    }
}