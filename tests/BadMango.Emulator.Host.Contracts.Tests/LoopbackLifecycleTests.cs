// <copyright file="LoopbackLifecycleTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Stub-only control behavior. The shared power menu lives in <see cref="HostControlContractTests"/>.
/// </summary>
[TestFixture]
public class LoopbackLifecycleTests
{
    /// <summary>
    /// The stub can be told a measured speed that is not the throttle ratio.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task MeasuredSpeed_IsIndependentOfTheThrottle()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop", startPaused: true);
        await session.Control.PowerOnAsync(id);
        await session.Control.SetThrottleAsync(id, HostThrottle.Double);
        ((ILoopbackMachineEdge)session).ReportMeasuredSpeed(id, 0.5);

        HostMachineInfo info = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(HostRunState.Paused));
            Assert.That(info.Throttle, Is.EqualTo(HostThrottle.Double));
            Assert.That(info.EffectiveClockHz, Is.EqualTo(2_040_968));
            Assert.That(info.MeasuredSpeed, Is.EqualTo(0.5));
            Assert.That(info.CpuSummary, Is.EqualTo("65C02"));
        });
    }

    /// <summary>
    /// Attach is idempotent, and memory commands wait until the debugger is attached.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Debugger_AttachMemoryBreakpoint_RoundTrip()
    {
        using LoopbackHostSession session = HostSessions.Open();
        RecordingSink sink = new();
        using IDisposable subscription = session.Control.Subscribe(sink);
        MachineId id = HostSessions.Add(session, "Workshop");

        Assert.Throws<InvalidOperationException>(() => session.Debugger.ReadMemory(id, 0x300, 1));
        await session.Control.AttachDebuggerAsync(id);
        await session.Control.AttachDebuggerAsync(id);
        Assert.That(session.Debugger.IsAttached(id), Is.True);

        session.Debugger.WriteMemory(id, 0x300, new byte[] { 0xA9, 0x01 });
        HostMemoryBlock block = session.Debugger.ReadMemory(id, 0x300, 2);
        IReadOnlyList<HostDisassemblyLine> lines = session.Debugger.Disassemble(id, 0x300, 2);
        session.Debugger.AddBreakpoint(id, 0x300, "loop");
        session.Debugger.AddWatch(id, 0x300, HostWatchAccess.Write, stopOnHit: true, label: "store");
        ((ILoopbackMachineEdge)session).RaiseBreakpointHit(id, 0x300);

        Assert.Multiple(() =>
        {
            Assert.That(block.Data.ToArray(), Is.EqualTo(new byte[] { 0xA9, 0x01 }));
            Assert.That(lines[0].Mnemonic, Is.EqualTo("DB"));
            Assert.That(lines[0].Bytes, Is.EqualTo("A9"));
            Assert.That(session.Debugger.GetBreakpoints(id)[0].Label, Is.EqualTo("loop"));
            Assert.That(session.Debugger.GetWatches(id)[0].Access, Is.EqualTo(HostWatchAccess.Write));
            Assert.That(session.Debugger.GetRegisters(id).ProgramCounter, Is.EqualTo(0));
            Assert.That(sink.Breakpoints, Is.EqualTo(new[] { 0x300u }));
        });

        await session.Control.DetachDebuggerAsync(id);
        Assert.Throws<InvalidOperationException>(() => session.Debugger.GetBreakpoints(id));
    }
}