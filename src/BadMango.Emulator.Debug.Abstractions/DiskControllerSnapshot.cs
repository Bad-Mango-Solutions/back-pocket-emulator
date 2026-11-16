// <copyright file="DiskControllerSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Represents a disk-controller snapshot.
/// </summary>
/// <param name="Slot">Controller slot number.</param>
/// <param name="Name">Controller display name.</param>
/// <param name="DriveCount">Controller drive count.</param>
/// <param name="Drives">Per-drive snapshots.</param>
public sealed record DiskControllerSnapshot(
    int Slot,
    string Name,
    int DriveCount,
    IReadOnlyList<DiskDriveSnapshot> Drives);