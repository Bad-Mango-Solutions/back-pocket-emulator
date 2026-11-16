// <copyright file="ScreenTextSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Represents a text-screen snapshot plus mode flags.
/// </summary>
/// <param name="Rows">The number of rows.</param>
/// <param name="Columns">The number of columns (40 or 80).</param>
/// <param name="IsTextMode">Whether text mode is active.</param>
/// <param name="IsMixedMode">Whether mixed graphics/text mode is active.</param>
/// <param name="Is80Column">Whether 80-column mode is active.</param>
/// <param name="IsPage2">Whether page 2 is selected.</param>
/// <param name="Lines">The text lines.</param>
public sealed record ScreenTextSnapshot(
    int Rows,
    int Columns,
    bool IsTextMode,
    bool IsMixedMode,
    bool Is80Column,
    bool IsPage2,
    IReadOnlyList<string> Lines)
{
    /// <summary>
    /// Gets all rows joined by newline characters.
    /// </summary>
    public string CombinedText => string.Join(Environment.NewLine, this.Lines);
}