// <copyright file="HostDriveStatus.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

/// <summary>
/// Activity a machine-edge tap reports for one drive. This is not a client mount request.
/// </summary>
/// <param name="Active">Whether the activity LED is on.</param>
/// <param name="Dirty">Whether the image has unsaved changes.</param>
/// <param name="Busy">Whether the host refuses mutation.</param>
/// <param name="Track">The track the host reports, or <see langword="null"/>.</param>
/// <param name="Block">The block the host reports, or <see langword="null"/>.</param>
/// <param name="Error">The error text, or <see langword="null"/> to clear it.</param>
public readonly record struct HostDriveStatus(
    bool Active,
    bool Dirty,
    bool Busy,
    int? Track,
    int? Block,
    string? Error);