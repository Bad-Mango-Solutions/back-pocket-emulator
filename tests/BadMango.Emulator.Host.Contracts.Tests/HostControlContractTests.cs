// <copyright file="HostControlContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using System.Collections.Concurrent;

/// <summary>
/// Control-plane rules shared by the stub loopback and the real in-process host.
/// </summary>
public abstract class HostControlContractTests
{
    /// <summary>
    /// A soft-power machine walks the power menu and reports Shutting down before Off.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task SoftPower_Lifecycle_ReportsShuttingDownThenOff()
    {
        using IHostSession session = Open();
        OrderedSink sink = new();
        using IDisposable subscription = session.Control.Subscribe(sink);
        MachineId id = Add(session, "Workshop", new PowerCapabilities(SoftPower: true, Sleep: true, HardReset: true));

        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));

        await session.Control.PowerOnAsync(id);
        await session.Control.PauseAsync(id);
        await session.Control.ResumeAsync(id);
        await session.Control.SleepAsync(id);
        await session.Control.ResumeAsync(id);
        await session.Control.ResetAsync(id);
        await session.Control.HardResetAsync(id);
        await session.Control.ShutDownAsync(id);

        HostRunState[] states = await WaitForAsync(sink.States, 7);
        bool[] resets = await WaitForAsync(sink.Resets, 2);
        HostMachineInfo info = session.Control.GetMachineInfo(id);

        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(HostRunState.Off));
            Assert.That(info.PowerCapabilities, Is.EqualTo(new PowerCapabilities(true, true, true)));
            Assert.That(states, Is.EqualTo(new[]
            {
                HostRunState.Running,
                HostRunState.Paused,
                HostRunState.Running,
                HostRunState.Sleeping,
                HostRunState.Running,
                HostRunState.ShuttingDown,
                HostRunState.Off,
            }));
            Assert.That(resets, Is.EqualTo(new[] { false, true }));
        });
    }

    /// <summary>
    /// With no power record the conservative set allows Power Off and Hard Reset, and refuses Sleep and Shut Down.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task MissingPowerRecord_UsesConservativeSet()
    {
        using IHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        Assert.That(session.Control.GetMachineInfo(id).PowerCapabilities, Is.Null);

        await session.Control.PowerOnAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.SleepAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.ShutDownAsync(id));
        await session.Control.HardResetAsync(id);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));

        await session.Control.PowerOffAsync(id);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
    }

    /// <summary>
    /// Soft power refuses Power Off. A machine that opts out of Hard Reset and Sleep refuses those actions.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Capabilities_RejectActionsTheRecordOmits()
    {
        using IHostSession session = Open();
        MachineId id = Add(session, "Workshop", new PowerCapabilities(SoftPower: true, Sleep: false, HardReset: false));
        await session.Control.PowerOnAsync(id);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.PowerOffAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.HardResetAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.SleepAsync(id));
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));

        await session.Control.ShutDownAsync(id);
    }

    /// <summary>
    /// Start paused lands in Paused, and the throttle scales the nominal clock without inventing a measurement.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task StartPaused_ScalesTheClock_WithoutMeasuringWhilePaused()
    {
        using IHostSession session = Open();
        MachineId id = Add(session, "Workshop", startPaused: true);
        await session.Control.PowerOnAsync(id);
        await session.Control.SetThrottleAsync(id, HostThrottle.Double);

        HostMachineInfo info = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(HostRunState.Paused));
            Assert.That(info.NominalClockHz, Is.EqualTo(1_020_484));
            Assert.That(info.Throttle, Is.EqualTo(HostThrottle.Double));
            Assert.That(info.EffectiveClockHz, Is.EqualTo(2_040_968));
            Assert.That(info.MeasuredSpeed, Is.Null);
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
        using IHostSession session = Open();
        MachineId id = Add(session, "Workshop", new PowerCapabilities(true, true, true));
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
        using IHostSession session = Open();
        MachineId id = Add(session, "Workshop");
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        Task powerOn = session.Control.PowerOnAsync(id, source.Token);
        Assert.That(powerOn.IsCanceled, Is.True);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await powerOn);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
    }

    /// <summary>
    /// Opens one host session.
    /// </summary>
    /// <returns>The session.</returns>
    protected abstract IHostSession Open();

    /// <summary>
    /// Stores a profile and adds a machine.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The instance name.</param>
    /// <param name="power">The power record, or <see langword="null"/> for the conservative set.</param>
    /// <param name="startPaused">Whether Power On leaves the machine paused.</param>
    /// <returns>The machine id.</returns>
    protected abstract MachineId Add(IHostSession session, string name, PowerCapabilities? power = null, bool startPaused = false);

    private static async Task<T[]> WaitForAsync<T>(Func<T[]> read, int count)
    {
        T[] snapshot = read();
        for (int attempt = 0; snapshot.Length < count && attempt < 100; attempt++)
        {
            await Task.Delay(20);
            snapshot = read();
        }

        return snapshot;
    }

    private sealed class OrderedSink : IHostNotificationSink
    {
        private readonly ConcurrentQueue<HostRunState> states = new();
        private readonly ConcurrentQueue<bool> resets = new();

        public HostRunState[] States()
        {
            return states.ToArray();
        }

        public bool[] Resets()
        {
            return resets.ToArray();
        }

        void IHostNotificationSink.OnStateChanged(HostStateNotice notice)
        {
            states.Enqueue(notice.State);
        }

        void IHostNotificationSink.OnReset(HostResetNotice notice)
        {
            resets.Enqueue(notice.Hard);
        }
    }
}