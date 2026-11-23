// <copyright file="DiskFileSystemInfoPayload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// JSON payload for <c>disk fsinfo</c>.
/// </summary>
/// <param name="Source"><c>image</c> or <c>drive</c>.</param>
/// <param name="Location">The image path or <c>slot:drive</c>.</param>
/// <param name="Recognized">Whether a volume was selected.</param>
/// <param name="Kind">The selected file-system kind, or <see langword="null"/>.</param>
/// <param name="VolumeName">The selected volume name, or <see langword="null"/>.</param>
/// <param name="Probes">Every probe that ran, in registration order.</param>
public sealed record DiskFileSystemInfoPayload(
    string Source,
    string Location,
    bool Recognized,
    string? Kind,
    string? VolumeName,
    IReadOnlyList<DiskFileSystemProbePayload> Probes);