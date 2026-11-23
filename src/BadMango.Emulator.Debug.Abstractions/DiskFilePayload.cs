// <copyright file="DiskFilePayload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// JSON payload for <c>disk read</c>.
/// </summary>
/// <param name="Source"><c>image</c> or <c>drive</c>.</param>
/// <param name="Location">The image path or <c>slot:drive</c>.</param>
/// <param name="Kind">The file-system kind.</param>
/// <param name="VolumeName">The volume name, with controls escaped.</param>
/// <param name="Path">The file that was read.</param>
/// <param name="View"><c>raw</c> or <c>logical</c>.</param>
/// <param name="Presentation"><c>text</c> or <c>hex</c>.</param>
/// <param name="Length">The number of bytes in <paramref name="Data"/>.</param>
/// <param name="Data">Lowercase hex of the selected view, with no separators.</param>
/// <param name="Text">The text rendering, or <see langword="null"/> when <paramref name="Presentation"/> is <c>hex</c>.</param>
/// <param name="SavedTo">The <c>--to</c> path relative to the output root, or <see langword="null"/>.</param>
public sealed record DiskFilePayload(
    string Source,
    string Location,
    string Kind,
    string VolumeName,
    string Path,
    string View,
    string Presentation,
    int Length,
    string Data,
    string? Text,
    string? SavedTo);