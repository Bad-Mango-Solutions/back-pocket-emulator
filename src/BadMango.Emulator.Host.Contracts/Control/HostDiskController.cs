// <copyright file="HostDiskController.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One disk controller and the drives the host reports on it.
/// </summary>
/// <param name="Slot">The slot number, 1–7.</param>
/// <param name="Name">The controller name.</param>
/// <param name="Drives">The drives, in drive-number order.</param>
/// <param name="Category">Whether every drive on this controller is removable media or fixed storage.</param>
public sealed record HostDiskController(
    int Slot,
    string Name,
    IReadOnlyList<HostDrive> Drives,
    HostStorageCategory Category = HostStorageCategory.RemovableMedia);