// <copyright file="IDecodedDiskRead.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Observes a decoded disk read. Raw nibbles are not reported.
/// </summary>
/// <remarks>
/// <para>
/// Disk II calls <see cref="OnDecodedSector"/> only after the GCR data field checksum
/// matches. A block-device card calls <see cref="OnDecodedBlock"/> after it has a block
/// number. Both calls pass integers. The card keeps a null reference when tracking is
/// off and does not call, so the read path does not allocate.
/// </para>
/// <para>
/// <see cref="OnVolumeChanged"/> drops the map after a mount or eject.
/// <see cref="OnVolumeWritten"/> drops the map after a decoded write is committed.
/// Neither call throws.
/// </para>
/// </remarks>
public interface IDecodedDiskRead
{
    /// <summary>
    /// Records one decoded Disk II sector. <paramref name="physicalSector"/> is the address-field sector.
    /// </summary>
    /// <param name="slot">Slot number of the controller.</param>
    /// <param name="drive">Zero-based drive index.</param>
    /// <param name="track">Track from the address field.</param>
    /// <param name="physicalSector">Physical sector from the address field.</param>
    void OnDecodedSector(int slot, int drive, int track, int physicalSector);

    /// <summary>
    /// Records one decoded block-device read.
    /// </summary>
    /// <param name="slot">Slot number of the card.</param>
    /// <param name="drive">Zero-based drive or unit index.</param>
    /// <param name="block">Zero-based block number.</param>
    void OnDecodedBlock(int slot, int drive, int block);

    /// <summary>
    /// Drops the cached map and load progress for a drive whose medium was swapped or ejected.
    /// </summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="drive">Zero-based drive index.</param>
    void OnVolumeChanged(int slot, int drive);

    /// <summary>
    /// Drops the cached map after a write. Progress already counted stays.
    /// </summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="drive">Zero-based drive index.</param>
    void OnVolumeWritten(int slot, int drive);
}