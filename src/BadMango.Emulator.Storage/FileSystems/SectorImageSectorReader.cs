// <copyright file="SectorImageSectorReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;

/// <summary>
/// <see cref="ISectorReader"/> of DOS logical sectors over a <see cref="SectorImageMedia"/>.
/// </summary>
/// <remarks>
/// DOS logical sector <c>L</c> is converted to a physical sector with the DOS 3.3 skew,
/// then <see cref="SectorImageMedia.ReadSectorPhysical"/> maps that physical sector into
/// whatever order the backing image uses. A <c>.do</c> and a <c>.po</c> of the same disk
/// therefore yield the same VTOC at track 17 sector 0. The wrapper does not own the image
/// and has no write method.
/// </remarks>
public sealed class SectorImageSectorReader : ISectorReader
{
    private readonly SectorImageMedia image;

    /// <summary>
    /// Initializes a new instance of the <see cref="SectorImageSectorReader"/> class.
    /// </summary>
    /// <param name="image">The sector image to read.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="image"/> is <see langword="null"/>.</exception>
    public SectorImageSectorReader(SectorImageMedia image)
    {
        ArgumentNullException.ThrowIfNull(image);
        this.image = image;
    }

    /// <inheritdoc />
    public int BytesPerSector => this.image.Geometry.BytesPerSector;

    /// <inheritdoc />
    public int TrackCount => this.image.Geometry.TrackCount;

    /// <inheritdoc />
    public int SectorsPerTrack => this.image.Geometry.SectorsPerTrack;

    /// <inheritdoc />
    public void ReadSector(int track, int sector, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sector, this.SectorsPerTrack);
        if (destination.Length != this.BytesPerSector)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), $"Destination must be {this.BytesPerSector} bytes.");
        }

        var physical = SectorSkew.LogicalToPhysical(SectorOrder.Dos33, sector);
        this.image.ReadSectorPhysical(track, physical, destination);
    }
}