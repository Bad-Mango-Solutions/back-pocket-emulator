// <copyright file="FileActivityLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>One file-load row for the console and for <c>get_file_activity</c>.</summary>
/// <param name="Path">The full path. Empty when the name is not known.</param>
/// <param name="Label">The last path segment, or <c>unknown</c>.</param>
/// <param name="Size">The file size in bytes, or <see langword="null"/> when it is unknown.</param>
/// <param name="BytesRead">Bytes transferred so far.</param>
/// <param name="Position">The file mark, in bytes from the start.</param>
/// <param name="OpenedAtCycle">The cycle when the row was first reported.</param>
/// <param name="RefNum">The ProDOS ref_num, or <see langword="null"/> for a pre-MLI row.</param>
/// <param name="Active"><see langword="true"/> while this file is still being loaded.</param>
/// <param name="Line">
/// The shared progress text: <c>BASIC.SYSTEM 6656/10240 (65%)</c>, or <c>BASIC.SYSTEM 6656</c> when
/// <paramref name="Size"/> is null.
/// </param>
public sealed record FileActivityLine(
    string Path,
    string Label,
    uint? Size,
    uint BytesRead,
    uint Position,
    ulong OpenedAtCycle,
    byte? RefNum,
    bool Active,
    string Line);