// <copyright file="IFlushableStorage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// A disk controller whose pending writes can be flushed without unmounting the media.
/// </summary>
/// <remarks>
/// Power-off walks components and slot cards for this interface. The machine does not
/// reference a concrete controller type, and flush failures propagate as the original
/// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
/// </remarks>
public interface IFlushableStorage
{
    /// <summary>
    /// Gets the number of drives this controller exposes.
    /// </summary>
    /// <value>The total drive count.</value>
    int DriveCount { get; }

    /// <summary>
    /// Flushes pending writes for the indicated drive. The medium stays mounted.
    /// </summary>
    /// <param name="driveIndex">Zero-based drive index.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="driveIndex"/> is outside the drive range.</exception>
    /// <exception cref="IOException">Thrown when the pending writes cannot be committed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the host denies the flush.</exception>
    void Flush(int driveIndex);
}