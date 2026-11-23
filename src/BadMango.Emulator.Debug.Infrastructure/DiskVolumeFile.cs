// <copyright file="DiskVolumeFile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>
/// Bytes read from one file on a DOS 3.3, ProDOS, or Pascal volume.
/// </summary>
/// <remarks>
/// The buffer is a copy. Disposing the image that produced it does not change these bytes.
/// </remarks>
public sealed class DiskVolumeFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiskVolumeFile"/> class.
    /// </summary>
    /// <param name="source"><c>image</c> or <c>drive</c>.</param>
    /// <param name="location">The image path or <c>slot:drive</c>.</param>
    /// <param name="kind">The file-system kind name.</param>
    /// <param name="volumeName">The volume name, with controls escaped.</param>
    /// <param name="path">The file that was read.</param>
    /// <param name="view"><c>raw</c> or <c>logical</c>.</param>
    /// <param name="data">The selected view. The constructor copies it.</param>
    public DiskVolumeFile(string source, string location, string kind, string volumeName, string path, string view, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(location);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(volumeName);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(view);
        this.Source = source;
        this.Location = location;
        this.Kind = kind;
        this.VolumeName = volumeName;
        this.Path = path;
        this.View = view;
        this.Data = data.ToArray();
    }

    /// <summary>Gets the source kind.</summary>
    /// <value><c>image</c> or <c>drive</c>.</value>
    public string Source { get; }

    /// <summary>Gets the image path or <c>slot:drive</c>.</summary>
    /// <value>The location shown to the caller.</value>
    public string Location { get; }

    /// <summary>Gets the file-system kind.</summary>
    /// <value>The enum name, such as <c>Dos33</c>.</value>
    public string Kind { get; }

    /// <summary>Gets the volume name.</summary>
    /// <value>The name with inverse, flashing, and control characters escaped.</value>
    public string VolumeName { get; }

    /// <summary>Gets the file that was read.</summary>
    /// <value>The path the caller requested.</value>
    public string Path { get; }

    /// <summary>Gets the selected view.</summary>
    /// <value><c>raw</c> or <c>logical</c>.</value>
    public string View { get; }

    /// <summary>Gets the file bytes.</summary>
    /// <value>A copy of the selected view.</value>
    public ReadOnlyMemory<byte> Data { get; }
}