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
/// <param name="Path">The file that was read.</param>
/// <param name="View"><c>raw</c> or <c>logical</c>.</param>
/// <param name="Length">The number of bytes in the selected view.</param>
/// <param name="Text">
/// Host text for the selected view. Apple carriage returns are newlines. Other non-printable bytes use <c>\xNN</c>.
/// </param>
/// <param name="Base64">Base64 of the same bytes. This tool does not write those bytes to the host.</param>
public sealed record DiskReadFileReceipt(
    string Source,
    string Location,
    string Kind,
    string VolumeName,
    string Path,
    string View,
    int Length,
    string Text,
    string Base64);