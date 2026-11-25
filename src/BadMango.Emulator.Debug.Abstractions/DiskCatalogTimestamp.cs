// <copyright file="DiskCatalogTimestamp.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// A ProDOS creation or modification stamp carried by <c>disk cat</c> JSON.
/// </summary>
/// <param name="Year">The full year. 40–99 in the date word is 19xx, and 0–39 is 20xx.</param>
/// <param name="Month">The month, 1–12.</param>
/// <param name="Day">The day of the month.</param>
/// <param name="Hour">The hour, 0–23.</param>
/// <param name="Minute">The minute, 0–59.</param>
public sealed record DiskCatalogTimestamp(int Year, int Month, int Day, int Hour, int Minute);