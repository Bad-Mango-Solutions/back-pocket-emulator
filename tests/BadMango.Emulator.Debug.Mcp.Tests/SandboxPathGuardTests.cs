// <copyright file="SandboxPathGuardTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Options;

/// <summary>
/// Tests for sandbox root confinement.
/// </summary>
public sealed class SandboxPathGuardTests
{
    /// <summary>
    /// A file inside a root is allowed, and a directory that only shares the root's prefix is not.
    /// </summary>
    [Test]
    public void IsAllowed_ConfinesPathsToTheRoot()
    {
        string root = Directory.CreateTempSubdirectory("mcp-sandbox-").FullName;
        try
        {
            var guard = new SandboxPathGuard(Options.Create(new DebugMcpOptions
            {
                SandboxRoots = [root],
            }));

            string inside = Path.Join(root, "disk.dsk");
            string sibling = root.TrimEnd(Path.DirectorySeparatorChar) + "-other";

            Assert.Multiple(() =>
            {
                Assert.That(guard.IsAllowed(inside), Is.True);
                Assert.That(guard.IsAllowed(root), Is.True);
                Assert.That(guard.IsAllowed(sibling), Is.False);
                Assert.That(guard.IsAllowed(string.Empty), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// An empty root list allows no path.
    /// </summary>
    [Test]
    public void IsAllowed_EmptyRoots_DeniesEveryPath()
    {
        var guard = new SandboxPathGuard(Options.Create(new DebugMcpOptions()));

        Assert.That(guard.IsAllowed(Path.GetTempPath()), Is.False);
    }

    /// <summary>
    /// EnsureAllowed rejects a path outside the roots.
    /// </summary>
    [Test]
    public void EnsureAllowed_OutsideRoot_Throws()
    {
        string root = Directory.CreateTempSubdirectory("mcp-sandbox-").FullName;
        try
        {
            var guard = new SandboxPathGuard(Options.Create(new DebugMcpOptions
            {
                SandboxRoots = [root],
            }));

            Assert.That(
                () => guard.EnsureAllowed(Path.Join(Path.GetTempPath(), "outside.bin")),
                Throws.InvalidOperationException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A symbolic link that sits inside a root and points outside it is denied.
    /// </summary>
    [Test]
    public void IsAllowed_SymlinkToOutsideFile_IsDenied()
    {
        string root = Directory.CreateTempSubdirectory("mcp-sandbox-").FullName;
        string outside = Directory.CreateTempSubdirectory("mcp-outside-").FullName;
        string outsideFile = Path.Join(outside, "secret.txt");
        string link = Path.Join(root, "evil.json");
        File.WriteAllText(outsideFile, "secret");
        try
        {
            CreateLinkOrIgnore(() => File.CreateSymbolicLink(link, outsideFile));
            var guard = Guard(root);

            Assert.Multiple(() =>
            {
                Assert.That(guard.IsAllowed(link), Is.False);
                Assert.That(() => guard.EnsureAllowed(link), Throws.InvalidOperationException);
            });
        }
        finally
        {
            DeleteLink(link);
            Directory.Delete(outside, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A directory link inside a root does not make files behind that link allowed.
    /// </summary>
    [Test]
    public void IsAllowed_SymlinkDirectoryToOutside_IsDenied()
    {
        string root = Directory.CreateTempSubdirectory("mcp-sandbox-").FullName;
        string outside = Directory.CreateTempSubdirectory("mcp-outside-").FullName;
        string link = Path.Join(root, "linked");
        string hidden = Path.Join(link, "note.txt");
        File.WriteAllText(Path.Join(outside, "note.txt"), "hidden");
        try
        {
            CreateLinkOrIgnore(() => Directory.CreateSymbolicLink(link, outside));
            var guard = Guard(root);

            Assert.That(guard.IsAllowed(hidden), Is.False);
        }
        finally
        {
            DeleteLink(link);
            Directory.Delete(outside, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A symbolic link whose final target stays inside the root is allowed.
    /// </summary>
    [Test]
    public void IsAllowed_SymlinkInsideRoot_IsAllowed()
    {
        string root = Directory.CreateTempSubdirectory("mcp-sandbox-").FullName;
        string inside = Path.Join(root, "ok.txt");
        string alias = Path.Join(root, "alias.txt");
        File.WriteAllText(inside, "ok");
        try
        {
            CreateLinkOrIgnore(() => File.CreateSymbolicLink(alias, inside));
            var guard = Guard(root);

            Assert.That(guard.IsAllowed(alias), Is.True);
        }
        finally
        {
            DeleteLink(alias);
            Directory.Delete(root, recursive: true);
        }
    }

    private static SandboxPathGuard Guard(string root)
    {
        return new SandboxPathGuard(Options.Create(new DebugMcpOptions
        {
            SandboxRoots = [root],
        }));
    }

    private static void CreateLinkOrIgnore(Action create)
    {
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are not available: {ex.Message}");
        }
    }

    private static void DeleteLink(string link)
    {
        if (!File.Exists(link) && !Directory.Exists(link))
        {
            return;
        }

        if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0)
        {
            File.Delete(link);
            return;
        }

        if (File.Exists(link))
        {
            File.Delete(link);
        }
    }
}