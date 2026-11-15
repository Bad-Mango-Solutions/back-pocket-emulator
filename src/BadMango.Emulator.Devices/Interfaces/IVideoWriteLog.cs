// <copyright file="IVideoWriteLog.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// Reserved emulator-thread log of writes that land in a captured video region.
/// </summary>
/// <remarks>
/// Declared so beam-racing can be added later without changing public signatures.
/// Not installed in v1, so <see cref="CapturedFrame{TState}.WriteLog"/> stays empty and
/// capture cost is unchanged.
/// </remarks>
public interface IVideoWriteLog
{
    /// <summary>
    /// Appends one write that landed in a captured video region. Emulator thread only.
    /// </summary>
    /// <param name="address">The address that was written.</param>
    /// <param name="value">The byte that was written.</param>
    /// <param name="bank">The memory bank the write landed in.</param>
    /// <remarks>No allocation and no lock. Not installed in v1.</remarks>
    void Record(uint address, byte value, VideoMemoryBank bank);
}