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
using BadMango.Emulator.Debug.Infrastructure.Status;
using BadMango.Emulator.Debug.Mcp;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.GuestOs;

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

    /// <summary>The boot log off installs no file tracker and prints no load line.</summary>
    [Test]
    public void BootLogOff_DoesNotTrackFileLoads()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        using var output = new StringWriter();

        Assert.That(machine.GetComponent<IFileLoadTracker>(), Is.Null);
        Assert.That(new OsCommand(Logger.None).Execute(Context(machine, output), ["files"]).Success, Is.True);
        Assert.That(output.ToString().Trim(), Is.EqualTo("File activity is off."));
        Assert.That(GuestOsStatusText.FormatMachineLine(machine), Does.Not.Contain("Load:"));
    }

    /// <summary><c>os on</c> installs one tracker and still registers no trap until a call returns.</summary>
    [Test]
    public void OsOn_InstallsFileTracker_WithoutReturnTraps()
    {
        using var off = MachineFactory.CreateMachine(CreateProfile(bootLog: false));
        using var output = new StringWriter();
        Assert.That(new OsCommand(Logger.None).Execute(Context(off, output), ["on"]).Success, Is.True);
        Assert.That(off.GetComponent<IFileLoadTracker>(), Is.Not.Null);
        Assert.That(Registry(off).IsEmpty, Is.True);

        using var on = MachineFactory.CreateMachine(CreateProfile(bootLog: true));
        var tracker = on.GetComponent<IFileLoadTracker>();
        using var again = new StringWriter();
        Assert.That(new OsCommand(Logger.None).Execute(Context(on, again), ["on"]).Success, Is.True);
        Assert.That(on.GetComponents<IFileLoadTracker>().Count(), Is.EqualTo(1));
        Assert.That(on.GetComponent<IFileLoadTracker>(), Is.SameAs(tracker));
    }

    /// <summary>A caller can feed the same row and line the MLI tracker uses.</summary>
    [Test]
    public void Report_FeedsTheSharedFileLine()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: true));
        var tracker = machine.GetComponent<IFileLoadTracker>()!;
        tracker.Report(new FileLoadProgress("PRODOS", 1024, 512, 512, 10, null, true));
        tracker.Report(new FileLoadProgress("PRODOS", 1024, 1024, 1024, 10, null, true));
        tracker.Report(new FileLoadProgress("A", null, 1, 1, 1, 1, true));
        tracker.Report(new FileLoadProgress("B", null, 2, 2, 2, 2, true));

        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files, Has.Count.EqualTo(3));
            Assert.That(tracker.Files[0].FormatLine(), Is.EqualTo("PRODOS 1024/1024 (100%)"));
            Assert.That(tracker.Files[1].BytesRead, Is.EqualTo(1u));
            Assert.That(tracker.Files[2].BytesRead, Is.EqualTo(2u));
            Assert.That(tracker.Current!.Value.Path, Is.EqualTo("B"));
            Assert.That(GuestOsStatusText.FormatMachineLine(machine), Does.Contain("Load: B 2"));
        });

        tracker.Clear();
        Assert.That(tracker.Files, Is.Empty);
        Assert.That(tracker.Current, Is.Null);
    }

    /// <summary>
    /// OPEN, GET_FILE_INFO, GET_EOF, READ, SET_MARK, and CLOSE track two refs.
    /// The return trap is observe-only and dies with the guest-OS session.
    /// </summary>
    [Test]
    public void ProDosMli_TracksEachOpenFile_AndKeepsTheGuestRunning()
    {
        using var machine = BootMachine();
        BootProDosToReady(machine);
        var cpu = machine.Cpu;
        var tracker = machine.GetComponent<IFileLoadTracker>()!;
        PlantMliStub(cpu);
        Poke(cpu, 0x1010, 0xA5);
        PlantPath(cpu, 0x0500, "BASIC.SYSTEM");
        PlantPath(cpu, 0x0510, "DRIVER");
        PlantPath(cpu, 0x0520, "/DISK/");
        Poke(cpu, 0x04F0, 0x01, 0x20, 0x05);
        Poke(cpu, 0x0400, 0x03, 0x00, 0x05, 0x00, 0x20, 0x00);
        Poke(cpu, 0x0430, 0x03, 0x10, 0x05, 0x00, 0x22, 0x00);
        Poke(cpu, 0x04E0, 0x0A, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
        Poke(cpu, 0x0460, 0x02, 0x01, 0x00, 0x00, 0x00);
        Poke(cpu, 0x0490, 0x04, 0x01, 0x00, 0x30, 0x00, 0x10, 0xFF, 0xFF);
        Poke(cpu, 0x04C0, 0x02, 0x01, 0x00, 0x00, 0x00);
        Poke(cpu, 0x04B0, 0x04, 0x02, 0x00, 0x40, 0x64, 0x00, 0xFF, 0xFF);
        Poke(cpu, 0x04D0, 0x01, 0x01);

        RunMli(machine, 0xC6, 0x04F0, [], 0);
        RunMli(machine, 0xC4, 0x04E0, [0x1E, 0x00], 0x04E8);
        var sawReturn = false;
        RunMli(machine, 0xC8, 0x0400, [0x01], 0x0405, () =>
        {
            sawReturn = true;
            var info = Registry(machine).GetTrapInfo(0x1006);
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Scope, Is.EqualTo(TrapScope.GuestOsSession));
            Assert.That(info.Value.Name, Is.EqualTo("MLI file return"));
            Assert.That(tracker.Files, Is.Empty);
        });

        var basic = tracker.Files[0];
        Assert.Multiple(() =>
        {
            Assert.That(sawReturn, Is.True);
            Assert.That(basic.Path, Is.EqualTo("/DISK/BASIC.SYSTEM"));
            Assert.That(basic.RefNum, Is.EqualTo((byte)1));
            Assert.That(basic.Size, Is.EqualTo(15360u));
            Assert.That(basic.BytesRead, Is.EqualTo(0u));
            Assert.That(cpu.Peek8(0x0405), Is.EqualTo(0x01));
        });

        RunMli(machine, 0xC4, 0x04E0, [0x28, 0x00], 0x04E8);
        Assert.That(tracker.Files[0].Size, Is.EqualTo(20480u));
        RunMli(machine, 0xD1, 0x0460, [0x00, 0x28, 0x00], 0x0462);
        Assert.That(tracker.Files[0].Size, Is.EqualTo(10240u));

        RunMli(machine, 0xC8, 0x0430, [0x02], 0x0435);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files, Has.Count.EqualTo(2));
            Assert.That(tracker.Files[1].Path, Is.EqualTo("/DISK/DRIVER"));
            Assert.That(tracker.Files[1].RefNum, Is.EqualTo((byte)2));
            Assert.That(tracker.Files[1].Size, Is.Null);
        });

        Poke(cpu, 0x0496, 0xFF, 0xFF);
        RunMli(machine, 0xCA, 0x0490, [0x00, 0x10], 0x0496);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files[0].BytesRead, Is.EqualTo(4096u));
            Assert.That(tracker.Files[0].Position, Is.EqualTo(4096u));
            Assert.That(tracker.Files[1].BytesRead, Is.EqualTo(0u));
        });

        RunMli(machine, 0xCE, 0x04C0, [], 0);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files[0].Position, Is.EqualTo(0u));
            Assert.That(tracker.Files[0].BytesRead, Is.EqualTo(4096u));
        });

        Poke(cpu, 0x0496, 0xFF, 0xFF);
        RunMli(machine, 0xCA, 0x0490, [0x00, 0x0A], 0x0496);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files[0].FormatLine(), Is.EqualTo("BASIC.SYSTEM 6656/10240 (65%)"));
            Assert.That(tracker.Files[0].Position, Is.EqualTo(2560u));
            Assert.That(tracker.Files[0].BytesRead, Is.EqualTo(6656u));
        });

        Poke(cpu, 0x04B6, 0xFF, 0xFF);
        RunMli(machine, 0xCA, 0x04B0, [0x64, 0x00], 0x04B6);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files[1].FormatLine(), Is.EqualTo("DRIVER 100"));
            Assert.That(tracker.Files[0].BytesRead, Is.EqualTo(6656u));
            Assert.That(tracker.Current!.Value.RefNum, Is.EqualTo((byte)2));
        });

        RunMli(machine, 0xCC, 0x04D0, [], 0);
        Poke(cpu, 0x0496, 0xFF, 0xFF);
        RunMli(machine, 0xCA, 0x0490, [0xE7, 0x03], 0x0496);

        using var filesOutput = new StringWriter();
        Assert.That(new OsCommand(Logger.None).Execute(Context(machine, filesOutput), ["files"]).Success, Is.True);
        using var inspectionOutput = new StringWriter();
        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, inspectionOutput), Logger.None);
        var activity = (DebugEnvelope<FileActivitySnapshot>)tools.GetFileActivity();

        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files[0].BytesRead, Is.EqualTo(6656u));
            Assert.That(tracker.Files[0].Active, Is.False);
            Assert.That(tracker.Files[1].Active, Is.True);
            Assert.That(filesOutput.ToString().Trim(), Is.EqualTo("BASIC.SYSTEM 6656/10240 (65%)" + Environment.NewLine + "DRIVER 100"));
            Assert.That(GuestOsStatusText.FormatMachineLine(machine), Does.Contain("Load: DRIVER 100"));
            Assert.That(activity.Payload.Enabled, Is.True);
            Assert.That(activity.Payload.Files, Has.Count.EqualTo(2));
            Assert.That(activity.Payload.Files[0].Line, Is.EqualTo("BASIC.SYSTEM 6656/10240 (65%)"));
            Assert.That(activity.Payload.Files[1].Line, Is.EqualTo("DRIVER 100"));
            Assert.That(activity.Payload.Current!.Line, Is.EqualTo("DRIVER 100"));
            Assert.That(activity.Payload.Current.RefNum, Is.EqualTo((byte)2));
            Assert.That(cpu.Peek8(0x1010), Is.EqualTo(0xA5));
            Assert.That(Registry(machine).HasTrap(0x1006), Is.False);
        });

        JumpToFirmware(machine);
        Assert.Multiple(() =>
        {
            Assert.That(tracker.Files, Is.Empty);
            Assert.That(tracker.Current, Is.Null);
            Assert.That(Registry(machine).HasTrap(0x1006), Is.False);
        });
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

    private static IMachine BootMachine(bool bootLog = true)
    {
        var machine = MachineFactory.CreateMachine(CreateProfile(bootLog));
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

    private static void RunMli(IMachine machine, byte command, ushort parameter, byte[] written, ushort writtenAt, Action? afterEntry = null)
    {
        var cpu = machine.Cpu;
        const ushort site = 0x1000;
        const ushort returnAddress = 0x1006;
        Poke(cpu, site, 0x20, 0x00, 0xBF, command, (byte)parameter, (byte)(parameter >> 8), 0xEA);
        Poke(cpu, 0x0020, (byte)written.Length);
        if (written.Length > 0)
        {
            Poke(cpu, 0x0030, written);
        }

        Poke(cpu, 0x0021, (byte)writtenAt, (byte)(writtenAt >> 8));
        Poke(cpu, 0x0023, (byte)(returnAddress & 0xFF), (byte)(returnAddress >> 8));
        cpu.Registers.X.SetByte(0x5A);
        cpu.SetPC(site);
        machine.Step();
        machine.Step();
        afterEntry?.Invoke();

        var guard = 0;
        while (cpu.GetPC() != returnAddress + 1 && guard < 48)
        {
            machine.Step();
            guard++;
        }

        Assert.That(cpu.GetPC(), Is.EqualTo((uint)(returnAddress + 1)), $"MLI ${command:X2} did not return");
        Assert.That(cpu.Registers.X.GetByte(), Is.EqualTo(0x5A));
        Assert.That(Registry(machine).HasTrap(returnAddress), Is.False);
    }

    private static void PlantMliStub(ICpu cpu)
    {
        // $BF00 stays a JMP so the global page still looks like ProDOS. The stub copies
        // $20 bytes from $0030 to ($21) and JMPs through ($23). Count zero copies nothing.
        Poke(cpu, 0xBF00, 0x4C, 0x10, 0xBF);
        Poke(cpu, 0xBF10, 0xA0, 0x00, 0xC4, 0x20, 0xF0, 0x08, 0xB9, 0x30, 0x00, 0x91, 0x21, 0xC8, 0xD0, 0xF4, 0x6C, 0x23, 0x00);
    }

    private static void PlantPath(ICpu cpu, ushort address, string text)
    {
        var bytes = new byte[text.Length + 1];
        bytes[0] = (byte)text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i + 1] = (byte)text[i];
        }

        Poke(cpu, address, bytes);
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

    private static void Poke(ICpu cpu, ushort address, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            cpu.Poke8((uint)(address + i), bytes[i]);
        }
    }

    private static MachineProfile CreateProfile(bool bootLog, bool highRom = false)
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
            GuestOs = new GuestOsProfile { Detect = bootLog, BootLog = bootLog },
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