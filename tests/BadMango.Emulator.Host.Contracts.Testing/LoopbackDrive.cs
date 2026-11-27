// <copyright file="LoopbackDrive.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// One drive in the loopback topology. The session mutates it while holding the machine gate.
/// </summary>
internal sealed class LoopbackDrive
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackDrive"/> class.
    /// </summary>
    /// <param name="id">The drive id.</param>
    /// <param name="boot">Whether this is the boot drive.</param>
    /// <param name="category">Whether the drive is removable media or fixed storage.</param>
    internal LoopbackDrive(DriveId id, bool boot, HostStorageCategory category = HostStorageCategory.RemovableMedia)
    {
        Id = id;
        IsBootDrive = boot;
        Category = category;
    }

    /// <summary>
    /// Gets the drive id.
    /// </summary>
    internal DriveId Id { get; }

    /// <summary>
    /// Gets a value indicating whether a drop on the monitor inserts here.
    /// </summary>
    internal bool IsBootDrive { get; }

    /// <summary>
    /// Gets whether this drive is removable media or fixed storage.
    /// </summary>
    internal HostStorageCategory Category { get; }

    /// <summary>
    /// Gets or sets the mounted image path.
    /// </summary>
    internal string? ImagePath { get; set; }

    /// <summary>
    /// Gets or sets the image file name.
    /// </summary>
    internal string? ImageName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image is write-protected.
    /// </summary>
    internal bool WriteProtected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the image has unsaved changes.
    /// </summary>
    internal bool Dirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the drive refuses mutation.
    /// </summary>
    internal bool Busy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the activity LED is on.
    /// </summary>
    internal bool Active { get; set; }

    /// <summary>
    /// Gets or sets the last error.
    /// </summary>
    internal string? Error { get; set; }

    /// <summary>
    /// Gets or sets the reported track.
    /// </summary>
    internal int? Track { get; set; }

    /// <summary>
    /// Gets or sets the reported block.
    /// </summary>
    internal int? Block { get; set; }

    /// <summary>
    /// Copies the drive into a client record.
    /// </summary>
    /// <returns>The drive snapshot.</returns>
    internal HostDrive ToDrive()
    {
        return new HostDrive(
            Id,
            Id.Label,
            ImagePath,
            ImageName,
            WriteProtected,
            Dirty,
            Busy,
            Active,
            IsBootDrive,
            Error,
            Track,
            Block,
            Category);
    }
}