// <copyright file="HostDrive.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One drive in the host's disk topology.
/// </summary>
/// <param name="Id">The slot and drive.</param>
/// <param name="Label">The button label, such as <c>S6 D1</c>.</param>
/// <param name="ImagePath">The mounted image path, or <see langword="null"/> when the drive is empty.</param>
/// <param name="ImageName">The image file name, or <see langword="null"/> when the drive is empty.</param>
/// <param name="WriteProtected">Whether the host flag refuses writes.</param>
/// <param name="Dirty">Whether the image has unsaved changes. Shut Down and Power Off confirm when this is set.</param>
/// <param name="Busy">Whether the host refuses mount, eject, and write-protect changes.</param>
/// <param name="Active">Whether the activity LED is on.</param>
/// <param name="IsBootDrive">Whether a drop on the monitor inserts here.</param>
/// <param name="Error">The host's last error for this drive, or <see langword="null"/>.</param>
/// <param name="Track">The quarter-track or track the host reports, or <see langword="null"/>.</param>
/// <param name="Block">The block the host reports, or <see langword="null"/>.</param>
public sealed record HostDrive(
    DriveId Id,
    string Label,
    string? ImagePath,
    string? ImageName,
    bool WriteProtected,
    bool Dirty,
    bool Busy,
    bool Active,
    bool IsBootDrive,
    string? Error,
    int? Track,
    int? Block);