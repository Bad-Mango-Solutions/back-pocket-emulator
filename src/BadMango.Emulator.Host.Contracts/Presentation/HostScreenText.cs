// <copyright file="HostScreenText.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A text-screen snapshot. Agents read this instead of the video device. It is not the pixel stream.
/// </summary>
/// <param name="Columns">The column count, 40 or 80 on a real machine. The loopback stores what it is given.</param>
/// <param name="Rows">The row count.</param>
/// <param name="Text">The characters, row-major. The length is not required to be columns times rows.</param>
/// <param name="EightyColumns">Whether the 80-column display is selected.</param>
/// <param name="Mixed">Whether mixed text and graphics is selected.</param>
/// <param name="Page2">Whether page 2 is selected.</param>
public sealed record HostScreenText(
    int Columns,
    int Rows,
    string Text,
    bool EightyColumns,
    bool Mixed,
    bool Page2);