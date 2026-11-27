// <copyright file="InProcessHostControlTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using System.Collections.Concurrent;

using BadMango.Emulator.Host;

/// <summary>
/// Control-plane tests that run against the real in-process host transport.
/// </summary>
[TestFixture]
public class InProcessHostControlTests
{
    /// <summary>
    /// A client that asks for another version is refused.
    /// </summary>
    [Test]
    public void Connect_RejectsADifferentVersion()
    {
        InProcessHostTransport transport = new();

        HostApiVersionException thrown = Assert.Throws<HostApiVersionException>(() => transport.Connect(HostApi.Version + 1))!;

        Assert.Multiple(() =>
        {
            Assert.That(thrown.Requested, Is.EqualTo(HostApi.Version + 1));
            Assert.That(thrown.Supported, Is.EqualTo(HostApi.Version));
            Assert.That(transport.Name, Is.EqualTo("loopback"));
        });
    }

    /// <summary>
    /// Several instances run on one host and are addressed independently by machine id.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task SeveralInstances_AreIndependent()
    {
        using InProcessHostSession session = CreateHost();
        UpsertBootRomProfile(session);

        MachineId first = session.Control.CreateInstance(new HostInstanceSpec("One", "boot-rom-test", StartPaused: false));
        MachineId second = session.Control.CreateInstance(new HostInstanceSpec("Two", "boot-rom-test", StartPaused: true));

        await session.Control.PowerOnAsync(first);

        HostMachineInfo firstInfo = session.Control.GetMachineInfo(first);
        HostMachineInfo secondInfo = session.Control.GetMachineInfo(second);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.ListInstances(), Has.Count.EqualTo(2));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(firstInfo.State, Is.EqualTo(HostRunState.Running));
            Assert.That(secondInfo.State, Is.EqualTo(HostRunState.Off));
            Assert.That(firstInfo.PowerCapabilities, Is.Null);
            Assert.That(firstInfo.NominalClockHz, Is.EqualTo(1_020_484));
            Assert.That(firstInfo.EffectiveClockHz, Is.EqualTo(1_020_484));
            Assert.That(firstInfo.MeasuredSpeed, Is.Not.Null);
        });

        await session.Control.PowerOffAsync(first);
        session.Control.RemoveInstance(first);

        Assert.Multiple(() =>
        {
            Assert.That(session.Control.ListInstances(), Has.Count.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => session.Control.GetMachineInfo(first));
        });
    }

    /// <summary>
    /// With no power record the conservative set allows hard reset and power off, and refuses sleep and shut down.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task MissingPowerRecord_UsesConservativePowerMenu()
    {
        using InProcessHostSession session = CreateHost();
        UpsertBootRomProfile(session);
        MachineId id = session.Control.CreateInstance(new HostInstanceSpec("Workshop", "boot-rom-test", StartPaused: true));

        await session.Control.PowerOnAsync(id);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Paused));

        await session.Control.ResumeAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.SleepAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.ShutDownAsync(id));
        await session.Control.HardResetAsync(id);

        await session.Control.SetThrottleAsync(id, HostThrottle.Double);
        HostMachineInfo doubled = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(doubled.Throttle, Is.EqualTo(HostThrottle.Double));
            Assert.That(doubled.EffectiveClockHz, Is.EqualTo(2_040_968));
            Assert.That(doubled.MeasuredSpeed, Is.EqualTo(2.0).Within(0.01));
        });

        await session.Control.SetThrottleAsync(id, HostThrottle.Max);
        Assert.That(session.Control.GetMachineInfo(id).EffectiveClockHz, Is.EqualTo(0));

        await session.Control.PowerOffAsync(id);
        Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Off));
    }

    /// <summary>
    /// State and reset notices are raised without blocking machine control calls.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Subscribe_RaisesStateAndResetNotices()
    {
        using InProcessHostSession session = CreateHost();
        UpsertBootRomProfile(session);
        MachineId id = session.Control.CreateInstance(new HostInstanceSpec("Workshop", "boot-rom-test", StartPaused: false));

        NoticeSink sink = new();
        using IDisposable subscription = session.Control.Subscribe(sink);

        await session.Control.PowerOnAsync(id);
        await session.Control.PauseAsync(id);
        await session.Control.ResumeAsync(id);
        await session.Control.ResetAsync(id);
        await session.Control.HardResetAsync(id);
        await session.Control.PowerOffAsync(id);

        Assert.That(await WaitForAsync(() => sink.States.Count >= 4), Is.True);
        Assert.That(await WaitForAsync(() => sink.HardResets.Count >= 2), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(sink.States, Does.Contain(HostRunState.Running));
            Assert.That(sink.States, Does.Contain(HostRunState.Paused));
            Assert.That(sink.States, Does.Contain(HostRunState.Off));
            Assert.That(sink.HardResets.Count(value => value), Is.EqualTo(1));
            Assert.That(sink.HardResets.Count(value => !value), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Unsupported planes keep the session shape stable with slice-scoped exceptions.
    /// </summary>
    [Test]
    public void UnsupportedPlanes_ThrowSliceNotSupported()
    {
        using InProcessHostSession session = new InProcessHostTransport().Connect(HostApi.Version);

        NotSupportedException presentation = Assert.Throws<NotSupportedException>(
            () => session.Presentation.GetScreenText(new MachineId("machine")))!;
        NotSupportedException input = Assert.Throws<NotSupportedException>(
            () => session.Input.GetInputState(new MachineId("machine")))!;
        NotSupportedException debugger = Assert.Throws<NotSupportedException>(
            () => session.Debugger.IsAttached(new MachineId("machine")))!;

        Assert.Multiple(() =>
        {
            Assert.That(presentation.Message, Does.Contain("#810"));
            Assert.That(input.Message, Does.Contain("#809"));
            Assert.That(debugger.Message, Does.Contain("#813"));
        });
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 40; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static InProcessHostSession CreateHost()
    {
        return new InProcessHostTransport().Connect(HostApi.Version);
    }

    private static void UpsertBootRomProfile(InProcessHostSession session)
    {
        const string ProfileJson = """
                                   {
                                       "name": "boot-rom-test",
                                       "displayName": "Boot ROM Test",
                                       "description": "Minimal 65C02 profile that boots from embedded boot.rom.",
                                       "cpu": { "type": "65C02", "clockSpeed": 1020484 },
                                       "addressSpace": 16,
                                       "memory": {
                                           "rom-images": [
                                               {
                                                   "name": "boot-rom",
                                                   "source": "embedded://BadMango.Emulator.Devices/BadMango.Emulator.Devices.Resources.boot.rom",
                                                   "size": "0x0800",
                                                   "required": true,
                                                   "on_verification_fail": "stop"
                                               }
                                           ],
                                           "physical": [
                                               { "name": "main-ram", "size": "0xF000", "fill": "0x00" },
                                               {
                                                   "name": "system-rom",
                                                   "size": "0x1000",
                                                   "fill": "0x00",
                                                   "sources": [{ "type": "rom-image", "name": "boot", "rom-image": "boot-rom", "offset": "0x0800" }]
                                               }
                                           ],
                                           "regions": [
                                               {
                                                   "name": "ram",
                                                   "type": "ram",
                                                   "start": "0x0000",
                                                   "size": "0xF000",
                                                   "permissions": "rwx",
                                                   "source": "main-ram",
                                                   "source-offset": "0x0000"
                                               },
                                               {
                                                   "name": "rom",
                                                   "type": "rom",
                                                   "start": "0xF000",
                                                   "size": "0x1000",
                                                   "permissions": "rx",
                                                   "source": "system-rom",
                                                   "source-offset": "0x0000"
                                               }
                                           ]
                                       }
                                   }
                                   """;
        session.Control.UpsertProfile(new HostProfileDocument("boot-rom-test", "Boot ROM Test", ProfileJson));
    }

    private sealed class NoticeSink : IHostNotificationSink
    {
        public ConcurrentQueue<HostRunState> States { get; } = new();

        public ConcurrentQueue<bool> HardResets { get; } = new();

        void IHostNotificationSink.OnStateChanged(HostStateNotice notice)
        {
            States.Enqueue(notice.State);
        }

        void IHostNotificationSink.OnReset(HostResetNotice notice)
        {
            HardResets.Enqueue(notice.Hard);
        }
    }
}