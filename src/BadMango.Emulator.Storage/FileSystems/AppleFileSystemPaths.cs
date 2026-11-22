// <copyright file="AppleFileSystemPaths.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Volume-relative path rules shared by the DOS 3.3, ProDOS, and Pascal readers.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="null"/>, empty, or slash-only path is the volume root.
/// <see cref="AppleFileSystemPathStyle.Hierarchical"/> is ProDOS. A leading slash makes
/// the path absolute: when the first segment equals <see cref="IAppleFileSystem.VolumeName"/>
/// under <see cref="StringComparison.OrdinalIgnoreCase"/>, that segment is removed.
/// <c>/SAMPLE/NOTE</c> and <c>NOTE</c> are the same file when the volume is <c>SAMPLE</c>.
/// A path without a leading slash does not strip a volume prefix.
/// <c>.</c>, <c>..</c>, and empty segments (<c>a//b</c>) are invalid. Readers return
/// <see cref="FileAccessStatus.InvalidPath"/> for those. They do not throw.
/// </para>
/// <para>
/// <see cref="AppleFileSystemPathStyle.Flat"/> is DOS 3.3 and Pascal. One optional
/// leading <c>/</c> is removed and everything after it is one file name, so <c>A/B</c>
/// and <c>/A/B</c> both name the file <c>A/B</c>. An absolute path that is only the
/// volume name (<c>/254</c> on volume <c>254</c>) is the root. A relative name equal to
/// the volume number stays a file. <c>.</c> and <c>/</c> inside that name are legal.
/// Backslashes are ordinary characters in both styles.
/// </para>
/// </remarks>
public static class AppleFileSystemPaths
{
    /// <summary>The path separator readers accept and emit.</summary>
    public const char Separator = '/';

    /// <summary>
    /// Splits a user path into volume-relative segments.
    /// </summary>
    /// <param name="path">The path. <see langword="null"/>, empty, and slash-only paths are the root.</param>
    /// <param name="volumeName">The volume name used to strip one absolute prefix.</param>
    /// <param name="style">Hierarchical for ProDOS. Flat for DOS 3.3 and Pascal.</param>
    /// <param name="segments">The segments. Empty when the path is the volume root. A flat name is one segment.</param>
    /// <param name="reason">Empty on success. A sentence when the path is illegal.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is the root or a legal relative path.</returns>
    /// <exception cref="ArgumentException">If <paramref name="volumeName"/> is empty.</exception>
    public static bool TryParse(
        string? path,
        string volumeName,
        AppleFileSystemPathStyle style,
        out IReadOnlyList<string> segments,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(volumeName);
        if (IsRoot(path))
        {
            segments = Array.Empty<string>();
            reason = string.Empty;
            return true;
        }

        if (path!.Contains('\0'))
        {
            segments = Array.Empty<string>();
            reason = "The path contains a NUL character.";
            return false;
        }

        if (style == AppleFileSystemPathStyle.Flat)
        {
            return TryParseFlat(path, volumeName, out segments, out reason);
        }

        if (style != AppleFileSystemPathStyle.Hierarchical)
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }

        var absolute = path[0] == Separator;
        var parts = path.Split(Separator);
        var start = 0;
        var end = parts.Length;
        if (absolute && end > 0 && parts[0].Length == 0)
        {
            start = 1;
        }

        if (end > start && parts[end - 1].Length == 0)
        {
            end--;
        }

        var parsed = new List<string>(end - start);
        for (var i = start; i < end; i++)
        {
            var segment = parts[i];
            if (segment.Length == 0)
            {
                segments = Array.Empty<string>();
                reason = "The path contains an empty segment.";
                return false;
            }

            if (segment is "." or "..")
            {
                segments = Array.Empty<string>();
                reason = "The path contains '.' or '..'.";
                return false;
            }

            parsed.Add(segment);
        }

        if (absolute && parsed.Count > 0 && parsed[0].Equals(volumeName, StringComparison.OrdinalIgnoreCase))
        {
            parsed.RemoveAt(0);
        }

        segments = parsed;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Joins volume-relative segments with <see cref="Separator"/>.
    /// </summary>
    /// <param name="segments">Segments already known to be legal. An empty list is the root.</param>
    /// <returns>The path readers store on <see cref="FileEntry.Path"/>. The root is an empty string.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="segments"/> is <see langword="null"/>.</exception>
    public static string Join(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return string.Join(Separator, segments);
    }

    /// <summary>
    /// Appends a name to a directory path.
    /// </summary>
    /// <param name="directoryPath">A volume-relative directory path. Empty is the root.</param>
    /// <param name="name">The file or subdirectory name.</param>
    /// <returns><paramref name="name"/> at the root, otherwise <c>directory/name</c>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="directoryPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="name"/> is empty.</exception>
    public static string Child(string directoryPath, string name)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (directoryPath.Length == 0)
        {
            return name;
        }

        return string.Concat(directoryPath, Separator, name);
    }

    private static bool TryParseFlat(string path, string volumeName, out IReadOnlyList<string> segments, out string reason)
    {
        var absolute = path[0] == Separator;
        var name = absolute ? path[1..] : path;
        if (name.Length == 0)
        {
            segments = Array.Empty<string>();
            reason = string.Empty;
            return true;
        }

        if (absolute && name.Equals(volumeName, StringComparison.OrdinalIgnoreCase))
        {
            segments = Array.Empty<string>();
            reason = string.Empty;
            return true;
        }

        segments = new[] { name };
        reason = string.Empty;
        return true;
    }

    private static bool IsRoot(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        foreach (var ch in path)
        {
            if (ch != Separator)
            {
                return false;
            }
        }

        return true;
    }
}