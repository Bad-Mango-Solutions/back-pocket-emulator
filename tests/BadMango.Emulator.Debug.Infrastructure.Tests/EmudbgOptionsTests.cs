// <copyright file="EmudbgOptionsTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="EmudbgOptions"/>.
/// </summary>
[TestFixture]
public sealed class EmudbgOptionsTests
{
    /// <summary>
    /// Verifies default values.
    /// </summary>
    [Test]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        EmudbgOptions options = EmudbgOptions.Parse([]);

        Assert.Multiple(() =>
        {
            Assert.That(options.Profile, Is.Null);
            Assert.That(options.ExecCommands, Is.Null);
            Assert.That(options.ScriptFile, Is.Null);
            Assert.That(options.NoBanner, Is.False);
            Assert.That(options.ShowHelp, Is.False);
            Assert.That(options.JsonOutput, Is.False);
            Assert.That(options.AgentMode, Is.False);
            Assert.That(options.MaxCycles, Is.Null);
            Assert.That(options.Seed, Is.Null);
        });
    }

    /// <summary>
    /// Verifies mixed argument parsing.
    /// </summary>
    [Test]
    public void Parse_MixedArgs_ParsesAll()
    {
        EmudbgOptions options = EmudbgOptions.Parse([
            "--profile", "pocket2e",
            "--exec", "boot;regs",
            "--no-banner",
            "--JSON",
            "--max-cycles", "1234",
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(options.Profile, Is.EqualTo("pocket2e"));
            Assert.That(options.ExecCommands, Is.EqualTo("boot;regs"));
            Assert.That(options.NoBanner, Is.True);
            Assert.That(options.JsonOutput, Is.True);
            Assert.That(options.MaxCycles, Is.EqualTo(1234UL));
            Assert.That(options.Seed, Is.Null);
        });
    }

    /// <summary>
    /// Verifies that a supplied seed is rejected because the machine has no RNG hook.
    /// </summary>
    [Test]
    public void TryParse_Seed_IsUsageError()
    {
        bool parsed = EmudbgOptions.TryParse(["--seed", "42"], out _, out string? error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain("no seeded random-number source"));
        });
    }

    /// <summary>
    /// Verifies missing values, bad numbers, and unknown flags.
    /// </summary>
    [Test]
    public void TryParse_InvalidArguments_AreUsageErrors()
    {
        AssertUsage(["--exec"], "missing value");
        AssertUsage(["--file"], "missing value");
        AssertUsage(["--max-cycles", "nope"], "invalid --max-cycles");
        AssertUsage(["--seed", "nope"], "invalid --seed");
        AssertUsage(["--bogus"], "unknown option");
    }

    /// <summary>
    /// Verifies --agent implied flags.
    /// </summary>
    [Test]
    public void Parse_AgentMode_EnablesHeadlessAndJson()
    {
        EmudbgOptions options = EmudbgOptions.Parse(["--agent"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.AgentMode, Is.True);
            Assert.That(options.Headless, Is.True);
            Assert.That(options.JsonOutput, Is.True);
        });
    }

    /// <summary>
    /// Verifies command splitting from --exec.
    /// </summary>
    [Test]
    public void GetExecCommandLines_SplitsSemicolonsAndNewlines()
    {
        EmudbgOptions options = new(ExecCommands: "boot;regs\\nstep 2");

        Assert.That(options.GetExecCommandLines(), Is.EqualTo(new[] { "boot", "regs", "step 2" }));
    }

    /// <summary>
    /// Verifies non-interactive detection.
    /// </summary>
    [Test]
    public void IsNonInteractive_TrueWhenExecOrFileOrAgent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new EmudbgOptions(ExecCommands: "regs").IsNonInteractive, Is.True);
            Assert.That(new EmudbgOptions(ScriptFile: "commands.txt").IsNonInteractive, Is.True);
            Assert.That(new EmudbgOptions(AgentMode: true).IsNonInteractive, Is.True);
            Assert.That(new EmudbgOptions().IsNonInteractive, Is.False);
        });
    }

    private static void AssertUsage(string[] args, string expected)
    {
        bool parsed = EmudbgOptions.TryParse(args, out _, out string? error);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(error, Does.Contain(expected));
        });
    }
}