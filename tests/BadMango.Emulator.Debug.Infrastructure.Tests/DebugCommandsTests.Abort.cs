// <copyright file="DebugCommandsTests.Abort.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Emulation.Cpu;

/// <content>
/// The <c>abort</c> command and the session reaction.
/// </content>
public partial class DebugCommandsTests
{
    /// <summary>
    /// <c>abort</c> with no argument prints the default reaction and does not pulse the line.
    /// </summary>
    [Test]
    public void AbortCommand_NoArgument_PrintsNoOp_AndDoesNotPulse()
    {
        var signals = (SignalBus)cpu.EventContext.Signals;
        int changes = 0;
        signals.SignalChanged += (_, _, _, _) => changes++;

        var result = new AbortCommand(null, DebugOutputRenderer.PlainText).Execute(debugContext, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(outputWriter.ToString().Trim(), Is.EqualTo("Abort reaction: NoOp"));
            Assert.That(cpu.AbortReaction, Is.EqualTo(AbortReaction.NoOp));
            Assert.That(signals.IsAsserted(SignalLine.Abort), Is.False);
            Assert.That(changes, Is.Zero);
        });
    }

    /// <summary>
    /// <c>abort hostbreak</c> selects host-break. The next instruction boundary calls
    /// <see cref="CpuBase.RequestStop"/> and does not set <see cref="HaltState.Stp"/>.
    /// <c>abort noop</c> restores the default.
    /// </summary>
    [Test]
    public void AbortHostBreak_NextBoundaryRequestsStop_NoopRestoresStp()
    {
        (Cpu65C02 localCpu, MainBus localBus, DebugContext ctx, StringWriter output) = CreateHole();
        var command = new AbortCommand(null, DebugOutputRenderer.PlainText);

        var hostBreak = command.Execute(ctx, ["hostbreak"]);
        localCpu.SetPC(0x2000);
        localCpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(hostBreak.Success, Is.True, hostBreak.Message);
            Assert.That(localCpu.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
            Assert.That(localCpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
            Assert.That(localCpu.IsStopRequested, Is.False);
        });

        localCpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(localCpu.IsStopRequested, Is.True);
            Assert.That(localCpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
        });

        output.GetStringBuilder().Clear();
        var restored = command.Execute(ctx, ["noop"]);
        localBus.ClearFaultStatus();
        localCpu.HaltReason = HaltState.None;
        localCpu.ClearStopRequest();
        localCpu.SetPC(0x3000);
        localCpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(restored.Success, Is.True, restored.Message);
            Assert.That(localCpu.AbortReaction, Is.EqualTo(AbortReaction.NoOp));
            Assert.That(output.ToString().Trim(), Is.EqualTo("Abort reaction: NoOp"));
            Assert.That(localCpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(localCpu.IsStopRequested, Is.False);
        });
    }

    /// <summary>
    /// Setting <see cref="IDebugSession.AbortReaction"/> directly selects the same host-break
    /// behavior as the command.
    /// </summary>
    [Test]
    public void SessionAbortReaction_HostBreak_NextBoundaryRequestsStop()
    {
        (Cpu65C02 localCpu, _, DebugContext ctx, _) = CreateHole();
        var session = new MachineDebugSession(ctx);
        session.AbortReaction = AbortReaction.HostBreak;
        localCpu.SetPC(0x2000);

        localCpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(localCpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
            Assert.That(localCpu.IsStopRequested, Is.False);
        });

        ushort pc = localCpu.Registers.PC.GetWord();
        uint sp = localCpu.Registers.SP.GetDWord();
        localCpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(localCpu.IsStopRequested, Is.True);
            Assert.That(localCpu.HaltReason, Is.Not.EqualTo(HaltState.Stp));
            Assert.That(localCpu.Registers.PC.GetWord(), Is.EqualTo(pc));
            Assert.That(localCpu.Registers.SP.GetDWord(), Is.EqualTo(sp));
            Assert.That(session.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
        });
    }

    /// <summary>
    /// A machine reset leaves the host's abort reaction alone.
    /// </summary>
    [Test]
    public void MachineReset_LeavesAbortReaction()
    {
        var machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(static ctx => new Cpu65C02(ctx))
            .Build();
        var localCpu = (Cpu65C02)machine.Cpu;
        var ctx = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter());
        ctx.AttachMachine(machine);
        var session = new MachineDebugSession(ctx);
        session.AbortReaction = AbortReaction.HostBreak;

        machine.Reset();
        machine.Reset(hardReset: true);

        Assert.Multiple(() =>
        {
            Assert.That(localCpu.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
            Assert.That(session.AbortReaction, Is.EqualTo(AbortReaction.HostBreak));
        });
    }

    /// <summary>
    /// <c>--json</c> on <c>abort</c> is one <c>emudbg/v1</c> envelope.
    /// </summary>
    [Test]
    public void AbortCommand_Json_IsOneEnvelope()
    {
        AssertGolden(new AbortCommand(null, JsonRenderer()), [], """
{
  "schema": "emudbg/v1",
  "payload": {
    "reaction": "NoOp"
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
        Assert.That(outputWriter.ToString().Split("emudbg/v1").Length - 1, Is.EqualTo(1));
    }

    /// <summary>
    /// <c>abort hostbreak --json</c> is one envelope naming HostBreak.
    /// </summary>
    [Test]
    public void AbortCommand_HostBreakJson_IsOneEnvelope()
    {
        AssertGolden(new AbortCommand(null, JsonRenderer()), ["hostbreak"], """
{
  "schema": "emudbg/v1",
  "payload": {
    "reaction": "HostBreak"
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
        using JsonDocument json = JsonDocument.Parse(outputWriter.ToString());
        Assert.That(json.RootElement.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
        Assert.That(outputWriter.ToString().Split("emudbg/v1").Length - 1, Is.EqualTo(1));
    }

    /// <summary>
    /// Setting <see cref="AbortReaction.HostBreak"/> on a session whose CPU is not a
    /// <see cref="Cpu65C02"/> throws <see cref="InvalidOperationException"/>. The getter
    /// returns <see cref="AbortReaction.NoOp"/>.
    /// </summary>
    [Test]
    public void SessionHostBreak_OnNon65C02_Throws()
    {
        var hole = new MainBus(16);
        var other = new Cpu65816(new EventContext(new Scheduler(), new SignalBus(), hole));
        var ctx = new DebugContext(dispatcher, new StringWriter(), new StringWriter(), other, hole, null);
        var session = new MachineDebugSession(ctx);

        Assert.That(session.AbortReaction, Is.EqualTo(AbortReaction.NoOp));
        Assert.DoesNotThrow(() => session.AbortReaction = AbortReaction.NoOp);
        Assert.Throws<InvalidOperationException>(() => session.AbortReaction = AbortReaction.HostBreak);

        var command = new AbortCommand(session, DebugOutputRenderer.PlainText).Execute(ctx, ["HOSTBREAK"]);
        Assert.That(command.Success, Is.False);
    }

    /// <summary>
    /// An unknown reaction is rejected and does not change the setting.
    /// </summary>
    [Test]
    public void AbortCommand_UnknownReaction_IsAnError()
    {
        var result = new AbortCommand(null, DebugOutputRenderer.PlainText).Execute(debugContext, ["sometimes"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(cpu.AbortReaction, Is.EqualTo(AbortReaction.NoOp));
        });
    }

    private static (Cpu65C02 Cpu, MainBus Bus, DebugContext Context, StringWriter Output) CreateHole()
    {
        var signals = new SignalBus();
        var hole = new MainBus(16, signals: signals);
        var localCpu = new Cpu65C02(new EventContext(new Scheduler(), signals, hole));
        var output = new StringWriter();
        var ctx = new DebugContext(new CommandDispatcher(), output, new StringWriter(), localCpu, hole, null);
        return (localCpu, hole, ctx, output);
    }
}