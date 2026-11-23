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
/// links in every existing segment, so a link inside a root cannot point at a file outside it.
/// Resolution uses <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/>. A link that cannot be
/// resolved is denied.
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
    public bool IsAllowed(string? path) => this.TryGetAllowedPath(path, out _);

    /// <summary>
    /// Resolves <paramref name="path"/>, including symbolic links, when the final path stays inside a root.
    /// </summary>
    /// <param name="path">The path to confine.</param>
    /// <param name="allowedPath">The resolved path inside a sandbox root when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is confined to a sandbox root.</returns>
    /// <remarks>
    /// <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/> supplies each link target. A link that cannot
    /// be resolved is denied. The returned path is the final target, so a later open does not follow the
    /// original link again.
    /// </remarks>
    public bool TryGetAllowedPath(string? path, out string allowedPath)
    {
        allowedPath = string.Empty;
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
                allowedPath = full;
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
        if (!this.TryGetAllowedPath(path, out _))
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
            if (!TryReadLink(next, out string? linkTarget))
            {
                current = next;
                continue;
            }

            if (linkTarget is null)
            {
                throw new InvalidOperationException("Symbolic link target cannot be resolved.");
            }

            current = Follow(Normalize(linkTarget), depth + 1);
        }

        return TrimTrailingSeparator(current);
    }

    private static bool TryReadLink(string path, out string? target)
    {
        FileSystemInfo info = new FileInfo(path);
        if (info.LinkTarget is null)
        {
            info = new DirectoryInfo(path);
            if (info.LinkTarget is null)
            {
                target = null;
                return false;
            }
        }

        FileSystemInfo? final = info.ResolveLinkTarget(returnFinalTarget: true);
        target = final?.FullName;
        return true;
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