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
}