// <copyright file="DiskCatalogPayload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// JSON payload for <c>disk cat</c>.
/// </summary>
/// <param name="Source"><c>image</c> or <c>drive</c>.</param>
/// <param name="Location">The image path or <c>slot:drive</c>.</param>
/// <param name="Kind">The file-system kind.</param>
/// <param name="VolumeName">The volume name, with controls escaped.</param>
/// <param name="Path">The directory that was listed. <c>/</c> is the volume root.</param>
/// <param name="Entries">Catalog entries in on-disk order.</param>
/// <param name="FreeUnits">Allocation units the volume bitmap marks free.</param>
/// <param name="UsedUnits">Total minus <paramref name="FreeUnits"/>.</param>
/// <param name="TotalUnits">The volume header total, or the DOS track geometry.</param>
/// <param name="Unit"><c>block</c> or <c>sector</c>.</param>
public sealed record DiskCatalogPayload(
    string Source,
    string Location,
    string Kind,
    string VolumeName,
    string Path,
    IReadOnlyList<DiskCatalogEntryPayload> Entries,
    int FreeUnits,
    int UsedUnits,
    int TotalUnits,
    string Unit);