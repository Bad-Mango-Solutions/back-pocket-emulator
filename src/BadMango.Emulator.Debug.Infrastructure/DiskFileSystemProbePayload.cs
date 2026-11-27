// <copyright file="DiskFileSystemProbePayload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// One file-system probe recorded by <c>disk fsinfo</c>.
/// </summary>
/// <param name="Kind">The format that was tried.</param>
/// <param name="Recognized">Whether this format opened the volume.</param>
/// <param name="Selected">Whether this match is the volume the command uses.</param>
/// <param name="Reason">Why the format was recognized or rejected.</param>
public sealed record DiskFileSystemProbePayload(
    string Kind,
    bool Recognized,
    bool Selected,
    string Reason);