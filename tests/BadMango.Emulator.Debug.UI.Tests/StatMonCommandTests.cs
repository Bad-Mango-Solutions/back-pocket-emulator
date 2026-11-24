// <copyright file="StatMonCommandTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.UI.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Unit tests for the <see cref="StatMonCommand"/> class.
/// </summary>
[TestFixture]
public class StatMonCommandTests
{
    /// <summary>
    /// Verifies that the command name is 'statmon'.
    /// </summary>
    [Test]
    public void Name_ReturnsStatmon()
    {
        var command = new StatMonCommand();

        Assert.That(command.Name, Is.EqualTo("statmon"));
    }

    /// <summary>
    /// Verifies that the command has a description.
    /// </summary>
    [Test]
    public void Description_IsNotEmpty()
    {
        var command = new StatMonCommand();

        Assert.That(command.Description, Is.Not.Empty);
    }

    /// <summary>
    /// Verifies that Execute returns an error when no window manager is provided.
    /// </summary>
    [Test]
    public void Execute_WithoutWindowManager_ReturnsError()
    {
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        var context = CreateTestContext(outputWriter, errorWriter);
        var command = new StatMonCommand();

        var result = command.Execute(context, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("window manager"));
        });
    }

    /// <summary>
    /// Verifies that Execute calls ShowWindowAsync when window manager is available.
    /// </summary>
    [Test]
    public void Execute_WithWindowManager_CallsShowWindowAsync()
    {
        var windowManager = new Mock<IDebugWindowManager>();
        windowManager.Setup(w => w.IsAvaloniaRunning).Returns(true);
        windowManager.Setup(w => w.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object?>())).ReturnsAsync(true);

        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        var context = CreateTestContext(outputWriter, errorWriter);
        var command = new StatMonCommand(windowManager.Object);

        var result = command.Execute(context, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Opening"));
            windowManager.Verify(w => w.ShowWindowAsync("StatusMonitor", It.IsAny<object?>()), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that Synopsis returns a non-empty string.
    /// </summary>
    [Test]
    public void Synopsis_ReturnsNonEmptyString()
    {
        var command = new StatMonCommand();

        Assert.That(command.Synopsis, Is.Not.Empty);
    }

    /// <summary>
    /// Verifies that DetailedDescription returns a non-empty string.
    /// </summary>
    [Test]
    public void DetailedDescription_ReturnsNonEmptyString()
    {
        var command = new StatMonCommand();

        Assert.That(command.DetailedDescription, Is.Not.Empty);
    }

    /// <summary>
    /// Verifies that SeeAlso includes related commands.
    /// </summary>
    [Test]
    public void SeeAlso_IncludesRelatedCommands()
    {
        var command = new StatMonCommand();

        Assert.Multiple(() =>
        {
            Assert.That(command.SeeAlso, Does.Contain("regs"));
            Assert.That(command.SeeAlso, Does.Contain("about"));
        });
    }

    /// <summary>
    /// Verifies that Execute does not block when showing window.
    /// </summary>
    [Test]
    public void Execute_DoesNotBlock_WhenShowingWindow()
    {
        var tcs = new TaskCompletionSource<bool>();
        var windowManager = new Mock<IDebugWindowManager>();
        windowManager.Setup(w => w.IsAvaloniaRunning).Returns(true);

        // Return a task that never completes to verify we don't await it
        windowManager.Setup(w => w.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object?>())).Returns(tcs.Task);

        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        var context = CreateTestContext(outputWriter, errorWriter);
        var command = new StatMonCommand(windowManager.Object);

        // Execute should return immediately without blocking
        var result = command.Execute(context, []);

        Assert.That(result.Success, Is.True);

        // Complete the task to clean up
        tcs.SetResult(true);
    }

    /// <summary>
    /// Text-mode statmon prints the guest OS and boot stage before the window opens.
    /// </summary>
    [Test]
    public void Execute_PrintsGuestOsLine()
    {
        var guestOs = new GuestOsInfo(GuestOsId.ProDos8, "ProDOS 8", "ProDOS 8 2.4.x", "kversion", null);
        var output = ExecuteWithGuestOs(guestOs, GuestOsBootStage.Ready, withWindowManager: true);

        Assert.Multiple(() =>
        {
            Assert.That(output.Result.Success, Is.True);
            Assert.That(output.Result.Message, Does.Contain("Opening"));
            Assert.That(output.Text, Does.Contain("OS: ProDOS 8 2.4.x     Stage: Ready"));
        });
    }

    /// <summary>
    /// A custom boot prints the media-neutral loader text.
    /// </summary>
    [Test]
    public void Execute_CustomBoot_PrintsCustomLoader()
    {
        var guestOs = new GuestOsInfo(GuestOsId.None, "None", string.Empty, "unrecognized loader", null);
        var output = ExecuteWithGuestOs(guestOs, GuestOsBootStage.CustomBoot, withWindowManager: true);

        Assert.That(output.Text, Does.Contain("OS: None (custom loader)     Stage: Custom boot"));
        Assert.That(output.Text, Does.Not.Contain("Disk"));
    }

    /// <summary>
    /// No detector prints "(detection off)" and, without a window manager, still returns the existing error.
    /// </summary>
    [Test]
    public void Execute_WithoutDetectorOrWindow_PrintsDetectionOff()
    {
        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        var context = CreateTestContext(outputWriter, errorWriter);
        var command = new StatMonCommand();

        var result = command.Execute(context, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("window manager"));
            Assert.That(outputWriter.ToString(), Does.Contain("OS: (detection off)"));
        });
    }

    private static (CommandResult Result, string Text) ExecuteWithGuestOs(
        GuestOsInfo guestOs,
        GuestOsBootStage bootStage,
        bool withWindowManager)
    {
        var detector = new Mock<IGuestOsDetector>();
        detector.Setup(d => d.Detect()).Returns(guestOs);

        var monitor = new Mock<IBootSequenceMonitor>();
        monitor.Setup(m => m.Stage).Returns(bootStage);
        monitor.Setup(m => m.Milestones).Returns([]);

        var machine = new Mock<IMachine>();
        machine.Setup(m => m.GetComponent<IGuestOsDetector>()).Returns(detector.Object);
        machine.Setup(m => m.GetComponent<IBootSequenceMonitor>()).Returns(monitor.Object);

        var debugContext = new Mock<IDebugContext>();
        debugContext.Setup(c => c.Machine).Returns(machine.Object);

        IDebugWindowManager? windowManager = null;
        if (withWindowManager)
        {
            var manager = new Mock<IDebugWindowManager>();
            manager.Setup(w => w.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object?>())).ReturnsAsync(true);
            windowManager = manager.Object;
        }

        using var outputWriter = new StringWriter();
        using var errorWriter = new StringWriter();
        var context = CreateTestContext(outputWriter, errorWriter);
        var command = new StatMonCommand(windowManager, debugContext.Object);
        var result = command.Execute(context, []);
        return (result, outputWriter.ToString());
    }

    private static ICommandContext CreateTestContext(StringWriter outputWriter, StringWriter errorWriter)
    {
        var mockDispatcher = new Mock<ICommandDispatcher>();
        var mockContext = new Mock<ICommandContext>();
        mockContext.Setup(c => c.Output).Returns(outputWriter);
        mockContext.Setup(c => c.Error).Returns(errorWriter);
        mockContext.Setup(c => c.Dispatcher).Returns(mockDispatcher.Object);
        return mockContext.Object;
    }
}