// <copyright file="ConstructorNullTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp.Tests;

using BadMango.Emulator.Debug.Mcp;

/// <summary>
/// Constructor guards for the MCP host services.
/// </summary>
public sealed class ConstructorNullTests
{
    /// <summary>
    /// A missing session fails with <see cref="ArgumentNullException"/> instead of a later null reference.
    /// </summary>
    [Test]
    public void LifecycleTools_NullSession_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new LifecycleTools(null!, null!, null!, null!, null!));

        Assert.That(exception!.ParamName, Is.EqualTo("session"));
    }

    /// <summary>
    /// A missing debug context fails with <see cref="ArgumentNullException"/>.
    /// </summary>
    [Test]
    public void ProfileLoader_NullContext_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new DebugMcpProfileLoader(null!, null!, null!, null!));

        Assert.That(exception!.ParamName, Is.EqualTo("context"));
    }
}