// <copyright file="NtscDecoder.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// NTSC decode algorithm selected by <see cref="NtscOptions"/>.
/// </summary>
/// <remarks>
/// Filtered YIQ is reserved and is not a member until that decoder exists. v1 has only
/// the exact <see cref="FourDotWindow"/> value.
/// </remarks>
public enum NtscDecoder : byte
{
    /// <summary>
    /// The exact v1 decoder: a 4-dot sliding window.
    /// </summary>
    FourDotWindow,
}