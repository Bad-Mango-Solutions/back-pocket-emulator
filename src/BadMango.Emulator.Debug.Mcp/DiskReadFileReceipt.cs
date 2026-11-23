// <copyright file="DiskReadFileReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// The payload for <c>disk_read_file</c>.
/// </summary>
/// <param name="Source"><c>image</c> or <c>drive</c>.</param>
/// <param name="Location">The image path or <c>slot:drive</c>.</param>
/// <param name="Kind">The file-system kind.</param>
/// <param name="VolumeName">The volume name, with controls escaped.</param>
/// <param name="Path">The catalog path that matched the request.</param>
/// <param name="View"><c>raw</c> or <c>logical</c>.</param>
/// <param name="Length">The number of bytes represented by <paramref name="Text"/> or <paramref name="Base64"/>.</param>
/// <param name="Encoding"><c>text</c> or <c>base64</c>. Exactly one of the content fields is set.</param>
/// <param name="Text">
/// Host text when <paramref name="Encoding"/> is <c>text</c>. Apple carriage returns are newlines.
/// DOS text is truncated at the first <c>$00</c>. Other bytes use <c>\xNN</c>.
/// </param>
/// <param name="Base64">Base64 of the selected bytes when <paramref name="Encoding"/> is <c>base64</c>.</param>
public sealed record DiskReadFileReceipt(
    string Source,
    string Location,
    string Kind,
    string VolumeName,
    string Path,
    string View,
    int Length,
    string Encoding,
    string? Text,
    string? Base64);