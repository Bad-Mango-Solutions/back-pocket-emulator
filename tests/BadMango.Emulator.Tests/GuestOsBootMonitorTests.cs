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

        var output = new StringWriter();
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
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
            }));
            Assert.That(monitor.Milestones[0].Detail, Is.EqualTo("slot 6: Block Device"));
            Assert.That(monitor.Milestones[1].Detail, Is.EqualTo("DOS 3.3"));
            Assert.That(monitor.Milestones[1].Os, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(monitor.Milestones[2].Detail, Is.EqualTo("DOS 3.3 Boot 2 at $B700"));
            Assert.That(monitor.Milestones[2].Pc, Is.EqualTo(0xB700u));
            Assert.That(monitor.Milestones[3].Detail, Is.EqualTo("DOS 3.3 cold start"));
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
            Assert.That(registry.HasTrap(Firmware), Is.False);
            Assert.That(registry.HasTrap(0xB700), Is.False);
            Assert.That(registry.HasTrap(0x9D84), Is.False);
        });

        var bootOutput = new StringWriter();
        var command = new OsCommand(Logger.None);
        Assert.That(command.Execute(Context(machine, bootOutput), ["boot"]).Success, Is.True);
        Assert.That(bootOutput.ToString(), Does.Contain("DOS 3.3 cold start"));

        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, new StringWriter()), Logger.None);
        var boot = (DebugEnvelope<BootLogSnapshot>)tools.GetBootLog();
        Assert.That(boot.Payload.Milestones, Has.Count.EqualTo(logged));
        Assert.That(boot.Payload.Stage, Is.EqualTo(nameof(GuestOsBootStage.Ready)));

        machine.Reset();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.None));
            Assert.That(monitor.Milestones, Is.Empty);
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
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
                BootMilestoneKind.SystemProgramStarted,
                BootMilestoneKind.FirstSystemCall,
            }));
            Assert.That(monitor.Milestones[2].Detail, Is.EqualTo("ProDOS loader"));
            Assert.That(monitor.Milestones[2].Pc, Is.EqualTo(0x2000u));
            Assert.That(monitor.Milestones[3].At, Is.EqualTo(monitor.Milestones[4].At));
            Assert.That(monitor.Milestones[5].Detail, Is.EqualTo("MLI $C8 OPEN"));
            Assert.That(monitor.Milestones[5].Pc, Is.EqualTo(0xBF00u));
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
            Assert.That(registry.HasTrap(Firmware), Is.False);
            Assert.That(registry.HasTrap(0x0801), Is.False);
            Assert.That(OperatingSystemTraps(registry), Is.Empty);
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
                BootMilestoneKind.BootFirmwareEntered,
                BootMilestoneKind.BootSectorLoaded,
                BootMilestoneKind.LoaderEntered,
                BootMilestoneKind.KernelReady,
            }));
            Assert.That(monitor.Milestones[2].Pc, Is.EqualTo(0x4000u));
            Assert.That(monitor.Milestones[3].Pc, Is.EqualTo(0x4003u));
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Unknown));
            Assert.That(
                sink.Lines.Exists(line =>
                    line.Contains("Boot BootFirmwareEntered at $C600 (cycle ", StringComparison.Ordinal)
                    && line.Contains("slot 6: Block Device", StringComparison.Ordinal)),
                Is.True);
        });
    }

    /// <summary>Memory that already holds ProDOS reaches Ready through the detector, with no boot milestones.</summary>
    [Test]
    public void NoBootMilestones_DetectorSetsReady()
    {
        using var machine = MachineFactory.CreateMachine(CreateProfile(bootLog: true));
        Power(machine);
        PlantProDosPage(machine.Cpu);
        machine.Cpu.SetPC(0x0300);
        Poke(machine.Cpu, 0x0300, 0xEA, 0xEA, 0xEA);
        machine.Step();
        machine.Step();
        machine.Step();
        var monitor = MonitorOf(machine);
        Assert.That(monitor.Milestones, Is.Empty);
        Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.None));

        var tools = new InspectionTools(new Mock<IDebugSession>().Object, Context(machine, new StringWriter()), Logger.None);
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

    private static IMachine BootMachine(bool bootLog = true)
    {
        var machine = MachineFactory.CreateMachine(CreateProfile(bootLog));
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

    private static void Poke(ICpu cpu, ushort address, params byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            cpu.Poke8((uint)(address + i), bytes[i]);
        }
    }

    private static MachineProfile CreateProfile(bool bootLog)
    {
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
                Regions =
                [
                    new MemoryRegionProfile
                    {
                        Name = "main-ram",
                        Type = "ram",
                        Start = "0x0000",
                        Size = "0xC000",
                        Permissions = "rwx",
                        Source = "main-ram",
                        SourceOffset = "0x0000",
                    },
                    new MemoryRegionProfile
                    {
                        Name = "io-region",
                        Type = "composite",
                        Start = "0xC000",
                        Size = "0x1000",
                        Permissions = "rw",
                        Handler = "composite-io",
                    },
                    new MemoryRegionProfile
                    {
                        Name = "vector-ram",
                        Type = "ram",
                        Start = "0xD000",
                        Size = "0x3000",
                        Permissions = "rwx",
                        Source = "vector-ram",
                        SourceOffset = "0x0000",
                    },
                ],
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