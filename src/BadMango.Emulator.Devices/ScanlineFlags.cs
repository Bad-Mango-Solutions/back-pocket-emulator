// <copyright file="ScanlineFlags.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Per-line flags carried beside a composite dot stream.
/// </summary>
[Flags]
public enum ScanlineFlags : byte
{
    /// <summary>
    /// No line flags are set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The color-burst gate was open for this line.
    /// </summary>
    ColorBurst = 1,
}