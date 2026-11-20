// <copyright file="DebugCommandsTests.Run.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

using Moq;

/// <content>
/// Unit tests for <see cref="RunCommand"/>.
/// </content>
public partial class DebugCommandsTests
{
    /// <summary>
    /// Verifies that RunCommand has correct name.
    /// </summary>
    [Test]
    public void RunCommand_HasCorrectName()
    {
        var command = new RunCommand();
        Assert.That(command.Name, Is.EqualTo("run"));
    }

    /// <summary>
    /// Verifies that RunCommand has correct aliases.
    /// </summary>
    [Test]
    public void RunCommand_HasCorrectAliases()
    {
        var command = new RunCommand();
        Assert.That(command.Aliases, Is.EquivalentTo(new[] { "g", "go" }));
    }

    /// <summary>
    /// Verifies that RunCommand runs until CPU halts.
    /// </summary>
    [Test]
    public void RunCommand_RunsUntilHalt()
    {
        // Write a few NOPs then STP
        WriteByte(bus, 0x1000, 0xEA); // NOP
        WriteByte(bus, 0x1001, 0xEA); // NOP
        WriteByte(bus, 0x1002, 0xDB); // STP
        cpu.Reset();

        var command = new RunCommand();
        var result = command.Execute(debugContext, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("CPU halted"));
            Assert.That(cpu.Halted, Is.True);
        });
    }

    /// <summary>
    /// Verifies that RunCommand respects instruction limit.
    /// </summary>
    [Test]
    public void RunCommand_RespectsInstructionLimit()
    {
        // Write infinite NOP loop
        WriteByte(bus, 0x1000, 0xEA); // NOP
        WriteByte(bus, 0x1001, 0x4C); // JMP $1000
        WriteByte(bus, 0x1002, 0x00);
        WriteByte(bus, 0x1003, 0x10);
        cpu.Reset();

        var command = new RunCommand();
        var result = command.Execute(debugContext, ["10"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("instruction limit"));
        });
    }

    /// <summary>
    /// Verifies that RunCommand steps again after the scheduler dispatches while the CPU is in WAI.
    /// </summary>
    /// <remarks>
    /// The first mocked step enters WAI the way <c>CpuBase</c> does (<see cref="HaltState.Wai"/>),
    /// and the later step clears it. The pre-step halt guard must not stop the loop before that
    /// second step, which is where a real machine services the interrupt.
    /// </remarks>
    [Test]
    public void RunCommand_AdvancesSchedulerDuringWai_WhenMachineAttached()
    {
        WriteByte(bus, 0x1000, 0xCB); // WAI
        cpu.Reset();

        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(s => s.JumpToNextEventAndDispatch()).Returns(true);

        var mockMachine = new Mock<IMachine>();
        mockMachine.SetupGet(m => m.Cpu).Returns(cpu);
        mockMachine.SetupGet(m => m.Bus).Returns(bus);
        mockMachine.SetupGet(m => m.Scheduler).Returns(mockScheduler.Object);
        mockMachine.SetupGet(m => m.State).Returns(MachineState.Stopped);
        mockMachine.SetupSequence(m => m.Step())
            .Returns(() =>
            {
                cpu.HaltReason = HaltState.Wai;
                return new CpuStepResult(CpuRunState.WaitingForInterrupt, Cycle.Zero);
            })
            .Returns(() =>
            {
                cpu.HaltReason = HaltState.None;
                return new CpuStepResult(CpuRunState.Halted, Cycle.Zero);
            });
        debugContext.AttachMachine(mockMachine.Object);

        var command = new RunCommand();
        var result = command.Execute(debugContext, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            mockMachine.Verify(m => m.Step(), Times.Exactly(2));
            mockScheduler.Verify(s => s.JumpToNextEventAndDispatch(), Times.Once);
        });
    }

    /// <summary>
    /// Verifies that a scheduler-dispatched IRQ wakes a real CPU from WAI and execution continues.
    /// </summary>
    /// <remarks>
    /// Uses a real machine, CPU, and scheduler. The scheduled callback asserts IRQ; the next
    /// machine step must service that interrupt, clear WAI, and run the handler through STP.
    /// </remarks>
    [Test]
    public void RunCommand_ResumesPastWai_WhenSchedulerDispatchesInterrupt()
    {
        const uint IrqMarkerAddress = 0x3000;
        var ram = new PhysicalMemory(64 * 1024, "wai-ram");
        var machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpu(CpuFamily.Cpu65C02)
            .WithCpuFactory(ctx => new Cpu65C02(ctx))
            .MapRegion(0x0000, 64 * 1024, new RamTarget(ram.Slice(0, 64 * 1024)), RegionTag.Ram, PagePerms.All)
            .Build();
        machine.PowerOnAsync().GetAwaiter().GetResult();

        debugContext.AttachMachine(machine);

        var machineBus = machine.Bus;
        WriteByte(machineBus, 0x1000, 0x58); // CLI
        WriteByte(machineBus, 0x1001, 0xCB); // WAI
        WriteByte(machineBus, 0x1002, 0xDB); // STP, reached only if WAI wakes without vectoring
        WriteByte(machineBus, 0x2000, 0xA9); // LDA #$42
        WriteByte(machineBus, 0x2001, 0x42);
        WriteByte(machineBus, 0x2002, 0x8D); // STA $3000
        WriteByte(machineBus, 0x2003, 0x00);
        WriteByte(machineBus, 0x2004, 0x30);
        WriteByte(machineBus, 0x2005, 0xDB); // STP
        WriteWord(machineBus, 0xFFFC, 0x1000); // Reset vector
        WriteWord(machineBus, 0xFFFE, 0x2000); // IRQ vector
        machine.Cpu.Reset();

        // Far enough ahead that CLI and WAI cannot dispatch it themselves.
        machine.Scheduler.ScheduleAfter(
            1000ul,
            ScheduledEventKind.InterruptLineChange,
            0,
            ctx => ctx.Signals.Assert(SignalLine.IRQ, 1, ctx.Now),
            "wai-irq");

        var command = new RunCommand();
        var result = command.Execute(debugContext, ["100"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(machine.Cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(machine.Cpu.GetPC(), Is.EqualTo(0x2006u));
            Assert.That(ReadByte(machineBus, IrqMarkerAddress), Is.EqualTo(0x42));
            Assert.That(outputWriter.ToString(), Does.Contain("CPU halted (Stp)"));
        });
    }

    /// <summary>
    /// Verifies that RunCommand exits cleanly when CPU-only execution enters WAI with no machine scheduler.
    /// </summary>
    [Test]
    public void RunCommand_CpuOnlyWai_StopsWithoutSpinning()
    {
        WriteByte(bus, 0x1000, 0xCB); // WAI
        cpu.Reset();

        var command = new RunCommand();
        var result = command.Execute(debugContext, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("CPU waiting for interrupt"));
        });
    }
}