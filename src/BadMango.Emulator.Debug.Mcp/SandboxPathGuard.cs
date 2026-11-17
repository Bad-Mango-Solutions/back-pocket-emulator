// <copyright file="SandboxPathGuard.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Options;

/// <summary>
/// Decides whether a filesystem path is inside a configured sandbox root.
/// </summary>
/// <remarks>
/// Part 3 calls <see cref="EnsureAllowed"/> before mounting a disk or reading a file out of an image.
/// An empty root list allows nothing.
/// </remarks>
public sealed class SandboxPathGuard
{
    private readonly IReadOnlyList<string> roots;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxPathGuard"/> class.
    /// </summary>
    /// <param name="options">The bound host options.</param>
    public SandboxPathGuard(IOptions<DebugMcpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.roots = options.Value.SandboxRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Normalize)
            .Distinct(PathComparer())
            .ToArray();
    }

    /// <summary>
    /// Gets the normalized sandbox roots.
    /// </summary>
    public IReadOnlyList<string> Roots => this.roots;

    /// <summary>
    /// Normalizes a rooted path for prefix checks.
    /// </summary>
    /// <param name="path">The absolute path to normalize.</param>
    /// <returns>The full path without a trailing separator, except for a filesystem root.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, PathComparison()) && full.Length > 0 && full[^1] is '/' or '\\')
        {
            full = full.TrimEnd('/', '\\');
        }

        return full;
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> is the root or a file under one configured root.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is confined to a sandbox root.</returns>
    public bool IsAllowed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || this.roots.Count == 0)
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        foreach (string root in this.roots)
        {
            if (IsUnderRoot(full, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Throws when <paramref name="path"/> is outside every sandbox root.
    /// </summary>
    /// <param name="path">The path that must be confined.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="path"/> is outside the sandbox.</exception>
    public void EnsureAllowed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!this.IsAllowed(path))
        {
            throw new InvalidOperationException($"Path is outside the configured sandbox roots: {path}");
        }
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        if (fullPath.Equals(root, PathComparison()))
        {
            return true;
        }

        string prefix = root.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison());
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IEqualityComparer<string> PathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}