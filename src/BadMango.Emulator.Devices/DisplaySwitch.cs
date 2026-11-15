// <copyright file="DisplaySwitch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// A display-affecting switch that can change during a frame.
/// </summary>
/// <remarks>
/// RGB-card and IIgs switches are appended by those devices' specs. They are not members yet.
/// </remarks>
public enum DisplaySwitch : byte
{
    /// <summary>
    /// TEXT. Owned by the video device.
    /// </summary>
    Text,

    /// <summary>
    /// MIXED. Owned by the video device.
    /// </summary>
    Mixed,

    /// <summary>
    /// PAGE2. Owned by the video device.
    /// </summary>
    Page2,

    /// <summary>
    /// HIRES. Owned by the video device.
    /// </summary>
    HiRes,

    /// <summary>
    /// 80COL. Owned by the video device.
    /// </summary>
    Col80,

    /// <summary>
    /// 80STORE. Owned by the video device.
    /// </summary>
    Store80,

    /// <summary>
    /// AN3 / double hi-res. Owned by the video device.
    /// </summary>
    DoubleHiRes,

    /// <summary>
    /// ALTCHAR. Owned by the character device.
    /// </summary>
    AltCharSet,

    /// <summary>
    /// NOFLASH for glyph bank 1. Owned by the character device.
    /// </summary>
    NoFlash1,

    /// <summary>
    /// NOFLASH for glyph bank 2. Owned by the character device.
    /// </summary>
    NoFlash2,

    /// <summary>
    /// ALTGLYPH for glyph bank 1. Owned by the character device.
    /// </summary>
    AltGlyph1,

    /// <summary>
    /// ALTGLYPH for glyph bank 2. Owned by the character device.
    /// </summary>
    AltGlyph2,
}