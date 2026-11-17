// <copyright file="DebugHostArgumentsTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Tests;

/// <summary>
/// Host flags are removed before the generic host parses the command line.
/// </summary>
[TestFixture]
public sealed class DebugHostArgumentsTests
{
    /// <summary>
    /// <c>--headless</c> is recognized and is not left for later parsing to consume the next token.
    /// </summary>
    [Test]
    public void WithoutHostOptions_RemovesHeadlessWithoutEatingTheNextArgument()
    {
        string[] args = ["--HEADLESS", "--dump-assemblies", "--profile", "pocket2e", "--exec=regs;exit", "version", "--headless=no"];

        string[] hostArgs = DebugHostArguments.WithoutHostOptions(args);

        Assert.Multiple(() =>
        {
            Assert.That(DebugHostArguments.Has(args, DebugHostArguments.HeadlessOption), Is.True);
            Assert.That(hostArgs, Is.EqualTo(new[] { "version", "--headless=no" }));
        });
    }
}