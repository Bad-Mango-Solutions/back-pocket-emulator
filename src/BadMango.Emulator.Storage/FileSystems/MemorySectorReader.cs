// <copyright file="MemorySectorReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// <see cref="ISectorReader"/> over a DOS-ordered sector image held in memory.
/// </summary>
/// <remarks>
/// Offset <c>(track × sectorsPerTrack + sector) × bytesPerSector</c> is DOS logical
/// sector <c>sector</c> of <c>track</c>. The buffer is not copied. There is no write method.
/// </remarks>
public sealed class MemorySectorReader : ISectorReader
{
    private readonly ReadOnlyMemory<byte> image;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemorySectorReader"/> class.
    /// </summary>
    /// <param name="image">DOS-ordered sectors. Its length is the product of the geometry.</param>
    /// <param name="trackCount">Number of tracks. Positive. At least 18 when track 17 is required.</param>
    /// <param name="sectorsPerTrack">Sectors per track. Positive. 16 for DOS 3.3.</param>
    /// <param name="bytesPerSector">Bytes per sector. Positive. 256 for DOS 3.3.</param>
    /// <exception cref="ArgumentOutOfRangeException">If a count is not positive or the buffer length does not match.</exception>
    public MemorySectorReader(ReadOnlyMemory<byte> image, int trackCount, int sectorsPerTrack, int bytesPerSector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trackCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sectorsPerTrack);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSector);
        var required = (long)trackCount * sectorsPerTrack * bytesPerSector;
        if (required > int.MaxValue || image.Length != required)
        {
            throw new ArgumentOutOfRangeException(nameof(image), "Image length must equal the sector geometry.");
        }

        this.image = image;
        this.TrackCount = trackCount;
        this.SectorsPerTrack = sectorsPerTrack;
        this.BytesPerSector = bytesPerSector;
    }

    /// <inheritdoc />
    public int BytesPerSector { get; }

    /// <inheritdoc />
    public int TrackCount { get; }

    /// <inheritdoc />
    public int SectorsPerTrack { get; }

    /// <inheritdoc />
    public void ReadSector(int track, int sector, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(track);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(track, this.TrackCount);
        ArgumentOutOfRangeException.ThrowIfNegative(sector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sector, this.SectorsPerTrack);
        if (destination.Length != this.BytesPerSector)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), $"Destination must be {this.BytesPerSector} bytes.");
        }

        var offset = ((track * this.SectorsPerTrack) + sector) * this.BytesPerSector;
        this.image.Span.Slice(offset, this.BytesPerSector).CopyTo(destination);
    }
}