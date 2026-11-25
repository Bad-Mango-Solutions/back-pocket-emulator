// <copyright file="GuestOsBootMonitorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using System.Globalization;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Debug.Mcp;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.GuestOs;

using ModelContextProtocol.Protocol;

using Moq;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Cycle-based boot monitor coverage. The boot card is a test double, not a Disk II, and no disk image is read.
/// </summary>
[TestFixture]
public class GuestOsBootMonitorTests
{
    private const int Slot = 6;
    private const ushort Firmware = 0xC600;

    /// <summary>A profile with the boot log off registers no operating-system traps.</summary>
    [Test]
    public void Disabled_RegistersZeroTraps()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        var registry = Registry(machine);

        Assert.That(registry.IsEmpty, Is.True);
        Assert.That(OperatingSystemTraps(registry), Is.Empty);
    }

    /// <summary>Boot log on, with no firmware card, still registers nothing.</summary>
    [Test]
    public void BootLogWithoutFirmware_RegistersZeroTraps()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: true));

        Assert.That(machine.GetComponent<IBootSequenceMonitor>(), Is.Not.Null);
        Assert.That(Registry(machine).IsEmpty, Is.True);
    }

    /// <summary>Removing a boot card while Off drops its $Cn00 trap. A card with no firmware never had one.</summary>
    [Test]
    public void RemoveBootCardWhileOff_DropsFirmwareTrap()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        machine.InsertSlotCard(5, new BareCard());
        var registry = Registry(machine);
        Assert.That(registry.IsEmpty, Is.True);

        using var output = new StringWriter();
        var command = new OsCommand(Logger.None);
        var result = command.Execute(Context(machine, output), ["on"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.HasTrap(0xC500), Is.False);
            Assert.That(OperatingSystemTraps(registry).All(trap => trap.Operation == TrapOperation.Call), Is.True);
        });

        machine.RemoveSlotCard(Slot);

        Assert.That(registry.HasTrap(Firmware), Is.False);
        Assert.That(OperatingSystemTraps(registry), Is.Empty);
    }

    /// <summary>DOS 3.3 milestones arrive in order, re-entries are counted, and boot traps are gone at Ready.</summary>
    [Test]
    public void Dos33Boot_MilestonesInOrder_ThenTrapsAreGone()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        var detector = machine.GetComponent<IGuestOsDetector>()!;
        GuestOsId? published = null;
        detector.Changed += (_, change) => published = change.Current.Id;
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);

        machine.Step();
        machine.Step();
        Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Boot1));
        Assert.That(monitor.BootSectorReentries, Is.EqualTo(0));
        var countAtBoot1 = monitor.Milestones.Count;
        machine.Cpu.SetPC(0x0801);
        machine.Step();
        Assert.That(monitor.BootSectorReentries, Is.EqualTo(1));
        Assert.That(monitor.Milestones, Has.Count.EqualTo(countAtBoot1));

        machine.Cpu.SetPC(0x0805);
        machine.Step();
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(Kinds(monitor), Is.EqualTo(new[]
            {
                BootMilestoneKind.RomInitEntered,
                BootMilestoneKind.BootDeviceSelected,
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
            }));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("cold"));
            Assert.That(monitor.Milestones[2].Detail, Is.EqualTo("slot 6: Block Device"));
            Assert.That(monitor.Milestones[3].Detail, Is.EqualTo("DOS 3.3"));
            Assert.That(monitor.Milestones[3].Os, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(monitor.Milestones[4].Detail, Is.EqualTo("DOS 3.3 Boot 2 at $B700"));
            Assert.That(monitor.Milestones[4].Pc, Is.EqualTo(0xB700u));
            Assert.That(monitor.Milestones[5].Detail, Is.EqualTo("DOS 3.3 cold start"));
            Assert.That(published, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(detector.Current.Id, Is.EqualTo(GuestOsId.Dos33));
        });
        AssertIncreasingCycles(monitor.Milestones);

        var logged = monitor.Milestones.Count;
        machine.Cpu.SetPC(0x0801);
        machine.Step();
        machine.Cpu.SetPC(0x2000);
        machine.Step();
        var registry = Registry(machine);
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Milestones, Has.Count.EqualTo(logged));
            Assert.That(registry.HasTrap(0x0801), Is.False);
            Assert.That(registry.HasTrap(0x2000), Is.False);
            Assert.That(registry.HasTrap(0xB700), Is.False);
            Assert.That(registry.HasTrap(0x9D84), Is.False);
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.GetTrapInfo(Firmware)!.Value.Scope, Is.EqualTo(TrapScope.Machine));
            Assert.That(registry.HasTrap(Dos33VectorPage.FileManagerTarget), Is.True);
            Assert.That(registry.GetTrapInfo(Dos33VectorPage.FileManagerTarget)!.Value.Scope, Is.EqualTo(TrapScope.GuestOsSession));
            Assert.That(registry.HasTrap(Dos33VectorPage.RwtsTarget), Is.True);
        });

        using var bootOutput = new StringWriter();
        var command = new OsCommand(Logger.None);
        Assert.That(command.Execute(Context(machine, bootOutput), ["boot"]).Success, Is.True);
        Assert.That(bootOutput.ToString(), Does.Contain("DOS 3.3 cold start"));

        using var inspectionOutput = new StringWriter();
        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, inspectionOutput), Logger.None);
        var boot = (DebugEnvelope<BootLogSnapshot>)tools.GetBootLog();
        Assert.That(boot.Payload.Milestones, Has.Count.EqualTo(logged));
        Assert.That(boot.Payload.Stage, Is.EqualTo(nameof(GuestOsBootStage.Ready)));

        machine.Reset();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(monitor.Milestones[^1].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[^1].Detail, Does.Contain("warm"));
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.HasTrap(0x0801), Is.False);
        });
    }

    /// <summary>ProDOS reaches Ready from a non-Disk II card, then the first MLI call is named.</summary>
    [Test]
    public void ProDosBoot_FromBlockDevice_ReachesReady_AndNamesOpen()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        GuestOsSystemCallEventArgs? call = null;
        monitor.SystemCallObserved += (_, seen) => call = seen;
        PlantProDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);

        for (var step = 0; step < 6; step++)
        {
            machine.Step();
        }

        Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
        machine.Cpu.SetPC(0x0300);
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(Kinds(monitor), Is.EqualTo(new[]
            {
                BootMilestoneKind.RomInitEntered,
                BootMilestoneKind.BootDeviceSelected,
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
                BootMilestoneKind.SystemProgramStarted,
                BootMilestoneKind.FirstSystemCall,
            }));
            Assert.That(monitor.Milestones[4].Detail, Is.EqualTo("ProDOS loader"));
            Assert.That(monitor.Milestones[4].Pc, Is.EqualTo(0x2000u));
            Assert.That(monitor.Milestones[5].At, Is.EqualTo(monitor.Milestones[6].At));
            Assert.That(monitor.Milestones[7].Detail, Is.EqualTo("MLI $C8 OPEN"));
            Assert.That(monitor.Milestones[7].Pc, Is.EqualTo(0xBF00u));
            Assert.That(call, Is.Not.Null);
            Assert.That(call!.Detail, Is.EqualTo("MLI $C8 OPEN"));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.ProDos8));
        });
        AssertIncreasingCycles(monitor.Milestones);
    }

    /// <summary>An unrecognized boot sector ends at CustomBoot, leaves the OS at None, and drops the traps.</summary>
    [Test]
    public void UnrecognizedSector_EndsAtCustomBoot_AndDropsTraps()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        Poke(machine.Cpu, 0x0800, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();

        var registry = Registry(machine);
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.CustomBoot));
            Assert.That(monitor.Milestones[^1].Kind, Is.EqualTo(BootMilestoneKind.CustomBootLoaderEntered));
            Assert.That(monitor.Milestones[^1].Detail, Is.EqualTo("boot sector 11 22 33 44 55 66 77 88"));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.None));
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.HasTrap(0x0801), Is.False);
            Assert.That(registry.GetTrapInfo(Firmware)!.Value.Scope, Is.EqualTo(TrapScope.Machine));
        });
    }

    /// <summary>A DOS-shaped boot whose page 3 is not DOS ends at Unknown.</summary>
    [Test]
    public void DosShapedBoot_WithoutVectors_EndsUnknown()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        Poke(machine.Cpu, 0x0800, 0x01, 0xA5, 0x27, 0xC9, 0x09, 0x4C, 0x00, 0xB7);
        Poke(machine.Cpu, 0xB700, 0x4C, 0x84, 0x9D);
        Poke(machine.Cpu, 0x9D84, 0x00);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        machine.Cpu.SetPC(0x0805);
        machine.Step();
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Unknown));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.None));
        });
    }

    /// <summary>A third profile's own addresses are the milestones the monitor records.</summary>
    [Test]
    public void FakeProfile_RecordsItsOwnMilestones_AndLogsFirmware()
    {
        var sink = new CaptureSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        var detector = BootSequenceMonitor.CreateDetector(machine, logger);
        var monitor = new BootSequenceMonitor(logger, detector, [new FakeBootProfile()]);
        monitor.Attach(machine);
        machine.AddComponent(monitor);
        Power(machine);
        Poke(machine.Cpu, 0x0800, 0xEA, 0x4C, 0x00, 0x40);
        Poke(machine.Cpu, 0x4000, 0x4C, 0x03, 0x40, 0xEA);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(Kinds(monitor), Is.EqualTo(new[]
            {
                BootMilestoneKind.RomInitEntered,
                BootMilestoneKind.BootDeviceSelected,
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
            }));
            Assert.That(monitor.Milestones[4].Pc, Is.EqualTo(0x4000u));
            Assert.That(monitor.Milestones[5].Pc, Is.EqualTo(0x4003u));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Unknown));
            Assert.That(
                sink.Lines.Exists(line =>
                    line.Contains("Boot BootFirmwareEntered at $C600 (cycle ", StringComparison.Ordinal)
                    && line.Contains("slot 6: Block Device", StringComparison.Ordinal)),
                Is.True);
        });
    }

    /// <summary>
    /// A monitor installed after power-on, with an OS already in memory and no milestone, reaches Ready.
    /// A monitored power-on has already logged Init, and detection does not replace that stage.
    /// </summary>
    [Test]
    public void NoBootMilestones_DetectorSetsReady()
    {
        using var watched = MachineFactory.CreateMachine(CreateProfile(bootLog: true));
        Power(watched);
        var watchedMonitor = MonitorOf(watched);
        Assert.That(watchedMonitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
        PlantProDosPage(watched.Cpu);
        using var watchedOutput = new StringWriter();
        var watchedTools = new InspectionTools(new Mock<IDebugSession>().Object, Context(watched, watchedOutput), Logger.None);
        var watchedGuest = (DebugEnvelope<GuestOsSnapshot>)watchedTools.GetGuestOs();
        Assert.That(watchedMonitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
        Assert.That(watchedGuest.Payload.Stage, Is.EqualTo(nameof(GuestOsBootStage.Init)));

        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        Power(machine);
        PlantProDosPage(machine.Cpu);
        using var output = new StringWriter();
        var command = new OsCommand(Logger.None);
        Assert.That(command.Execute(Context(machine, output), ["on"]).Success, Is.True);
        var monitor = MonitorOf(machine);
        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, output), Logger.None);
        var guest = (DebugEnvelope<GuestOsSnapshot>)tools.GetGuestOs();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(monitor.Milestones, Is.Empty);
            Assert.That(guest.Payload.Id, Is.EqualTo("prodos8"));
            Assert.That(guest.Payload.Stage, Is.EqualTo(nameof(GuestOsBootStage.Ready)));
        });
    }

    /// <summary>The same boot leaves identical CPU state with the monitor on and off.</summary>
    [Test]
    public void MonitorOnAndOff_MatchCpuStateAfterBootSteps()
    {
        using var enabled = BootMachine();
        using var disabled = BootMachine(bootLog: false);
        PlantDos(enabled.Cpu);
        PlantDos(disabled.Cpu);
        enabled.Cpu.SetPC(Firmware);
        disabled.Cpu.SetPC(Firmware);
        var enabledStart = enabled.Cpu.GetCycles();
        var disabledStart = disabled.Cpu.GetCycles();

        for (var step = 0; step < 4; step++)
        {
            enabled.Step();
            disabled.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(enabled.Cpu.GetPC(), Is.EqualTo(disabled.Cpu.GetPC()));
            Assert.That(enabled.Cpu.GetCycles() - enabledStart, Is.EqualTo(disabled.Cpu.GetCycles() - disabledStart));
            Assert.That(enabled.Cpu.Registers.A.GetByte(), Is.EqualTo(disabled.Cpu.Registers.A.GetByte()));
            Assert.That(enabled.Cpu.Registers.X.GetByte(), Is.EqualTo(disabled.Cpu.Registers.X.GetByte()));
            Assert.That(enabled.Cpu.Registers.Y.GetByte(), Is.EqualTo(disabled.Cpu.Registers.Y.GetByte()));
            Assert.That(enabled.Cpu.Registers.SP.GetByte(), Is.EqualTo(disabled.Cpu.Registers.SP.GetByte()));
            Assert.That(enabled.Cpu.Registers.P, Is.EqualTo(disabled.Cpu.Registers.P));
            Assert.That(MonitorOf(enabled).Milestones, Is.Not.Empty);
            Assert.That(disabled.GetComponent<IBootSequenceMonitor>(), Is.Null);
        });
    }

    /// <summary><c>boot --off --log</c> is a usage error and leaves detection off.</summary>
    [Test]
    public void BootOffAndLog_IsUsageError()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        using var output = new StringWriter();
        var result = new BootCommand().Execute(Context(machine, output), ["--log", "--off"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(machine.State, Is.EqualTo(MachineState.Off));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.Null);
        });
    }

    /// <summary>
    /// <c>boot --log</c> on an Off machine with detection off records DOS 3.3 and ProDOS from the first milestone.
    /// </summary>
    /// <returns>A task that completes when both boot profiles have been checked.</returns>
    [Test]
    public async Task BootLog_OnOffMachine_StartsAtFirmwareMilestone()
    {
        await AssertLoggedBootStartsAtFirmware(PlantDos, GuestOsId.Dos33, "DOS 3.3");
        await AssertLoggedBootStartsAtFirmware(PlantProDos, GuestOsId.ProDos8, "ProDOS 8");
    }

    /// <summary>
    /// <c>boot --log</c> keeps the installed monitor, clears its log, and does not register a second firmware trap.
    /// </summary>
    /// <returns>A task that completes when the fresh boot milestone has been checked.</returns>
    [Test]
    public async Task BootLog_WhenMonitorAlreadyInstalled_ClearsTheLogWithoutASecondMonitor()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        var registry = Registry(machine);
        Assume.That(monitor.Milestones, Is.Not.Empty);
        Assume.That(registry.HasTrap(Firmware), Is.True);

        var command = LoggedBootCommand();
        using var output = new StringWriter();
        var result = command.Execute(Context(machine, output), ["--log"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(machine.GetComponents<BootSequenceMonitor>().Count(), Is.EqualTo(1));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.SameAs(monitor));
        });

        await command.BootTask;
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(1));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(registry.GetAllTraps().Count(trap => trap.Address == Firmware), Is.EqualTo(1));
        });
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();

        Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
        Assert.That(monitor.Milestones[0].Detail, Does.Contain("cold"));
    }

    /// <summary><c>os on</c> leaves an in-progress boot log in place.</summary>
    [Test]
    public void OsOn_WhenMonitorAlreadyInstalled_LeavesTheLog()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        int logged = monitor.Milestones.Count;
        GuestOsBootStage stage = monitor.Stage;

        using var output = new StringWriter();
        var result = new OsCommand(Logger.None).Execute(Context(machine, output), ["on"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(machine.GetComponents<BootSequenceMonitor>().Count(), Is.EqualTo(1));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.SameAs(monitor));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(logged));
            Assert.That(monitor.Stage, Is.EqualTo(stage));
        });
    }

    /// <summary><c>reset --log</c> without a cold-reset option is a usage error and does not reset.</summary>
    [Test]
    public void ResetLog_WithoutColdReset_IsUsageError()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        var monitor = MonitorOf(machine);
        int logged = monitor.Milestones.Count;
        uint pc = machine.Cpu.GetPC();
        using var context = ResetContext(machine);

        var result = LoggedResetCommand().Execute(context, ["--log"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(logged));
            Assert.That(machine.Cpu.GetPC(), Is.EqualTo(pc));
        });
    }

    /// <summary><c>reset --closed-apple --log</c> is a usage error. Closed Apple is not a cold reset.</summary>
    [Test]
    public void ResetClosedAppleAndLog_IsUsageError()
    {
        using var machine = BootMachine(bootLog: false);
        using var context = ResetContext(machine);

        var result = LoggedResetCommand().Execute(context, ["--closed-apple", "--log"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(2));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.Null);
        });
    }

    /// <summary><c>reset --hard --log</c> installs the monitor when detection is off, then records firmware.</summary>
    [Test]
    public void ResetHardLog_WhenDetectionIsOff_InstallsAndRecordsFirmware()
    {
        using var machine = BootMachine(bootLog: false);
        Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.Null);
        using var context = ResetContext(machine);

        var result = LoggedResetCommand().Execute(context, ["--hard", "--log"]);
        var monitor = MonitorOf(machine);

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
        Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
        Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.BootRom));
    }

    /// <summary><c>reset --hard --log</c> clears the previous log and records the next boot from reset.</summary>
    [Test]
    public void ResetHardLog_StartsAFreshBootLog()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        Assume.That(monitor.Milestones, Is.Not.Empty);
        machine.Cpu.SetPC(0x0300);
        Poke(machine.Cpu, 0x0300, 0xEA, 0xEA, 0xEA);
        using var context = ResetContext(machine);

        var result = LoggedResetCommand().Execute(context, ["--hard", "--log"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(machine.GetComponents<BootSequenceMonitor>().Count(), Is.EqualTo(1));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.SameAs(monitor));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(1));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("cold"));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
        });

        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();

        Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
        Assert.That(monitor.Milestones[1].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
    }

    /// <summary><c>reset --cold --log</c> drops the previous milestone log. Without a keyboard the pulse is warm.</summary>
    [Test]
    public void ResetColdLog_StartsAFreshBootLog()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        Assume.That(monitor.Milestones, Is.Not.Empty);
        machine.Cpu.SetPC(0x0300);
        Poke(machine.Cpu, 0x0300, 0xEA, 0xEA, 0xEA);
        using var context = ResetContext(machine);

        var result = LoggedResetCommand().Execute(context, ["--cold", "--log"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(machine.GetComponents<BootSequenceMonitor>().Count(), Is.EqualTo(1));
            Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.SameAs(monitor));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(1));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("warm"));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.None));
        });

        machine.Cpu.SetPC(Firmware);
        machine.Step();
        Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
        Assert.That(monitor.Milestones[1].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
    }

    /// <summary>Boot and reset help name <c>--log</c> and point at <c>os boot</c>.</summary>
    [Test]
    public void BootAndResetHelp_DescribeLog()
    {
        var boot = new BootCommand();
        var reset = new ResetCommand();
        string bootExamples = string.Join('\n', boot.Examples);
        string resetExamples = string.Join('\n', reset.Examples);

        Assert.Multiple(() =>
        {
            Assert.That(boot.Usage, Does.Contain("--log"));
            Assert.That(boot.Synopsis, Does.Contain("--log"));
            Assert.That(boot.DetailedDescription, Does.Contain("os boot"));
            Assert.That(boot.Options.Select(option => option.Name), Does.Contain("--log"));
            Assert.That(bootExamples, Does.Contain("boot --log"));
            Assert.That(bootExamples, Does.Contain("power --on --log"));
            Assert.That(boot.SeeAlso, Does.Contain("os"));
            Assert.That(reset.Usage, Does.Contain("--log"));
            Assert.That(reset.Synopsis, Does.Contain("--log"));
            Assert.That(reset.DetailedDescription, Does.Contain("os boot"));
            Assert.That(reset.Options.Select(option => option.Name), Does.Contain("--log"));
            Assert.That(resetExamples, Does.Contain("--hard --log"));
            Assert.That(resetExamples, Does.Contain("--cold --log"));
            Assert.That(reset.SeeAlso, Does.Contain("os"));
        });
    }

    /// <summary>A JMP fetched from ROM after cold Init is an autostart device selection.</summary>
    [Test]
    public void TransferFromRom_SelectsAutostart()
    {
        using var machine = RomBootMachine();
        Poke(machine.Cpu, 0xF000, 0x4C, 0x00, 0xC6);
        machine.Cpu.SetPC(0xF000);
        machine.Step();
        machine.Step();

        var monitor = MonitorOf(machine);
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Is.EqualTo("reset vector $0000, cold"));
            Assert.That(monitor.Milestones[1].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
            Assert.That(monitor.Milestones[1].Detail, Is.EqualTo("slot 6: Block Device, method autostart"));
            Assert.That(monitor.Milestones[2].Kind, Is.EqualTo(BootMilestoneKind.BootFirmwareEntered));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.BootRom));
        });
    }

    /// <summary>A JMP fetched from RAM after cold Init is a user device selection.</summary>
    [Test]
    public void TransferFromRam_SelectsUser()
    {
        using var machine = BootMachine();
        Poke(machine.Cpu, 0x0300, 0x4C, 0x00, 0xC6);
        machine.Cpu.SetPC(0x0300);
        machine.Step();
        machine.Step();

        var selected = MonitorOf(machine).Milestones[1];
        Assert.Multiple(() =>
        {
            Assert.That(selected.Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
            Assert.That(selected.Detail, Is.EqualTo("slot 6: Block Device, method user"));
        });
    }

    /// <summary>Open Apple held at RESET is a cold start and selects the device as open-apple.</summary>
    [Test]
    public void OpenAppleReset_IsCold_AndSelectsOpenApple()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        var monitor = MonitorOf(machine);
        Assume.That(monitor.Milestones, Is.Not.Empty);
        var keyboard = new AppleIIeKeyboard();
        machine.AddComponent(keyboard);
        keyboard.SetModifiers(KeyboardModifiers.OpenApple);

        machine.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(1));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Is.EqualTo("reset vector $0000, cold, open-apple"));
        });

        machine.Cpu.SetPC(Firmware);
        machine.Step();
        Assert.That(monitor.Milestones[1].Detail, Is.EqualTo("slot 6: Block Device, method open-apple"));
    }

    /// <summary>A warm reset keeps the stage and appends a warm Init milestone.</summary>
    [Test]
    public void WarmReset_LogsWarmWithoutRestartingTheStage()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        Assume.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Boot1));
        int logged = monitor.Milestones.Count;

        machine.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Boot1));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(logged + 1));
            Assert.That(monitor.Milestones[^1].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[^1].Detail, Is.EqualTo("reset vector $0000, warm"));
        });
    }

    /// <summary>Slot firmware after a warm reset starts the boot stages again and keeps the warm milestone.</summary>
    [Test]
    public void WarmReset_ThatEntersFirmware_ContinuesTheBoot()
    {
        using var machine = BootMachine();
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        machine.Reset();
        int warmIndex = monitor.Milestones.Count - 1;

        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Milestones[warmIndex].Detail, Does.Contain("warm"));
            Assert.That(monitor.Milestones[warmIndex + 1].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
            Assert.That(monitor.Milestones[warmIndex + 1].Detail, Does.Contain("method user"));
            Assert.That(monitor.Milestones[warmIndex + 2].Kind, Is.EqualTo(BootMilestoneKind.BootFirmwareEntered));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Boot1));
        });
    }

    /// <summary>
    /// A ProDOS session followed by a RAM jump to <c>$C600</c> (<c>PR#6</c>) boots DOS 3.3.
    /// The old session traps are gone and the DOS session traps are attached at Loader.
    /// </summary>
    [Test]
    public void ProDosThenUserJump_BootsDos33_AndSwapsSessionTraps()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        var started = new List<GuestOsId>();
        var ended = new List<GuestOsSessionEventArgs>();
        monitor.GuestOsSessionStarted += (_, args) => started.Add(args.Id);
        monitor.GuestOsSessionEnded += (_, args) => ended.Add(args);
        BootProDosToReady(machine);
        var registry = Registry(machine);
        var detector = machine.GetComponent<IGuestOsDetector>()!;
        registry.Register(
            0xBF06,
            "session clock",
            TrapCategory.OperatingSystem,
            static (_, _, _) => TrapResult.NotHandled,
            "DATETIME",
            TrapScope.GuestOsSession);
        registry.Register(0x0400, "machine hook", TrapCategory.UserDefined, static (_, _, _) => TrapResult.NotHandled);
        registry.RegisterSlotDependent(0xC610, Slot, "card hook", TrapCategory.SlotFirmware, static (_, _, _) => TrapResult.NotHandled);

        PlantDos(machine.Cpu);
        ClearProDosPage(machine.Cpu);
        JumpToFirmware(machine);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Has.Count.EqualTo(1));
            Assert.That(ended[0].Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(ended[0].Stage, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(detector.Current.Id, Is.EqualTo(GuestOsId.None));
            Assert.That(detector.Current.Reason, Is.EqualTo("Guest OS detection has not run."));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("method user"));
            Assert.That(Kinds(monitor), Does.Not.Contain(BootMilestoneKind.RomInitEntered));
            Assert.That(registry.HasTrap(0xBF00), Is.False);
            Assert.That(registry.HasTrap(0xBF06), Is.False);
            Assert.That(registry.HasTrap(0x0400), Is.True);
            Assert.That(registry.HasTrap(0xC610), Is.True);
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.GetTrapInfo(Firmware)!.Value.Scope, Is.EqualTo(TrapScope.Machine));
        });

        FinishDosFromBootSector(machine);

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(started, Is.EqualTo(new[] { GuestOsId.ProDos8, GuestOsId.Dos33 }));
            Assert.That(detector.Current.Id, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(registry.HasTrap(0xBF00), Is.False);
            Assert.That(registry.HasTrap(0xBF06), Is.False);
            Assert.That(registry.HasTrap(Dos33VectorPage.FileManagerTarget), Is.True);
            Assert.That(registry.GetTrapInfo(Dos33VectorPage.FileManagerTarget)!.Value.Scope, Is.EqualTo(TrapScope.GuestOsSession));
            Assert.That(registry.HasTrap(Dos33VectorPage.RwtsTarget), Is.True);
            Assert.That(registry.HasTrap(0x0400), Is.True);
            Assert.That(registry.HasTrap(0xC610), Is.True);
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.BootDeviceSelected));
        });
    }

    /// <summary>A DOS 3.3 session followed by <c>PR#6</c> boots ProDOS and brings the MLI trap back.</summary>
    [Test]
    public void Dos33ThenUserJump_BootsProDos_AndSwapsSessionTraps()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        var started = new List<GuestOsBootStage>();
        var ended = new List<GuestOsId>();
        monitor.GuestOsSessionStarted += (_, args) => started.Add(args.Stage);
        monitor.GuestOsSessionEnded += (_, args) => ended.Add(args.Id);
        BootDosToReady(machine);
        var registry = Registry(machine);
        Assume.That(registry.HasTrap(Dos33VectorPage.RwtsTarget), Is.True);

        PlantProDos(machine.Cpu);
        JumpToFirmware(machine);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.EqualTo(new[] { GuestOsId.Dos33 }));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.BootRom));
            Assert.That(registry.HasTrap(Dos33VectorPage.FileManagerTarget), Is.False);
            Assert.That(registry.HasTrap(Dos33VectorPage.RwtsTarget), Is.False);
            Assert.That(registry.HasTrap(0xBF00), Is.False);
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Reason, Is.EqualTo("Guest OS detection has not run."));
        });

        for (var step = 0; step < 5; step++)
        {
            machine.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(started, Is.EqualTo(new[] { GuestOsBootStage.Loader, GuestOsBootStage.Loader }));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(registry.HasTrap(0xBF00), Is.True);
            Assert.That(registry.GetTrapInfo(0xBF00)!.Value.Scope, Is.EqualTo(TrapScope.GuestOsSession));
            Assert.That(registry.HasTrap(Dos33VectorPage.FileManagerTarget), Is.False);
            Assert.That(registry.HasTrap(Dos33VectorPage.RwtsTarget), Is.False);
            Assert.That(registry.HasTrap(Firmware), Is.True);
        });
    }

    /// <summary>A monitor-ROM jump to <c>$Cn00</c> (<c>IN#6</c> or <c>C600G</c>) ends the session too.</summary>
    [Test]
    public void MonitorRomJump_EndsTheProDosSession()
    {
        using var machine = RomBootMachine();
        var monitor = MonitorOf(machine);
        GuestOsBootStage? endedAt = null;
        monitor.GuestOsSessionEnded += (_, args) => endedAt = args.Stage;
        BootProDosToReady(machine);

        Poke(machine.Cpu, 0xF000, 0x4C, 0x00, 0xC6);
        machine.Cpu.SetPC(0xF000);
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(endedAt, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("method autostart"));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.BootRom));
            Assert.That(Registry(machine).HasTrap(0xBF00), Is.False);
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.None));
        });
    }

    /// <summary>A warm reset leaves the assigned OS and its session traps in place.</summary>
    [Test]
    public void WarmReset_LeavesTheSessionLoaded()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        var ended = 0;
        monitor.GuestOsSessionEnded += (_, _) => ended++;
        BootProDosToReady(machine);
        var registry = Registry(machine);
        registry.Register(
            0xBF06,
            "session clock",
            TrapCategory.OperatingSystem,
            static (_, _, _) => TrapResult.NotHandled,
            "DATETIME",
            TrapScope.GuestOsSession);
        int logged = monitor.Milestones.Count;

        machine.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.EqualTo(0));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(logged + 1));
            Assert.That(monitor.Milestones[^1].Detail, Does.Contain("warm"));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(registry.HasTrap(0xBF00), Is.True);
            Assert.That(registry.HasTrap(0xBF06), Is.True);
            Assert.That(registry.HasTrap(Firmware), Is.True);
        });
    }

    /// <summary>A cold reset returns to Init, clears the detector, and unloads session traps.</summary>
    [Test]
    public void ColdReset_EndsTheSession()
    {
        using var machine = BootMachine();
        var monitor = MonitorOf(machine);
        GuestOsSessionEventArgs? ended = null;
        monitor.GuestOsSessionEnded += (_, args) => ended = args;
        BootProDosToReady(machine);
        var registry = Registry(machine);
        registry.Register(
            0xBF06,
            "session clock",
            TrapCategory.OperatingSystem,
            static (_, _, _) => TrapResult.NotHandled,
            "DATETIME",
            TrapScope.GuestOsSession);
        registry.RegisterSlotDependent(0xC610, Slot, "card hook", TrapCategory.SlotFirmware, static (_, _, _) => TrapResult.NotHandled);

        machine.Reset(hardReset: true);

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.Not.Null);
            Assert.That(ended!.Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(ended.Stage, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Init));
            Assert.That(monitor.Milestones, Has.Count.EqualTo(1));
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("cold"));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Id, Is.EqualTo(GuestOsId.None));
            Assert.That(machine.GetComponent<IGuestOsDetector>()!.Current.Reason, Is.EqualTo("Guest OS detection has not run."));
            Assert.That(registry.HasTrap(0xBF00), Is.False);
            Assert.That(registry.HasTrap(0xBF06), Is.False);
            Assert.That(registry.HasTrap(Dos33VectorPage.FileManagerTarget), Is.False);
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.HasTrap(0xC610), Is.True);
            Assert.That(registry.GetAllTraps().Single(trap => trap.Name == "card hook").Scope, Is.EqualTo(TrapScope.Card));
        });
    }

    /// <summary>Two ROM polls of the keyboard data register, with no slot entry, end at NoBootDevice.</summary>
    [Test]
    public void RomKeyboardPoll_EndsAtNoBootDevice()
    {
        using var machine = RomBootMachine();

        // $C000 with no keyboard reads as $FF, so BMI keeps polling. BPL would leave after one read.
        Poke(machine.Cpu, 0xF000, 0xAD, 0x00, 0xC0, 0x30, 0xFB);
        machine.Cpu.SetPC(0xF000);
        machine.Step();
        machine.Step();
        machine.Step();

        var monitor = MonitorOf(machine);
        var registry = Registry(machine);
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.NoBootDevice));
            Assert.That(monitor.Milestones[^1].Kind, Is.EqualTo(BootMilestoneKind.NoBootDevice));
            Assert.That(monitor.Milestones[^1].Detail, Is.EqualTo("ROM keyboard wait, no boot device"));
            Assert.That(monitor.Milestones[^1].Pc, Is.EqualTo(0xF000u));
            Assert.That(Kinds(monitor), Does.Not.Contain(BootMilestoneKind.BootFirmwareEntered));
            Assert.That(registry.HasTrap(Firmware), Is.True);
            Assert.That(registry.HasTrap(0xC000, TrapOperation.Read), Is.False);
        });
    }

    /// <summary>The call log is absent until the profile or <c>os calls</c> turns it on.</summary>
    [Test]
    public void CallLog_IsOffByDefault()
    {
        using var machine = BootMachine();
        Assert.That(machine.GetComponent<IOsCallLog>(), Is.Null);
    }

    /// <summary>
    /// A ProDOS MLI call is decoded from the stack, an unknown command is named, and the guest matches a machine with the log off.
    /// </summary>
    [Test]
    public void ProDosCalls_AreDecoded_AndMatchTheLogOffMachine()
    {
        using var logged = BootMachine(callLog: "all");
        using var quiet = BootMachine();
        BootProDosToReady(logged);
        BootProDosToReady(quiet);
        var log = logged.GetComponent<OsCallLog>()!;

        PlantMli(logged.Cpu, 0x0300, 0xC8, 0x0400, "/HELLO");
        PlantMli(quiet.Cpu, 0x0300, 0xC8, 0x0400, "/HELLO");
        Enter(logged, 0x0300);
        Enter(quiet, 0x0300);

        PlantMli(logged.Cpu, 0x0310, 0xCA, 0x0500, null);
        PlantMli(quiet.Cpu, 0x0310, 0xCA, 0x0500, null);
        Enter(logged, 0x0310);
        Enter(quiet, 0x0310);

        PlantMli(logged.Cpu, 0x0320, 0x1A, 0x0000, null);
        PlantMli(quiet.Cpu, 0x0320, 0x1A, 0x0000, null);
        Enter(logged, 0x0320);
        Enter(quiet, 0x0320);

        AssertSameGuest(logged.Cpu, quiet.Cpu);
        IReadOnlyList<OsCall> entries = log.Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(entries[0].Os, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(entries[0].Code, Is.EqualTo(0xC8));
            Assert.That(entries[0].Name, Is.EqualTo("OPEN"));
            Assert.That(entries[0].Caller, Is.EqualTo(0x0300u));
            Assert.That(entries[0].ParamList, Is.EqualTo(0x0400u));
            Assert.That(entries[0].Summary, Is.EqualTo("/HELLO"));
            Assert.That(entries[1].Name, Is.EqualTo("READ"));
            Assert.That(entries[1].Summary, Is.Null);
            Assert.That(entries[2].Name, Is.EqualTo("$1A (unknown)"));
            Assert.That(entries[2].Code, Is.EqualTo(0x1A));
            Assert.That(entries[0].At.Value, Is.LessThan(entries[1].At.Value));
            Assert.That(entries[1].At.Value, Is.LessThan(entries[2].At.Value));
        });
    }

    /// <summary>DOS file-manager and RWTS calls are decoded, and the guest matches a machine with the log off.</summary>
    [Test]
    public void DosCalls_AreDecoded_AndMatchTheLogOffMachine()
    {
        using var logged = BootMachine(callLog: "all");
        using var quiet = BootMachine();
        BootDosToReady(logged);
        BootDosToReady(quiet);
        var log = logged.GetComponent<OsCallLog>()!;

        PlantFileManager(logged.Cpu, 1);
        PlantFileManager(quiet.Cpu, 1);
        Enter(logged, 0x0320);
        Enter(quiet, 0x0320);

        PlantRwts(logged.Cpu, 1);
        PlantRwts(quiet.Cpu, 1);
        Enter(logged, 0x0330);
        Enter(quiet, 0x0330);

        AssertSameGuest(logged.Cpu, quiet.Cpu);
        IReadOnlyList<OsCall> entries = log.Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Os, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(entries[0].Code, Is.EqualTo(0x01));
            Assert.That(entries[0].Name, Is.EqualTo("OPEN"));
            Assert.That(entries[0].Caller, Is.EqualTo(0x0320u));
            Assert.That(entries[0].ParamList, Is.EqualTo(0x0900u));
            Assert.That(entries[1].Name, Is.EqualTo("READ"));
            Assert.That(entries[1].Code, Is.EqualTo(0x01));
            Assert.That(entries[1].Caller, Is.EqualTo(0x0330u));
            Assert.That(entries[1].ParamList, Is.EqualTo(0x0A00u));
            Assert.That(entries[1].Summary, Is.EqualTo("slot 6 drive 1 track $11 sector $05"));
        });
    }

    /// <summary><c>mli</c> ignores DOS, <c>dos</c> ignores MLI, and <c>off</c> stops recording.</summary>
    [Test]
    public void CallLogModes_FilterMliAndDos()
    {
        using var mli = BootMachine(callLog: "mli");
        BootDosToReady(mli);
        PlantFileManager(mli.Cpu, 6);
        Enter(mli, 0x0320);
        Assert.That(mli.GetComponent<OsCallLog>()!.Entries, Is.Empty);

        using var dos = BootMachine(callLog: "dos");
        BootProDosToReady(dos);
        PlantMli(dos.Cpu, 0x0300, 0xC8, 0x0400, "/HELLO");
        Enter(dos, 0x0300);
        Assert.That(dos.GetComponent<OsCallLog>()!.Entries, Is.Empty);

        using var machine = BootMachine(callLog: "all");
        BootProDosToReady(machine);
        PlantMli(machine.Cpu, 0x0300, 0xC8, 0x0400, "/HELLO");
        Enter(machine, 0x0300);
        using var output = new StringWriter();
        Assert.That(new OsCommand(Logger.None).Execute(Context(machine, output), ["calls", "off"]).Success, Is.True);
        PlantMli(machine.Cpu, 0x0310, 0x65, 0x0000, null);
        Enter(machine, 0x0310);
        IReadOnlyList<OsCall> entries = machine.GetComponent<OsCallLog>()!.Entries;
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Name, Is.EqualTo("OPEN"));
    }

    /// <summary><c>os calls</c> and <c>get_os_calls</c> return the ring, including a pathname.</summary>
    [Test]
    public void OsCallsCommand_AndMcp_ReturnTheDecodedCall()
    {
        var sink = new CaptureSink();
        var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        using var machine = BootMachine();
        BootProDosToReady(machine);
        using var output = new StringWriter();
        var command = new OsCommand(logger);
        Assert.That(command.Execute(Context(machine, output), ["calls", "on"]).Success, Is.True);

        PlantMli(machine.Cpu, 0x0300, 0xC2, 0x0400, "/OLD", "/NEW");
        Enter(machine, 0x0300);

        output.GetStringBuilder().Clear();
        Assert.That(command.Execute(Context(machine, output), ["calls"]).Success, Is.True);
        Assert.That(output.ToString(), Does.Contain("RENAME"));
        Assert.That(output.ToString(), Does.Contain("/OLD -> /NEW"));
        Assert.That(sink.Lines, Has.Some.Contains("RENAME").And.Some.Contains("/OLD -> /NEW"));

        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, output), Logger.None);
        var envelope = (DebugEnvelope<OsCallLogSnapshot>)tools.GetOsCalls(1);
        Assert.Multiple(() =>
        {
            Assert.That(envelope.Payload.Mode, Is.EqualTo("all"));
            Assert.That(envelope.Payload.Calls, Has.Count.EqualTo(1));
            Assert.That(envelope.Payload.Calls[0].Name, Is.EqualTo("RENAME"));
            Assert.That(envelope.Payload.Calls[0].Summary, Is.EqualTo("/OLD -> /NEW"));
        });
        Assert.That(tools.GetOsCalls(0), Is.InstanceOf<CallToolResult>());
    }

    /// <summary>The ring keeps the newest calls and drops the oldest.</summary>
    [Test]
    public void CallLog_DropsTheOldestWhenFull()
    {
        using var machine = BootMachine(callLog: "mli");
        BootProDosToReady(machine);
        var log = machine.GetComponent<OsCallLog>()!;
        PlantMli(machine.Cpu, 0x0300, 0x01, 0x0000, null);
        Enter(machine, 0x0300);
        machine.Cpu.Poke8(0x0303, 0xCA);
        for (int index = 0; index < OsCallLog.Capacity; index++)
        {
            Enter(machine, 0x0300);
        }

        IReadOnlyList<OsCall> entries = log.Entries;
        Assert.That(entries, Has.Count.EqualTo(OsCallLog.Capacity));
        Assert.That(entries.All(call => call.Name == "READ"), Is.True);
    }

    private static async Task AssertLoggedBootStartsAtFirmware(Action<ICpu> plant, GuestOsId os, string bootSectorDetail)
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        machine.PowerOnMemoryPattern = new PowerOnMemoryPattern(PowerOnMemoryPatternKind.Zero, 0);
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        Assert.That(machine.GetComponent<BootSequenceMonitor>(), Is.Null);

        var command = LoggedBootCommand();
        using var output = new StringWriter();
        var result = command.Execute(Context(machine, output), ["--log"]);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(machine.GetComponents<BootSequenceMonitor>().Count(), Is.EqualTo(1));

        await command.BootTask;
        plant(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();

        var monitor = MonitorOf(machine);
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Milestones[0].Kind, Is.EqualTo(BootMilestoneKind.RomInitEntered));
            Assert.That(monitor.Milestones[0].Detail, Does.Contain("cold"));
            Assert.That(monitor.Milestones[3].Kind, Is.EqualTo(BootMilestoneKind.BootSectorLoaded));
            Assert.That(monitor.Milestones[3].Os, Is.EqualTo(os));
            Assert.That(monitor.Milestones[3].Detail, Is.EqualTo(bootSectorDetail));
        });
    }

    private static BootCommand LoggedBootCommand() =>
        new(null, null, bootDelayMs: 0, static (host, token) => host.PowerOnAsync(token), Logger.None);

    private static ResetCommand LoggedResetCommand() => new(null, null, Logger.None);

    private static DebugContext ResetContext(IMachine machine)
    {
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter());
        context.AttachMachine(machine);
        return context;
    }

    private static IMachine BootMachine(bool bootLog = true, string callLog = "off")
    {
        var machine = MachineFactory.CreateMachine(CreateProfile(bootLog, callLog: callLog));
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        Power(machine);
        return machine;
    }

    private static IMachine RomBootMachine()
    {
        var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: true, highRom: true));
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        Power(machine);
        return machine;
    }

    private static void Power(IMachine machine)
    {
        machine.PowerOnMemoryPattern = new PowerOnMemoryPattern(PowerOnMemoryPatternKind.Zero, 0);
        machine.PowerOnAsync().GetAwaiter().GetResult();
    }

    private static BootSequenceMonitor MonitorOf(IMachine machine) =>
        machine.GetComponent<BootSequenceMonitor>()!;

    private static TrapRegistry Registry(IMachine machine) =>
        (TrapRegistry)machine.GetComponent<ITrapRegistry>()!;

    private static IDebugContext Context(IMachine machine, StringWriter output)
    {
        var context = new Mock<IDebugContext>();
        context.Setup(candidate => candidate.Machine).Returns(machine);
        context.Setup(candidate => candidate.Output).Returns(output);
        context.Setup(candidate => candidate.Error).Returns(TextWriter.Null);
        return context.Object;
    }

    private static List<TrapInfo> OperatingSystemTraps(ITrapRegistry registry) =>
        registry.GetAllTraps().Where(trap => trap.Category == TrapCategory.OperatingSystem).ToList();

    private static BootMilestoneKind[] Kinds(IBootSequenceMonitor monitor) =>
        monitor.Milestones.Select(milestone => milestone.Kind).ToArray();

    private static void AssertIncreasingCycles(IReadOnlyList<BootMilestone> milestones)
    {
        Assert.That(milestones, Is.Not.Empty);
        for (var i = 1; i < milestones.Count; i++)
        {
            Assert.That(milestones[i].At.Value, Is.GreaterThanOrEqualTo(milestones[i - 1].At.Value));
        }
    }

    private static void BootDosToReady(IMachine machine)
    {
        PlantDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        machine.Step();
        machine.Step();
        machine.Cpu.SetPC(0x0805);
        machine.Step();
        machine.Step();
        machine.Step();
    }

    private static void BootProDosToReady(IMachine machine)
    {
        PlantProDos(machine.Cpu);
        machine.Cpu.SetPC(Firmware);
        for (var step = 0; step < 6; step++)
        {
            machine.Step();
        }
    }

    private static void JumpToFirmware(IMachine machine)
    {
        Poke(machine.Cpu, 0x0300, 0x4C, 0x00, 0xC6);
        machine.Cpu.SetPC(0x0300);
        machine.Step();
        machine.Step();
    }

    private static void FinishDosFromBootSector(IMachine machine)
    {
        machine.Step();
        machine.Cpu.SetPC(0x0805);
        machine.Step();
        machine.Step();
        machine.Step();
    }

    private static void ClearProDosPage(ICpu cpu)
    {
        Poke(cpu, 0xBF00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
        Poke(cpu, 0xBFFF, 0x00);
    }

    private static void PlantDos(ICpu cpu)
    {
        Poke(cpu, 0x0800, 0x01, 0xA5, 0x27, 0xC9, 0x09, 0x4C, 0x00, 0xB7);
        Poke(cpu, 0xB700, 0x4C, 0x84, 0x9D);
        Poke(cpu, 0x9D84, 0xEA);
        PlantJmp(cpu, 0x03D0, 0x9DBF);
        PlantJmp(cpu, 0x03D3, 0x9D84);
        PlantJmp(cpu, 0x03D6, 0xAAFD);
        PlantJmp(cpu, 0x03D9, 0xB7B5);
        PlantJmp(cpu, 0x03EA, 0xA851);
    }

    private static void PlantProDos(ICpu cpu)
    {
        PlantProDosPage(cpu);
        Poke(cpu, 0x0800, 0x01, 0x38, 0xB0, 0x03, 0xEA, 0xEA, 0xEA, 0x4C, 0x00, 0x20);
        Poke(cpu, 0x2000, 0x4C, 0x00, 0x20);
        Poke(cpu, 0x0300, 0x20, 0x00, 0xBF, 0xC8, 0x00, 0x00, 0x00);
        Poke(cpu, 0xBF00, 0x4C, 0x00, 0xBF);
    }

    private static void PlantProDosPage(ICpu cpu)
    {
        Poke(cpu, 0xBF00, 0x4C, 0x00, 0xBF);
        Poke(cpu, 0xBF06, 0x60);
        Poke(cpu, 0xBF07, 0x00, 0xD0);
        Poke(cpu, 0xBF98, 0x00);
        Poke(cpu, 0xBFFD, 0x00);
        Poke(cpu, 0xBFFF, 0x24);
    }

    private static void PlantJmp(ICpu cpu, ushort address, ushort target)
    {
        Poke(cpu, address, 0x4C, (byte)target, (byte)(target >> 8));
    }

    private static void Enter(IMachine machine, ushort address)
    {
        machine.Cpu.SetPC(address);
        machine.Step();
        machine.Step();
    }

    private static void PlantMli(ICpu cpu, ushort at, byte command, ushort parms, string? path, string? newPath = null)
    {
        cpu.Poke8(ProDosGlobalPage.MliEntry, 0x60);
        Poke(cpu, at, 0x20, 0x00, 0xBF, command, (byte)parms, (byte)(parms >> 8));
        if (path is null)
        {
            return;
        }

        ushort text = (ushort)(parms + 16);
        PlantPath(cpu, text, path);
        cpu.Poke8(parms, 0x03);
        cpu.Poke8((ushort)(parms + 1), (byte)text);
        cpu.Poke8((ushort)(parms + 2), (byte)(text >> 8));
        if (newPath is null)
        {
            return;
        }

        ushort renamed = (ushort)(text + path.Length + 2);
        PlantPath(cpu, renamed, newPath);
        cpu.Poke8((ushort)(parms + 3), (byte)renamed);
        cpu.Poke8((ushort)(parms + 4), (byte)(renamed >> 8));
    }

    private static void PlantPath(ICpu cpu, ushort address, string path)
    {
        cpu.Poke8(address, (byte)path.Length);
        for (int index = 0; index < path.Length; index++)
        {
            cpu.Poke8((ushort)(address + 1 + index), (byte)path[index]);
        }
    }

    private static void PlantFileManager(ICpu cpu, byte opcode)
    {
        cpu.Poke8(Dos33VectorPage.FileManagerTarget, 0x60);
        ushort list = 0x0900;
        cpu.Poke8(Dos33VectorPage.FileManagerParameterList, (byte)list);
        cpu.Poke8((ushort)(Dos33VectorPage.FileManagerParameterList + 1), (byte)(list >> 8));
        cpu.Poke8(list, opcode);
        Poke(cpu, 0x0320, 0x20, 0xFD, 0xAA);
    }

    private static void PlantRwts(ICpu cpu, byte command)
    {
        cpu.Poke8(Dos33VectorPage.RwtsTarget, 0x60);
        ushort iob = 0x0A00;
        cpu.Poke8(iob, 0x01);
        cpu.Poke8((ushort)(iob + 1), 0x60);
        cpu.Poke8((ushort)(iob + 2), 0x01);
        cpu.Poke8((ushort)(iob + 4), 0x11);
        cpu.Poke8((ushort)(iob + 5), 0x05);
        cpu.Poke8((ushort)(iob + 0x0C), command);
        cpu.Registers.A.SetByte((byte)(iob >> 8));
        cpu.Registers.Y.SetByte((byte)iob);
        Poke(cpu, 0x0330, 0x20, 0xB5, 0xB7);
    }

    private static void AssertSameGuest(ICpu logged, ICpu quiet)
    {
        Assert.Multiple(() =>
        {
            Assert.That(logged.Registers.A.GetByte(), Is.EqualTo(quiet.Registers.A.GetByte()));
            Assert.That(logged.Registers.X.GetByte(), Is.EqualTo(quiet.Registers.X.GetByte()));
            Assert.That(logged.Registers.Y.GetByte(), Is.EqualTo(quiet.Registers.Y.GetByte()));
            Assert.That(logged.Registers.SP.GetByte(), Is.EqualTo(quiet.Registers.SP.GetByte()));
            Assert.That((byte)logged.Registers.P, Is.EqualTo((byte)quiet.Registers.P));
            Assert.That(logged.GetPC(), Is.EqualTo(quiet.GetPC()));
        });

        for (uint address = 0; address < 0xC000; address++)
        {
            byte left = logged.Peek8(address);
            byte right = quiet.Peek8(address);
            if (left != right)
            {
                Assert.Fail($"${address:X4} is ${left:X2} with the log and ${right:X2} without it.");
            }
        }
    }

    private static void Poke(ICpu cpu, ushort address, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            cpu.Poke8((uint)(address + i), bytes[i]);
        }
    }

    private static MachineProfile CreateProfile(bool bootLog, bool highRom = false, string callLog = "off")
    {
        var highMemory = new List<MemoryRegionProfile>();
        if (highRom)
        {
            highMemory.Add(new MemoryRegionProfile
            {
                Name = "vector-ram",
                Type = "ram",
                Start = "0xD000",
                Size = "0x2000",
                Permissions = "rwx",
                Source = "vector-ram",
                SourceOffset = "0x0000",
            });
            highMemory.Add(new MemoryRegionProfile
            {
                Name = "synthetic-rom",
                Type = "rom",
                Start = "0xF000",
                Size = "0x1000",
                Permissions = "rx",
                Source = "vector-ram",
                SourceOffset = "0x2000",
            });
        }
        else
        {
            highMemory.Add(new MemoryRegionProfile
            {
                Name = "vector-ram",
                Type = "ram",
                Start = "0xD000",
                Size = "0x3000",
                Permissions = "rwx",
                Source = "vector-ram",
                SourceOffset = "0x0000",
            });
        }

        var regions = new List<MemoryRegionProfile>
        {
            new()
            {
                Name = "main-ram",
                Type = "ram",
                Start = "0x0000",
                Size = "0xC000",
                Permissions = "rwx",
                Source = "main-ram",
                SourceOffset = "0x0000",
            },
            new()
            {
                Name = "io-region",
                Type = "composite",
                Start = "0xC000",
                Size = "0x1000",
                Permissions = "rw",
                Handler = "composite-io",
            },
        };
        regions.AddRange(highMemory);

        return new MachineProfile
        {
            Name = "guest-os-boot",
            DisplayName = "Guest OS boot",
            Cpu = new CpuProfileSection { Type = "65C02", ClockSpeed = 1_000_000 },
            AddressSpace = 16,
            Memory = new MemoryProfileSection
            {
                Physical =
                [
                    new PhysicalMemoryProfile { Name = "main-ram", Size = "0xC000", Fill = "0x00" },
                    new PhysicalMemoryProfile { Name = "vector-ram", Size = "0x3000", Fill = "0x00" },
                ],
                Regions = regions,
            },
            Devices = new DevicesProfile
            {
                Slots = new SlotSystemProfile { Enabled = true, Cards = [] },
            },
            GuestOs = new GuestOsProfile { Detect = bootLog, BootLog = bootLog, CallLog = callLog },
        };
    }

    private sealed class FakeBootProfile : IGuestOsBootProfile
    {
        public GuestOsId Id { get; } = new("fakeos");

        public IReadOnlyList<ReadOnlyMemory<byte>> BootSectorSignatures { get; } = [new byte[] { 0xEA, 0x4C }];

        public IReadOnlyList<GuestOsBootTrap> MilestoneTraps { get; } =
        [
            new GuestOsBootTrap(0x4000, BootMilestoneKind.LoaderEntered),
            new GuestOsBootTrap(0x4003, BootMilestoneKind.KernelReady),
        ];
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent) =>
            this.Lines.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
    }

    private sealed class BlockDeviceCard : ISlotCard
    {
        public BlockDeviceCard()
        {
            var bytes = new byte[256];
            Array.Fill(bytes, (byte)0x60);
            bytes[0] = 0x4C;
            bytes[1] = 0x01;
            bytes[2] = 0x08;
            this.ROMRegion = new MaskedSlotRom(bytes);
        }

        public string Name => "Block Device";

        public string DeviceType => "block-device";

        public PeripheralKind Kind => PeripheralKind.SlotCard;

        public int SlotNumber { get; set; }

        public SlotIOHandlers? IOHandlers => null;

        public IBusTarget? ROMRegion { get; }

        public IBusTarget? ExpansionROMRegion => null;

        public void Initialize(IEventContext context) => ArgumentNullException.ThrowIfNull(context);

        public void Reset()
        {
        }

        public void OnExpansionROMSelected()
        {
        }

        public void OnExpansionROMDeselected()
        {
        }
    }

    /// <summary>
    /// Slot firmware whose read offset is the address inside the $C000 page. The low byte is the ROM index.
    /// </summary>
    private sealed class MaskedSlotRom : IBusTarget
    {
        private readonly byte[] bytes;

        public MaskedSlotRom(byte[] bytes) => this.bytes = bytes;

        public TargetCaps Capabilities => TargetCaps.SupportsPeek;

        public byte Read8(Addr offset, in BusAccess access)
        {
            _ = access;
            return this.bytes[offset & 0xFF];
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            _ = offset;
            _ = value;
            _ = access;
        }
    }

    private sealed class BareCard : ISlotCard
    {
        public string Name => "Bare Card";

        public string DeviceType => "bare";

        public PeripheralKind Kind => PeripheralKind.SlotCard;

        public int SlotNumber { get; set; }

        public SlotIOHandlers? IOHandlers => null;

        public IBusTarget? ROMRegion => null;

        public IBusTarget? ExpansionROMRegion => null;

        public void Initialize(IEventContext context) => ArgumentNullException.ThrowIfNull(context);

        public void Reset()
        {
        }

        public void OnExpansionROMSelected()
        {
        }

        public void OnExpansionROMDeselected()
        {
        }
    }
}