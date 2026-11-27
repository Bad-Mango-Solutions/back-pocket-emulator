// <copyright file="HostAudioDefaults.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The line-out format the loopback uses until a machine-edge tap replaces it.
/// </summary>
public static class HostAudioDefaults
{
    /// <summary>
    /// 44,100 Hz, mono, signed 16-bit little-endian PCM.
    /// </summary>
    public static HostAudioFormat LineOut { get; } = new(44_100, 1, 16, Signed: true, LittleEndian: true);
}