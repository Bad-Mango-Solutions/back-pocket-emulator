// <copyright file="HostVideoState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Soft-switch flags a client can read without a video-device reference.
/// </summary>
/// <param name="Output">The output kind. This matches the machine.</param>
/// <param name="Text">Whether text mode is selected.</param>
/// <param name="Mixed">Whether mixed mode is selected.</param>
/// <param name="Page2">Whether page 2 is selected.</param>
/// <param name="HiRes">Whether hi-res is selected.</param>
/// <param name="EightyColumns">Whether the 80-column display is selected.</param>
/// <param name="AltCharSet">Whether the alternate character set is selected.</param>
public sealed record HostVideoState(
    HostVideoKind Output,
    bool Text,
    bool Mixed,
    bool Page2,
    bool HiRes,
    bool EightyColumns,
    bool AltCharSet);