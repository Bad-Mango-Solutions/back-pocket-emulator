// <copyright file="HostLibraryPaths.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Library folders for the two storage categories. A path with no <c>library://</c> scheme is a file path and is not checked here.
/// </summary>
public static class HostLibraryPaths
{
    /// <summary>
    /// The folder for removable media.
    /// </summary>
    public const string RemovablePrefix = "library://disks/";

    /// <summary>
    /// The folder for fixed storage.
    /// </summary>
    public const string FixedPrefix = "library://drives/";

    private const string Scheme = "library://";

    /// <summary>
    /// Rejects a library path that is not under <see cref="RemovablePrefix"/>.
    /// </summary>
    /// <param name="path">The path the client supplied.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="path"/> is a library path outside <c>disks</c>.</exception>
    public static void RequireRemovableMedia(string path)
    {
        if (!IsInFolder(path, "disks"))
        {
            throw new InvalidOperationException(
                $"Removable media uses {RemovablePrefix}. '{path}' is not in that folder.");
        }
    }

    /// <summary>
    /// Rejects a library path that is not under <see cref="FixedPrefix"/>.
    /// </summary>
    /// <param name="path">The path the client supplied.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="path"/> is a library path outside <c>drives</c>.</exception>
    public static void RequireFixedStorage(string path)
    {
        if (!IsInFolder(path, "drives"))
        {
            throw new InvalidOperationException(
                $"Fixed storage uses {FixedPrefix}. '{path}' is not in that folder.");
        }
    }

    private static bool IsInFolder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string rest = path[Scheme.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash >= rest.Length - 1)
        {
            return false;
        }

        return rest[..slash].Equals(folder, StringComparison.OrdinalIgnoreCase);
    }
}