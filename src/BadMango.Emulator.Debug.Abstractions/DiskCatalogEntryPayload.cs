// <copyright file="DiskCatalogEntryPayload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// One catalog row from <c>disk cat</c>.
/// </summary>
/// <param name="Name">The lookup name.</param>
/// <param name="DisplayName">The host name, with inverse and flashing characters escaped.</param>
/// <param name="Path">The volume-relative path.</param>
/// <param name="FileType">A short catalog type.</param>
/// <param name="FileTypeByte">The catalog type byte. The DOS lock bit is not included.</param>
/// <param name="AuxType">The ProDOS auxiliary type, or 0.</param>
/// <param name="EofBytes">The raw-view length in bytes.</param>
/// <param name="AllocatedUnits">The catalog allocation count.</param>
/// <param name="Locked">Whether the catalog marks the entry locked.</param>
/// <param name="Directory">Whether the entry is a ProDOS subdirectory.</param>
public sealed record DiskCatalogEntryPayload(
    string Name,
    string DisplayName,
    string Path,
    string FileType,
    int FileTypeByte,
    int AuxType,
    int EofBytes,
    int AllocatedUnits,
    bool Locked,
    bool Directory);