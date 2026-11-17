// <copyright file="Cpu65C02AbortReactionTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// 65C02 reactions to <see cref="SignalLine.Abort"/>.
/// </summary>
[TestFixture]
public class Cpu65C02AbortReactionTests
{
    /// <summary>
    /// The default reaction is <see cref="AbortReaction.NoOp"/>. An unmapped fetch still sets
    /// <see cref="HaltState.Stp"/> and does not call <see cref="CpuBase.RequestStop"/>.
    /// </summary>
    [Test]
    public void DefaultNoOp_UnmappedFetch_SetsStp_AndDoesNotRequestStop()
    {
        (Cpu65C02 cpu, MainBus bus, SignalBus signals) = CreateUnmappedCpu();
        Assert.That(cpu.AbortReaction, Is.EqualTo(AbortReaction.NoOp));

        cpu.SetPC(0x2000);
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.IsStopRequested, Is.False);
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.True);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
        });

        cpu.Step();

        Assert.That(cpu.IsStopRequested, Is.False, "NoOp ignores ABORT at the next boundary.");
    }

    /// <summary>
    /// <see cref="AbortReaction.HostBreak"/> does not set <see cref="HaltState.Stp"/>. The next
    /// instruction boundary calls <see cref="CpuBase.RequestStop"/> and does not change registers.
    /// </summary>
    [Test]
    public void HostBreak_NextBoundary_RequestsStop_WithoutStpOrRegisterChanges()
    {
        (Cpu65C02 cpu, _, _) = CreateUnmappedCpu();
        cpu.AbortReaction = AbortReaction.HostBreak;
        cpu.SetPC(0x2000);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
            Assert.That(cpu.IsStopRequested, Is.False);
        });

        ushort pc = cpu.Registers.PC.GetWord();
        uint a = cpu.Registers.A.GetDWord();
        uint x = cpu.Registers.X.GetDWord();
        uint y = cpu.Registers.Y.GetDWord();
        uint sp = cpu.Registers.SP.GetDWord();
        byte status = (byte)cpu.Registers.P;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.IsStopRequested, Is.True);
            Assert.That(cpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
            Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(pc));
            Assert.That(cpu.Registers.A.GetDWord(), Is.EqualTo(a));
            Assert.That(cpu.Registers.X.GetDWord(), Is.EqualTo(x));
            Assert.That(cpu.Registers.Y.GetDWord(), Is.EqualTo(y));
            Assert.That(cpu.Registers.SP.GetDWord(), Is.EqualTo(sp));
            Assert.That((byte)cpu.Registers.P, Is.EqualTo(status));
        });
    }

    /// <summary>
    /// STP still sets <see cref="HaltState.Stp"/> when the abort reaction is host-break and the
    /// line is not asserted.
    /// </summary>
    [Test]
    public void HostBreak_StpInstruction_StillSetsStp()
    {
        var raw = new byte[0x1000];
        raw[0] = 0xDB;
        var signals = new SignalBus();
        var bus = new MainBus(16, signals: signals);
        bus.MapPage(1, new(0, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));
        var cpu = new Cpu65C02(new EventContext(new Scheduler(), signals, bus))
        {
            AbortReaction = AbortReaction.HostBreak,
        };
        cpu.SetPC(0x1000);

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.IsStopRequested, Is.False);
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
        });
    }

    /// <summary>
    /// <see cref="Cpu65C02.Reset"/> leaves the host's abort reaction alone.
    /// </summary>
    [Test]
    public void CpuReset_LeavesAbortReaction()
    {
        (Cpu65C02 cpu, _, _) = CreateUnmappedCpu();
        cpu.AbortReaction = AbortReaction.HostBreak;

        cpu.Reset();

        Assert.That(cpu.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
    }

    /// <summary>
    /// <see cref="MachineBuilder.Build"/> passes its signal bus into <see cref="MainBus"/>.
    /// </summary>
    [Test]
    public void MachineBuilder_PassesSignalBus()
    {
        var machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(ctx => new Cpu65C02(ctx))
            .Build();
        var cpu = (Cpu65C02)machine.Cpu;
        cpu.AbortReaction = AbortReaction.HostBreak;

        machine.Bus.TryRead8(new BusAccess(
            Address: 0x2000,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataRead,
            SourceId: 0,
            Cycle: 1,
            Flags: AccessFlags.None));

        Assert.Multiple(() =>
        {
            Assert.That(machine.Signals.IsAsserted(SignalLine.Abort), Is.True);
            Assert.That(machine.Bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
        });

        machine.Reset();

        Assert.That(cpu.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
    }

    private static (Cpu65C02 Cpu, MainBus Bus, SignalBus Signals) CreateUnmappedCpu()
    {
        var signals = new SignalBus();
        var bus = new MainBus(16, signals: signals);
        var cpu = new Cpu65C02(new EventContext(new Scheduler(), signals, bus));
        return (cpu, bus, signals);
    }
}