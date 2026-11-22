// <copyright file="ISectorReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Read-only DOS 3.3 logical sector source.
/// </summary>
/// <remarks>
/// <para>
/// Sector numbers are DOS logical sectors: track 17 sector 0 is the VTOC on a DOS-ordered
/// image and on a ProDOS-ordered image. <see cref="SectorImageSectorReader"/> applies the
/// skew. Callers do not pass a <see cref="Func{T1, T2, TResult}"/>.
/// </para>
/// <para>
/// The interface has no write method. Reads are not required to be thread-safe.
/// Out-of-range tracks or sectors and the wrong buffer length throw
/// <see cref="ArgumentOutOfRangeException"/>.
/// </para>
/// </remarks>
public interface ISectorReader
{
    /// <summary>Gets the decoded sector size in bytes.</summary>
    /// <value>256 for DOS 3.3.</value>
    int BytesPerSector { get; }

    /// <summary>Gets the number of tracks the image contains.</summary>
    /// <value>The track count. A standard disk is 35. A tiny fixture may be 18 so track 17 exists.</value>
    int TrackCount { get; }

    /// <summary>Gets the number of sectors on each track.</summary>
    /// <value>16 for DOS 3.3.</value>
    int SectorsPerTrack { get; }

    /// <summary>
    /// Reads one DOS logical sector.
    /// </summary>
    /// <param name="track">Zero-based track in the range <c>[0, TrackCount)</c>.</param>
    /// <param name="sector">DOS logical sector in the range <c>[0, SectorsPerTrack)</c>.</param>
    /// <param name="destination">Destination buffer. Its length is <see cref="BytesPerSector"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="track"/> or <paramref name="sector"/> is out of range, or
    /// <paramref name="destination"/> has the wrong length.
    /// </exception>
    void ReadSector(int track, int sector, Span<byte> destination);
}