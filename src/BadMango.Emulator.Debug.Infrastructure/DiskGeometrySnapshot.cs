// <copyright file="DiskGeometrySnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Represents a mounted-disk geometry snapshot.
/// </summary>
/// <param name="TrackCount">Track count.</param>
/// <param name="SectorsPerTrack">Sectors per track.</param>
/// <param name="BytesPerSector">Bytes per sector.</param>
/// <param name="SectorOrder">Sector-order identifier.</param>
/// <param name="TotalBytes">Total image bytes.</param>
public sealed record DiskGeometrySnapshot(
    int TrackCount,
    int SectorsPerTrack,
    int BytesPerSector,
    string SectorOrder,
    long TotalBytes);