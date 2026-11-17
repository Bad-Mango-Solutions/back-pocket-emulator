// <copyright file="DebugCommandsTests.RunUntil.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

/// <content>
/// Unit tests for <see cref="RunUntilCommand"/>.
/// </content>
public partial class DebugCommandsTests
{
    /// <summary>
    /// Verifies that run-until requires a machine-backed debug context.
    /// </summary>
    [Test]
    public void RunUntilCommand_ReturnsErrorWithoutMachine()
    {
        var command = new RunUntilCommand();
        var result = command.Execute(debugContext, ["$1002"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("No machine is attached"));
        });
    }

    /// <summary>
    /// Verifies that run-until mem validates required arguments.
    /// </summary>
    [Test]
    public void RunUntilCommand_MemWithoutArgs_ReturnsError()
    {
        var command = new RunUntilCommand();

        var result = command.Execute(debugContext, ["mem", "$2000"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Usage: run-until mem"));
        });
    }
}