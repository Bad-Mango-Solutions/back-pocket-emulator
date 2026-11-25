// <copyright file="FileActivitySnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>The file-load timeline the console and <c>get_file_activity</c> return.</summary>
/// <param name="Enabled"><see langword="false"/> when the boot log, and therefore the tracker, is off.</param>
/// <param name="Current">The file currently being loaded, or <see langword="null"/>.</param>
/// <param name="Files">Every row, oldest first, including loads that have finished.</param>
public sealed record FileActivitySnapshot(bool Enabled, FileActivityLine? Current, IReadOnlyList<FileActivityLine> Files);