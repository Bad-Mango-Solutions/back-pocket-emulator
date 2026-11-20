// <copyright file="DebugCommandsTests.LifecycleCommands.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;

using Moq;

/// <content>
/// Unit tests for lifecycle commands (Boot, Pause, Resume, Halt).
/// </content>
public partial class DebugCommandsTests
{
    /// <summary>
    /// Verifies that BootCommand has correct name.
    /// </summary>
    [Test]
    public void BootCommand_HasCorrectName()
    {
        var command = new BootCommand();
        Assert.That(command.Name, Is.EqualTo("boot"));
    }

    /// <summary>
    /// Verifies that BootCommand returns error when no machine is attached.
    /// </summary>
    [Test]
    public void BootCommand_NoMachine_ReturnsError()
    {
        var command = new BootCommand();
        var result = command.Execute(debugContext, []);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No machine"));
    }

    /// <summary>
    /// Verifies that BootCommand succeeds when machine is attached.
    /// </summary>
    [Test]
    public void BootCommand_WithMachine_Succeeds()
    {
        var command = new BootCommand();
        var mockMachine = CreateMockMachine();

        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Booting"));
            Assert.That(result.Message, Does.Contain("modifier keys"));
        });
    }

    /// <summary>
    /// Verifies that PauseCommand has correct name.
    /// </summary>
    [Test]
    public void PauseCommand_HasCorrectName()
    {
        var command = new PauseCommand();
        Assert.That(command.Name, Is.EqualTo("pause"));
    }

    /// <summary>
    /// Verifies that PauseCommand returns error when no machine is attached.
    /// </summary>
    [Test]
    public void PauseCommand_NoMachine_ReturnsError()
    {
        var command = new PauseCommand();
        var result = command.Execute(debugContext, []);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No machine"));
    }

    /// <summary>
    /// Verifies that PauseCommand succeeds when machine is attached.
    /// </summary>
    [Test]
    public void PauseCommand_WithMachine_Succeeds()
    {
        var command = new PauseCommand();
        var mockMachine = CreateMockMachine(MachineState.Running);

        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            mockMachine.Verify(m => m.Pause(), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that ResumeCommand has correct name.
    /// </summary>
    [Test]
    public void ResumeCommand_HasCorrectName()
    {
        var command = new ResumeCommand();
        Assert.That(command.Name, Is.EqualTo("resume"));
    }

    /// <summary>
    /// Verifies that ResumeCommand returns error when no machine is attached.
    /// </summary>
    [Test]
    public void ResumeCommand_NoMachine_ReturnsError()
    {
        var command = new ResumeCommand();
        var result = command.Execute(debugContext, []);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No machine"));
    }

    /// <summary>
    /// Verifies that ResumeCommand returns error when not paused.
    /// </summary>
    [Test]
    public void ResumeCommand_NotPaused_ReturnsError()
    {
        var command = new ResumeCommand();
        var mockMachine = CreateMockMachine(MachineState.Stopped);

        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("not paused"));
        });
    }

    /// <summary>
    /// Verifies that ResumeCommand succeeds when paused.
    /// </summary>
    [Test]
    public void ResumeCommand_WhenPaused_Succeeds()
    {
        var command = new ResumeCommand();
        var mockMachine = CreateMockMachine(MachineState.Paused);

        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            mockMachine.Verify(m => m.ResumeAsync(It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that HaltCommand has correct name.
    /// </summary>
    [Test]
    public void HaltCommand_HasCorrectName()
    {
        var command = new HaltCommand();
        Assert.That(command.Name, Is.EqualTo("halt"));
    }

    /// <summary>
    /// Verifies that HaltCommand returns error when no machine is attached.
    /// </summary>
    [Test]
    public void HaltCommand_NoMachine_ReturnsError()
    {
        var command = new HaltCommand();
        var result = command.Execute(debugContext, []);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No machine"));
    }

    /// <summary>
    /// Verifies that HaltCommand succeeds and calls Machine.Halt.
    /// </summary>
    [Test]
    public void HaltCommand_WithMachine_CallsHalt()
    {
        var command = new HaltCommand();
        var mockMachine = CreateMockMachine();

        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("halted"));
            mockMachine.Verify(m => m.Halt(), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that BootCommand opens video window when autoVideoWindowOpen is true.
    /// </summary>
    [Test]
    public void BootCommand_WithAutoVideoWindowOpen_OpensVideoWindow()
    {
        var mockMachine = CreateMockMachine();
        var mockWindowManager = new Mock<IDebugWindowManager>();
        mockWindowManager.Setup(m => m.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync(true);

        var profile = new MachineProfile
        {
            Name = "test",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoVideoWindowOpen = true,
            },
        };

        var command = new BootCommand(profile, mockWindowManager.Object);
        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Contain("Video window opened"));
            mockWindowManager.Verify(m => m.ShowWindowAsync("Video", mockMachine.Object), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that BootCommand does not open video window when autoVideoWindowOpen is false.
    /// </summary>
    [Test]
    public void BootCommand_WithoutAutoVideoWindowOpen_DoesNotOpenVideoWindow()
    {
        var mockMachine = CreateMockMachine();
        var mockWindowManager = new Mock<IDebugWindowManager>();

        var profile = new MachineProfile
        {
            Name = "test",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoVideoWindowOpen = false,
            },
        };

        var command = new BootCommand(profile, mockWindowManager.Object);
        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Not.Contain("Video window opened"));
            mockWindowManager.Verify(m => m.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that BootCommand does not open video window when window manager is null.
    /// </summary>
    [Test]
    public void BootCommand_WithNullWindowManager_DoesNotOpenVideoWindow()
    {
        var mockMachine = CreateMockMachine();

        var profile = new MachineProfile
        {
            Name = "test",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoVideoWindowOpen = true,
            },
        };

        var command = new BootCommand(profile, null);
        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Not.Contain("Video window opened"));
        });
    }

    /// <summary>
    /// Verifies that BootCommand does not open video window when profile is null.
    /// </summary>
    [Test]
    public void BootCommand_WithNullProfile_DoesNotOpenVideoWindow()
    {
        var mockMachine = CreateMockMachine();
        var mockWindowManager = new Mock<IDebugWindowManager>();

        var command = new BootCommand(null, mockWindowManager.Object);
        var contextWithMachine = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, bus, disassembler);
        contextWithMachine.AttachMachine(mockMachine.Object);

        var result = command.Execute(contextWithMachine, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Does.Not.Contain("Video window opened"));
            mockWindowManager.Verify(m => m.ShowWindowAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that StopCommand no longer has 'halt' as an alias.
    /// </summary>
    [Test]
    public void StopCommand_DoesNotHaveHaltAlias()
    {
        var command = new StopCommand();
        Assert.That(command.Aliases, Does.Not.Contain("halt"));
    }

    /// <summary>
    /// Verifies that BootCommand has correct aliases.
    /// </summary>
    [Test]
    public void BootCommand_HasCorrectAliases()
    {
        var command = new BootCommand();
        Assert.That(command.Aliases, Does.Contain("startup"));
    }

    /// <summary>
    /// Verifies that BootCommand help text mentions profile autoStart.
    /// </summary>
    [Test]
    public void BootCommand_DetailedDescription_MentionsAutoStart()
    {
        var command = new BootCommand();
        Assert.That(command.DetailedDescription, Does.Contain("autoStart"));
    }

    /// <summary>
    /// Verifies that profile auto-start powers on when enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AutoStartProfileAsync_AutoStartTrue_BootsMachine()
    {
        var mockMachine = new Mock<IMachine>();
        var state = MachineState.Off;
        mockMachine.Setup(m => m.State).Returns(() => state);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>()))
            .Callback(() => state = MachineState.Running)
            .Returns(Task.CompletedTask);

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoStart = true,
            },
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.True);
            Assert.That(mockMachine.Object.State, Is.EqualTo(MachineState.Running));
            mockMachine.Verify(m => m.BootAsync(It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that profile auto-start never resets a machine that is already on.
    /// </summary>
    /// <param name="state">The machine state when auto-start runs.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [TestCase(MachineState.Running)]
    [TestCase(MachineState.Paused)]
    [TestCase(MachineState.Stopped)]
    public async Task AutoStartProfileAsync_MachineNotOff_DoesNotBootMachine(MachineState state)
    {
        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.State).Returns(state);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoStart = true,
            },
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.False);
            mockMachine.Verify(m => m.BootAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that a boot failure during profile auto-start is reported instead of thrown.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AutoStartProfileAsync_BootThrows_ReturnsFalseWithoutThrowing()
    {
        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.State).Returns(MachineState.Off);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boot failed"));

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoStart = true,
            },
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.That(started, Is.False);
    }

    /// <summary>
    /// Verifies that profile auto-start does not power on when disabled.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AutoStartProfileAsync_AutoStartFalse_DoesNotBootMachine()
    {
        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.State).Returns(MachineState.Off);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                AutoStart = false,
            },
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.False);
            Assert.That(mockMachine.Object.State, Is.EqualTo(MachineState.Off));
            mockMachine.Verify(m => m.BootAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that profile auto-start does not power on when boot section omits autoStart.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AutoStartProfileAsync_MissingAutoStartKey_DoesNotBootMachine()
    {
        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.State).Returns(MachineState.Off);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
            Boot = new BootProfile
            {
                StartupSlot = 6,
            },
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.False);
            Assert.That(mockMachine.Object.State, Is.EqualTo(MachineState.Off));
            mockMachine.Verify(m => m.BootAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that profile auto-start does not power on when no boot section exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AutoStartProfileAsync_NoBootSection_DoesNotBootMachine()
    {
        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.State).Returns(MachineState.Off);
        mockMachine.Setup(m => m.BootAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var profile = new MachineProfile
        {
            Name = "test-profile",
            Cpu = new CpuProfileSection { Type = "65C02" },
            Memory = new MemoryProfileSection(),
        };

        bool started = await BootCommand.AutoStartProfileAsync(mockMachine.Object, profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.False);
            Assert.That(mockMachine.Object.State, Is.EqualTo(MachineState.Off));
            mockMachine.Verify(m => m.BootAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that PauseCommand has correct aliases.
    /// </summary>
    [Test]
    public void PauseCommand_HasCorrectAliases()
    {
        var command = new PauseCommand();
        Assert.That(command.Aliases, Does.Contain("suspend"));
        Assert.That(command.Aliases, Does.Contain("freeze"));
    }

    /// <summary>
    /// Verifies that ResumeCommand has correct aliases.
    /// </summary>
    [Test]
    public void ResumeCommand_HasCorrectAliases()
    {
        var command = new ResumeCommand();
        Assert.That(command.Aliases, Does.Contain("continue"));
        Assert.That(command.Aliases, Does.Contain("cont"));
    }

    private static Mock<IMachine> CreateMockMachine(MachineState initialState = MachineState.Stopped)
    {
        var mock = new Mock<IMachine>();
        var state = initialState;

        mock.Setup(m => m.State).Returns(() => state);
        mock.Setup(m => m.Pause()).Callback(() => state = MachineState.Paused);
        mock.Setup(m => m.Halt()).Callback(() => state = MachineState.Stopped);
        mock.Setup(m => m.BootAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(m => m.ResumeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return mock;
    }
}