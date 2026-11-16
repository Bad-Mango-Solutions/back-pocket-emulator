// <copyright file="DiskDriveSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Represents a drive snapshot.
/// </summary>
/// <param name="Drive">1-based drive number.</param>
/// <param name="Selected">Whether the drive is selected.</param>
/// <param name="MotorOn">Whether the motor is on.</param>
/// <param name="PhaseLatch">Current phase latch.</param>
/// <param name="QuarterTrack">Current quarter-track position.</param>
/// <param name="WriteProtect">Whether media is write-protected.</param>
/// <param name="HasMedia">Whether media is present.</param>
/// <param name="MountedImagePath">Mounted image path, if available.</param>
/// <param name="Geometry">Mounted-media geometry, if available.</param>
/// <param name="Error">
/// The error reported while reading this drive, when the rest of the controller listing succeeded.
/// </param>
public sealed record DiskDriveSnapshot(
    int Drive,
    bool Selected,
    bool MotorOn,
    int PhaseLatch,
    int QuarterTrack,
    bool WriteProtect,
    bool HasMedia,
    string? MountedImagePath,
    DiskGeometrySnapshot? Geometry,
    string? Error = null);