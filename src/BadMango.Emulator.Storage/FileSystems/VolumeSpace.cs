// <copyright file="VolumeSpace.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Free, used, and total allocation units on one volume.
/// </summary>
/// <param name="FreeUnits">Units the bitmap marks free.</param>
/// <param name="UsedUnits">Total minus free. This is not the sum of catalog allocation counts.</param>
/// <param name="TotalUnits">The volume header total, or the DOS track geometry.</param>
public readonly record struct VolumeSpace(int FreeUnits, int UsedUnits, int TotalUnits);