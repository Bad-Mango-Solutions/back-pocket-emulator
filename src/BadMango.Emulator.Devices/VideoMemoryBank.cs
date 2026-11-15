// <copyright file="VideoMemoryBank.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Memory bank a captured video write landed in.
/// </summary>
/// <remarks>
/// IIgs banks E0 and E1 are appended by that spec. They are not members yet.
/// </remarks>
public enum VideoMemoryBank : byte
{
    /// <summary>
    /// Main video memory.
    /// </summary>
    Main,

    /// <summary>
    /// Auxiliary video memory.
    /// </summary>
    Aux,
}