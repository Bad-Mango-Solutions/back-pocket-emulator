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
/// An empty root list allows nothing. Symbolic links are resolved before the prefix check, including
/// links in parent directories, so a link inside a root cannot point at a file outside it.
/// </remarks>
public sealed class SandboxPathGuard
{
    private const int MaxLinkDepth = 40;

    private readonly IReadOnlyList<string> roots;

    /// <summary>
    /// Initializes a new instance of the <see cref="SandboxPathGuard"/> class.
    /// </summary>
    /// <param name="options">The bound host options.</param>
    public SandboxPathGuard(IOptions<DebugMcpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value);
        this.roots = (options.Value.SandboxRoots ?? [])
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Canonicalize)
            .Distinct(PathComparer())
            .ToArray();
    }

    /// <summary>
    /// Gets the sandbox roots with symbolic links resolved.
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
        return TrimTrailingSeparator(Path.GetFullPath(path));
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
            full = Canonicalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
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

    private static string Canonicalize(string path)
    {
        return Follow(Normalize(path), 0);
    }

    private static string Follow(string full, int depth)
    {
        if (depth > MaxLinkDepth)
        {
            throw new InvalidOperationException("Symbolic link chain is too deep.");
        }

        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        string current = root;
        string relative = full[root.Length..];
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Join(current, segment);
            string? link = TryReadLink(next);
            if (link is null)
            {
                current = next;
                continue;
            }

            string target = Path.IsPathRooted(link)
                ? Normalize(link)
                : Normalize(Path.Join(current, link));
            current = Follow(target, depth + 1);
        }

        return TrimTrailingSeparator(current);
    }

    private static string? TryReadLink(string path)
    {
        string? fileTarget = new FileInfo(path).LinkTarget;
        if (fileTarget is not null)
        {
            return fileTarget;
        }

        return new DirectoryInfo(path).LinkTarget;
    }

    private static string TrimTrailingSeparator(string full)
    {
        string? root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, PathComparison()) && full.Length > 0 && full[^1] is '/' or '\\')
        {
            return full.TrimEnd('/', '\\');
        }

        return full;
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