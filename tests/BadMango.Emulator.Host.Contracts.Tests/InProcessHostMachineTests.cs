// <copyright file="InProcessHostMachineTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Host;

/// <summary>
/// Proves the in-process host drives a live <c>boot.rom</c> machine.
/// </summary>
/// <remarks>
/// <c>boot.rom</c> clears the decimal flag, sets the stack, enables interrupts, then waits.
/// Its loop is <c>WAI</c> / <c>JMP</c> back, with <c>RTI</c> at the IRQ vector. Holding IRQ,
/// the way a device would, is what makes that loop retire instructions.
/// </remarks>
[TestFixture]
[CancelAfter(20000)]
public class InProcessHostMachineTests
{
    /// <summary>
    /// After Power On the CPU retires instructions, so the cycle count advances.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task PowerOn_ExecutesBootRom()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);

        ulong before = session.ReadSchedulerCycles(id);
        ulong after = await WaitForAdvanceAsync(session, id, before);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Running));
            Assert.That(after, Is.GreaterThan(before));
            Assert.That(session.LiveRunLoops, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Pause stops the cycle counter. Resume starts it again.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Pause_StopsAdvancement_AndResumeContinues()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);
        _ = await WaitForAdvanceAsync(session, id, session.ReadSchedulerCycles(id));

        await session.Control.PauseAsync(id);
        ulong paused = session.ReadSchedulerCycles(id);
        uint pausedPc = session.ReadProgramCounter(id);
        await Task.Delay(250);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Paused));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Paused));
            Assert.That(session.ReadSchedulerCycles(id), Is.EqualTo(paused));
            Assert.That(session.ReadProgramCounter(id), Is.EqualTo(pausedPc));
            Assert.That(session.LiveRunLoops, Is.Zero);
        });

        await session.Control.ResumeAsync(id);
        ulong resumed = await WaitForAdvanceAsync(session, id, paused);
        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(resumed, Is.GreaterThan(paused));
            Assert.That(session.LiveRunLoops, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Start paused powers the machine on and does not run the loop until Resume.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task StartPaused_PowersOnWithoutExecuting()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop", startPaused: true);
        await session.Control.PowerOnAsync(id);

        ulong paused = session.ReadSchedulerCycles(id);
        uint pausedPc = session.ReadProgramCounter(id);
        await Task.Delay(250);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Paused));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Paused));
            Assert.That(session.ReadSchedulerCycles(id), Is.EqualTo(paused));
            Assert.That(session.ReadProgramCounter(id), Is.EqualTo(pausedPc));
            Assert.That(session.LiveRunLoops, Is.Zero);
        });

        HoldIrq(session, id);
        await Task.Delay(200);
        Assert.That(session.ReadSchedulerCycles(id), Is.EqualTo(paused));

        await session.Control.ResumeAsync(id);
        ulong resumed = await WaitForAdvanceAsync(session, id, paused);
        Assert.That(resumed, Is.GreaterThan(paused));
    }

    /// <summary>
    /// Sleep stops execution the way Pause does and reports Sleeping. Resume runs again.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Sleep_StopsExecution_AndResumeContinues()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop", new PowerCapabilities(SoftPower: false, Sleep: true, HardReset: true));
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);
        _ = await WaitForAdvanceAsync(session, id, session.ReadSchedulerCycles(id));

        await session.Control.SleepAsync(id);
        ulong sleeping = session.ReadSchedulerCycles(id);
        await Task.Delay(250);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Sleeping));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Paused));
            Assert.That(session.ReadSchedulerCycles(id), Is.EqualTo(sleeping));
            Assert.That(session.LiveRunLoops, Is.Zero);
        });

        await session.Control.ResumeAsync(id);
        ulong resumed = await WaitForAdvanceAsync(session, id, sleeping);
        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(resumed, Is.GreaterThan(sleeping));
        });
    }

    /// <summary>
    /// Power Off stops the run loop and leaves the machine off.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task PowerOff_StopsTheLoopAndReportsOff()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);
        _ = await WaitForAdvanceAsync(session, id, session.ReadSchedulerCycles(id));
        Assert.That(session.LiveRunLoops, Is.EqualTo(1));

        await session.Control.PowerOffAsync(id);
        ulong off = session.ReadSchedulerCycles(id);
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Off));
            Assert.That(session.ReadSchedulerCycles(id), Is.EqualTo(off));
            Assert.That(session.LiveRunLoops, Is.Zero);
        });
    }

    /// <summary>
    /// Reset and Hard Reset leave the machine running, and it keeps retiring instructions.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task ResetAndHardReset_KeepTheMachineRunning()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);
        _ = await WaitForAdvanceAsync(session, id, session.ReadSchedulerCycles(id));

        await session.Control.ResetAsync(id);
        HoldIrq(session, id);
        ulong afterReset = session.ReadSchedulerCycles(id);
        ulong resetAdvanced = await WaitForAdvanceAsync(session, id, afterReset);
        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Running));
            Assert.That(resetAdvanced, Is.GreaterThan(afterReset));
        });

        await session.Control.HardResetAsync(id);
        HoldIrq(session, id);
        ulong afterHard = session.ReadSchedulerCycles(id);
        ulong hardAdvanced = await WaitForAdvanceAsync(session, id, afterHard);
        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(session.ReadMachineState(id), Is.EqualTo(MachineState.Running));
            Assert.That(hardAdvanced, Is.GreaterThan(afterHard));
            Assert.That(session.LiveRunLoops, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Throttle changes the clock while the machine is off, and Double roughly doubles measured speed while it runs.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Throttle_DoubleRoughlyDoublesMeasuredSpeed()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Workshop");

        await session.Control.SetThrottleAsync(id, HostThrottle.Max);
        HostMachineInfo off = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(off.State, Is.EqualTo(HostRunState.Off));
            Assert.That(off.EffectiveClockHz, Is.Zero);
            Assert.That(off.MeasuredSpeed, Is.Null);
        });

        await session.Control.SetThrottleAsync(id, HostThrottle.Normal);
        await session.Control.PowerOnAsync(id);
        HoldIrq(session, id);

        await Task.Delay(400);
        _ = session.Control.GetMachineInfo(id).MeasuredSpeed;
        await Task.Delay(500);
        double? normal = session.Control.GetMachineInfo(id).MeasuredSpeed;

        await session.Control.SetThrottleAsync(id, HostThrottle.Double);
        Assert.That(session.Control.GetMachineInfo(id).EffectiveClockHz, Is.EqualTo(2_040_968));
        await Task.Delay(500);
        double? doubled = session.Control.GetMachineInfo(id).MeasuredSpeed;

        Assert.Multiple(() =>
        {
            Assert.That(normal, Is.Not.Null);
            Assert.That(doubled, Is.Not.Null);
            Assert.That(normal!.Value, Is.GreaterThan(0.25));
            Assert.That(doubled!.Value, Is.GreaterThan(normal.Value * 1.5), $"normal {normal} doubled {doubled}");
            Assert.That(doubled.Value, Is.LessThan(normal.Value * 3.0), $"normal {normal} doubled {doubled}");
        });

        await session.Control.SetThrottleAsync(id, HostThrottle.Max);
        ulong beforeMax = session.ReadSchedulerCycles(id);
        await Task.Delay(200);
        Assert.Multiple(() =>
        {
            Assert.That(session.Control.GetMachineInfo(id).EffectiveClockHz, Is.Zero);
            Assert.That(session.ReadSchedulerCycles(id), Is.GreaterThan(beforeMax));
        });
    }

    /// <summary>
    /// Two machines run on one host. Pausing one does not stop the other.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task SeveralInstances_RunIndependently()
    {
        using InProcessHostSession session = Open();
        MachineId first = Add(session, "One");
        MachineId second = Add(session, "Two");
        await session.Control.PowerOnAsync(first);
        await session.Control.PowerOnAsync(second);
        HoldIrq(session, first);
        HoldIrq(session, second);

        ulong firstBefore = session.ReadSchedulerCycles(first);
        ulong secondBefore = session.ReadSchedulerCycles(second);
        _ = await WaitForAdvanceAsync(session, first, firstBefore);
        _ = await WaitForAdvanceAsync(session, second, secondBefore);

        await session.Control.PauseAsync(first);
        ulong firstPaused = session.ReadSchedulerCycles(first);
        ulong secondRunning = session.ReadSchedulerCycles(second);
        await Task.Delay(250);
        ulong secondLater = session.ReadSchedulerCycles(second);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.ListInstances(), Has.Count.EqualTo(2));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(session.Control.GetMachineInfo(first).State, Is.EqualTo(HostRunState.Paused));
            Assert.That(session.Control.GetMachineInfo(second).State, Is.EqualTo(HostRunState.Running));
            Assert.That(session.ReadSchedulerCycles(first), Is.EqualTo(firstPaused));
            Assert.That(secondLater, Is.GreaterThan(secondRunning));
            Assert.That(session.LiveRunLoops, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Remove drops an off machine, and Dispose joins every run loop.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task RemoveAndDispose_LeaveNoRunningLoops()
    {
        InProcessHostSession session = Open();
        try
        {
            MachineId kept = Add(session, "Kept");
            MachineId removed = Add(session, "Removed");
            await session.Control.PowerOnAsync(kept);
            await session.Control.PowerOnAsync(removed);
            HoldIrq(session, kept);
            HoldIrq(session, removed);
            _ = await WaitForAdvanceAsync(session, kept, session.ReadSchedulerCycles(kept));
            Assert.That(session.LiveRunLoops, Is.EqualTo(2));

            await session.Control.PowerOffAsync(removed);
            Assert.That(session.LiveRunLoops, Is.EqualTo(1));
            session.Control.RemoveInstance(removed);
            Assert.Multiple(() =>
            {
                Assert.That(session.LiveRunLoops, Is.EqualTo(1));
                Assert.That(session.Control.ListInstances(), Has.Count.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => session.Control.GetMachineInfo(removed));
            });

            Task dispose = Task.Run(session.Dispose);
            Assert.That(dispose.Wait(TimeSpan.FromSeconds(10)), Is.True, "Dispose did not join the run loop.");
            Assert.That(session.LiveRunLoops, Is.Zero);
        }
        finally
        {
            session.Dispose();
        }
    }

    private static void HoldIrq(InProcessHostSession session, MachineId id)
    {
        IMachine machine = session.GetMachine(id);
        machine.Post(() => machine.Signals.Assert(SignalLine.IRQ, deviceId: 1, machine.Now));
    }

    private static async Task<ulong> WaitForAdvanceAsync(InProcessHostSession session, MachineId id, ulong previous)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            ulong cycles = session.ReadSchedulerCycles(id);
            if (cycles > previous)
            {
                return cycles;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Scheduler cycles stayed at {previous}.");
        return previous;
    }

    private static InProcessHostSession Open()
    {
        return new InProcessHostTransport().Connect(HostApi.Version);
    }

    private static MachineId Add(InProcessHostSession session, string name, PowerCapabilities? power = null, bool startPaused = false)
    {
        BootProfile.Ensure(session.Control);
        return session.Control.CreateInstance(new HostInstanceSpec(name, BootProfile.Id, startPaused, power));
    }
}