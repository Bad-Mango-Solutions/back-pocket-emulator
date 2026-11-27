// <copyright file="InProcessHostConcurrencyTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using BadMango.Emulator.Host;

/// <summary>
/// Disk reads and profile validation stay consistent with power-on replacement.
/// </summary>
[TestFixture]
[CancelAfter(20000)]
public class InProcessHostConcurrencyTests
{
    /// <summary>
    /// A disk list waits while power-on is replacing the machine, then reads the machine that replaced it.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task ListDisks_WaitsWhilePowerOnReplacesTheMachine()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Replace", startPaused: true);
        session.Control.SetInstanceOverlay(id, """{"cpu":{}}""");
        using ManualResetEventSlim blocked = new(false);
        using ManualResetEventSlim release = new(false);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ReplaceBlocked = blocked;
        session.BlockDuringReplace = release;
        session.DiskReadEntered = entered;
        Task? powerOn = null;
        try
        {
            powerOn = session.Control.PowerOnAsync(id);
            Assert.That(blocked.Wait(TimeSpan.FromSeconds(5)), Is.True, "Power-on did not reach the machine replacement.");

            Task<IReadOnlyList<HostDiskController>> list = session.Control.ListDisksAsync(id);
            Assert.That(entered.Task.Wait(TimeSpan.FromSeconds(5)), Is.True, "The disk list did not wait for the lifecycle.");
            Assert.That(list.IsCompleted, Is.False);

            release.Set();
            await powerOn;
            IReadOnlyList<HostDiskController> disks = await list;
            Assert.Multiple(() =>
            {
                Assert.That(disks, Is.Not.Null);
                Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Paused));
            });
        }
        finally
        {
            release.Set();
            if (powerOn is not null)
            {
                await powerOn;
            }

            session.BlockDuringReplace = null;
            session.ReplaceBlocked = null;
            session.DiskReadEntered = null;
        }
    }

    /// <summary>
    /// Power-on validates the overlay that is current when validation finishes, not a value replaced mid-check.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task PowerOn_ValidatesTheOverlayThatIsCurrent()
    {
        using InProcessHostSession session = Open();
        MachineId id = Add(session, "Overlay", startPaused: true);
        session.Control.SetInstanceOverlay(id, """{"cpu":{"type":"z80"}}""");
        session.BeforeProfileValidation = () => session.Control.SetInstanceOverlay(id, null);

        await session.Control.PowerOnAsync(id);

        HostMachineInfo info = session.Control.GetMachineInfo(id);
        Assert.Multiple(() =>
        {
            Assert.That(info.State, Is.EqualTo(HostRunState.Paused));
            Assert.That(info.ProfileOverlayJson, Is.Null);
        });
    }

    private static InProcessHostSession Open()
    {
        return new InProcessHostTransport().Connect(HostApi.Version);
    }

    private static MachineId Add(InProcessHostSession session, string name, bool startPaused)
    {
        BootProfile.Ensure(session.Control);
        return session.Control.CreateInstance(new HostInstanceSpec(name, BootProfile.Id, startPaused, CpuSummary: "65C02"));
    }
}