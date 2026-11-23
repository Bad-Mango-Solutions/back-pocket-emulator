// <copyright file="DebugMcpOptionsValidatorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Options;

/// <summary>
/// Tests for sandbox-root option validation.
/// </summary>
public sealed class DebugMcpOptionsValidatorTests
{
    private readonly DebugMcpOptionsValidator validator = new();

    /// <summary>
    /// An empty list is valid and means sandboxed file access is denied.
    /// </summary>
    [Test]
    public void Validate_EmptyList_Succeeds()
    {
        ValidateOptionsResult result = this.validator.Validate(null, new DebugMcpOptions());

        Assert.That(result.Succeeded, Is.True);
    }

    /// <summary>
    /// A relative root is rejected.
    /// </summary>
    [Test]
    public void Validate_RelativeRoot_Fails()
    {
        ValidateOptionsResult result = this.validator.Validate(null, new DebugMcpOptions
        {
            SandboxRoots = ["profiles"],
        });

        Assert.That(result.Failed, Is.True);
        Assert.That(string.Join(" ", result.Failures ?? []), Does.Contain("absolute"));
    }

    /// <summary>
    /// A non-positive file cap is rejected.
    /// </summary>
    [Test]
    public void Validate_NonPositiveFileCap_Fails()
    {
        ValidateOptionsResult result = this.validator.Validate(null, new DebugMcpOptions
        {
            MaxDiskFileBytes = 0,
        });

        Assert.That(result.Failed, Is.True);
        Assert.That(string.Join(" ", result.Failures ?? []), Does.Contain("MaxDiskFileBytes"));
    }

    /// <summary>
    /// An empty entry is rejected.
    /// </summary>
    [Test]
    public void Validate_EmptyEntry_Fails()
    {
        ValidateOptionsResult result = this.validator.Validate(null, new DebugMcpOptions
        {
            SandboxRoots = [" "],
        });

        Assert.That(result.Failed, Is.True);
    }

    /// <summary>
    /// The same directory listed twice is rejected.
    /// </summary>
    [Test]
    public void Validate_DuplicateRoot_Fails()
    {
        string root = Directory.CreateTempSubdirectory("mcp-root-").FullName;
        try
        {
            ValidateOptionsResult result = this.validator.Validate(null, new DebugMcpOptions
            {
                SandboxRoots = [root, root + Path.DirectorySeparatorChar],
            });

            Assert.That(result.Failed, Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}