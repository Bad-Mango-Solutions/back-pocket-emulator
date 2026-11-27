// <copyright file="LoopbackLifecycleTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Power, throttle, profile, and debugger-attach tests for one loopback host.
/// </summary>
[TestFixture]
public class LoopbackLifecycleTests
{
    /// <summary>
    /// A soft-power machine walks the power menu and reports Shutting down before Off.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task SoftPower_Lifecycle_ReportsShuttingDownThenOff()
    {
        using LoopbackHostSession session = HostSessions.Open();
        RecordingSink sink = new();
        using IDisposable subscription = session.Control.Subscribe(sink);
        MachineId id = HostSessions.Add(session, "Workshop", new PowerCapabilities(SoftPower: true, Sleep: true, HardReset: true));

        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));

        await session.Control.PowerOnAsync(id);
        await session.Control.PauseAsync(id);
        await session.Control.ResumeAsync(id);
        await session.Control.SleepAsync(id);
        await session.Control.ResumeAsync(id);
        await session.Control.ResetAsync(id);
        await session.Control.HardResetAsync(id);
        await session.Control.ShutDownAsync(id);

        HostMachineInfo info = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(HostRunState.Off));
            Assert.That(info.PowerCapabilities, Is.EqualTo(new PowerCapabilities(true, true, true)));
            Assert.That(sink.States, Is.EqualTo(new[]
            {
                HostRunState.Running,
                HostRunState.Paused,
                HostRunState.Running,
                HostRunState.Sleeping,
                HostRunState.Running,
                HostRunState.ShuttingDown,
                HostRunState.Off,
            }));
            Assert.That(sink.HardResets, Is.EqualTo(new[] { false, true }));
        });
    }

    /// <summary>
    /// With no power record the conservative set allows Power Off and Hard Reset, and refuses Sleep and Shut Down.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task MissingPowerRecord_UsesConservativeSet()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        HostMachineInfo off = session.Control.GetMachineInfo(id);
        Assert.That(off.PowerCapabilities, Is.Null);

        await session.Control.PowerOnAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.SleepAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.ShutDownAsync(id));
        await session.Control.HardResetAsync(id);
        await session.Control.PowerOffAsync(id);

        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
    }

    /// <summary>
    /// Soft power refuses Power Off. A machine that opts out of Hard Reset refuses that action.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Capabilities_RejectActionsTheRecordOmits()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop", new PowerCapabilities(SoftPower: true, Sleep: false, HardReset: false));
        await session.Control.PowerOnAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.PowerOffAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.HardResetAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.SleepAsync(id));
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
    }

    /// <summary>
    /// Start paused lands in Paused, and the throttle scales the nominal clock without changing measured speed.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task StartPaused_AndThrottle_AreDistinctFromMeasuredSpeed()
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
            Assert.That(info.NominalClockHz, Is.EqualTo(1_020_484));
            Assert.That(info.Throttle, Is.EqualTo(HostThrottle.Double));
            Assert.That(info.EffectiveClockHz, Is.EqualTo(2_040_968));
            Assert.That(info.MeasuredSpeed, Is.EqualTo(0.5));
            Assert.That(info.CpuSummary, Is.EqualTo("65C02"));
        });

        await session.Control.SetThrottleAsync(id, HostThrottle.Max);
        Assert.That(session.Control.GetMachineInfo(id).EffectiveClockHz, Is.EqualTo(0));
    }

    /// <summary>
    /// The overlay can change only while the machine is off. Removing a running machine is refused.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task ProfileOverlay_AndRemove_RequirePowerOff()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop", new PowerCapabilities(true, true, true));
        session.Control.SetInstanceOverlay(id, "{\"cpu\":{}}");
        Assert.That(session.Control.GetMachineInfo(id).ProfileOverlayJson, Is.EqualTo("{\"cpu\":{}}"));

        await session.Control.PowerOnAsync(id);
        Assert.Throws<InvalidOperationException>(() => session.Control.SetInstanceOverlay(id, "{}"));
        Assert.Throws<InvalidOperationException>(() => session.Control.RemoveInstance(id));

        await session.Control.ShutDownAsync(id);
        session.Control.SetInstanceOverlay(id, null);
        session.Control.RemoveInstance(id);
        Assert.Throws<InvalidOperationException>(() => session.Control.GetMachineInfo(id));
        Assert.That(session.Control.ListInstances(), Is.Empty);
    }

    /// <summary>
    /// A canceled command does not change state.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task CanceledPowerOn_LeavesTheMachineOff()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        Task powerOn = session.Control.PowerOnAsync(id, source.Token);
        Assert.That(powerOn.IsCanceled, Is.True);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await powerOn);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
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