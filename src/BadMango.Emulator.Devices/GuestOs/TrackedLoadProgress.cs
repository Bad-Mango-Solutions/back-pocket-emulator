// <copyright file="TrackedLoadProgress.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using System.Globalization;

/// <summary>
/// Load progress for one file, or for the DOS 3.3 boot image.
/// </summary>
/// <param name="Path">Volume-relative path, or <see cref="BootImagePath"/>.</param>
/// <param name="Size">End-of-file in bytes, or the boot image size. <see langword="null"/> when the size is not known.</param>
/// <param name="BytesRead">Bytes accounted so far. A unit is counted once.</param>
/// <param name="Position">End offset of the latest read in the file.</param>
/// <param name="OpenCycle">Cycle of the first counted read. Zero when the caller did not supply a cycle.</param>
/// <remarks>
/// The first five values are the shape planned for <c>FileLoadProgress</c> (#699).
/// <see cref="UnitsRead"/>, <see cref="UnitCount"/>, and <see cref="UnitLabel"/> are local
/// until that record lands. A boot shows one line from the loader through a later MLI read.
/// </remarks>
public readonly record struct TrackedLoadProgress(
    string Path,
    long? Size,
    long BytesRead,
    long Position,
    long OpenCycle)
{
    /// <summary>Path stored for the DOS 3.3 tracks 0–2 boot image.</summary>
    public const string BootImagePath = "boot image";

    /// <summary>Gets how many data blocks or sectors have been counted.</summary>
    /// <value>The unit count. A ProDOS block counts once, even when it arrives as two sectors.</value>
    public int UnitsRead { get; init; }

    /// <summary>Gets how many data units the map recorded, when that is known.</summary>
    /// <value>The mapped unit count, or <see langword="null"/>.</value>
    public int? UnitCount { get; init; }

    /// <summary>Gets the unit noun, <c>blocks</c> or <c>sectors</c>.</summary>
    /// <value>The label used by <see cref="Describe"/>, or <see langword="null"/>.</value>
    public string? UnitLabel { get; init; }

    /// <summary>
    /// Formats one progress line. The DOS boot image is <c>boot image sectors read N/M</c>.
    /// </summary>
    /// <returns>The line. A file with a known size includes blocks or sectors and bytes.</returns>
    public string Describe()
    {
        if (this.Path == BootImagePath && this.UnitCount is int bootUnits)
        {
            return string.Create(CultureInfo.InvariantCulture, $"boot image sectors read {this.UnitsRead}/{bootUnits}");
        }

        if (this.Size is long size && size > 0)
        {
            var percent = this.BytesRead * 100 / size;
            if (this.UnitCount is int units && this.UnitLabel is not null)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{this.Path} {this.UnitsRead}/{units} {this.UnitLabel}, {this.BytesRead}/{size} ({percent}%)");
            }

            return string.Create(CultureInfo.InvariantCulture, $"{this.Path} {this.BytesRead}/{size} ({percent}%)");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{this.Path} {this.BytesRead}");
    }
}