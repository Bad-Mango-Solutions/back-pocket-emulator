// <copyright file="ProDosClockTrapTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.GuestOs;
using BadMango.Unit.Components;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Cycle-level coverage for the slotless ProDOS clock. Fixtures are poked bytes, not disk images.
/// </summary>
[TestFixture]
public class ProDosClockTrapTests
{
    private const ushort Program = 0x0300;
    private const ushort DateLo = 0xBF90;
    private const ushort MachId = 0xBF98;
    private const ushort Firmware = 0xC600;
    private const int Slot = 6;

    /// <summary>
    /// Verifies that a machine without the device registers neither <c>$BF06</c> nor <c>$BF00</c>.
    /// </summary>
    [Test]
    public void DeviceAbsent_RegistersNoDateTimeOrMliTrap()
    {
        using IMachine machine = Build(includeClock: false, detect: false, bootLog: false);
        ITrapRegistry registry = Registry(machine);

        Assert.Multiple(() =>
        {
            Assert.That(machine.GetComponent<ProDosClockDevice>(), Is.Null);
            Assert.That(registry.HasTrap(0xBF06), Is.False);
            Assert.That(registry.HasTrap(0xBF00), Is.False);
        });
    }

    /// <summary>
    /// Verifies that <c>enabled: false</c> leaves the device and its traps out of the machine.
    /// </summary>
    [Test]
    public void DeviceDisabled_RegistersNoTrap()
    {
        using IMachine machine = Build(includeClock: true, clockEnabled: false, detect: true, bootLog: false);

        Assert.Multiple(() =>
        {
            Assert.That(machine.GetComponent<ProDosClockDevice>(), Is.Null);
            Assert.That(Registry(machine).HasTrap(0xBF06), Is.False);
        });
    }

    /// <summary>
    /// Verifies that a valid <c>RTS</c> page is ignored until ProDOS is detected, then
    /// <c>JSR $BF06</c> writes DATE/TIME, sets MACHID, and returns with the documented cycles.
    /// </summary>
    [Test]
    public void DetectedRts_JsrDateTime_WritesDateTimeAndReturns()
    {
        using JsonDocument config = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00"}""");
        using IMachine machine = Build(includeClock: true, detect: true, bootLog: false, clockConfig: config.RootElement);
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantPage(cpu, 0x60);
        cpu.Poke8(0x0200, 0xA5);
        cpu.Poke8(0xBF94, 0x5A);

        Assert.That(Registry(machine).HasTrap(0xBF06), Is.False, "The trap waits for a ProDOS session.");
        machine.GetComponent<IGuestOsDetector>()!.Detect();
        Assert.That(Registry(machine).HasTrap(0xBF06), Is.True);

        var frozen = new DateTime(1986, 6, 15, 10, 30, 0);
        ProDosDateTime packed = ProDosDateTime.Pack(frozen);
        byte sp = cpu.Registers.SP.GetByte();
        WriteProgram(cpu, Program, 0x20, 0x06, 0xBF, 0x00);
        cpu.SetPC(Program);

        CpuStepResult jsr = machine.Step();
        CpuStepResult dateTime = machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(jsr.CyclesConsumed.Value, Is.EqualTo(6), "JSR $BF06.");
            Assert.That(dateTime.CyclesConsumed.Value, Is.EqualTo(ProDosClockDevice.DateTimeTrapCycles + 6));
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u));
            Assert.That(cpu.Registers.SP.GetByte(), Is.EqualTo(sp));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(packed.DateLo));
            Assert.That(cpu.Read8(DateLo + 1), Is.EqualTo(packed.DateHi));
            Assert.That(cpu.Read8(0xBF92), Is.EqualTo(packed.Minute));
            Assert.That(cpu.Read8(0xBF93), Is.EqualTo(packed.Hour));
            Assert.That(cpu.Read8(MachId) & 0x01, Is.EqualTo(1));
            Assert.That(cpu.Read8(0x0200), Is.EqualTo(0xA5), "$0200 is left alone.");
            Assert.That(cpu.Read8(0xBF94), Is.EqualTo(0x5A));
            Assert.That(cpu.Read8(0xBF06), Is.EqualTo(0x60), "The RTS opcode stays in memory.");
        });
    }

    /// <summary>
    /// Verifies that an absent global page executes the byte at <c>$BF06</c>.
    /// </summary>
    [Test]
    public void NotProDos_ExecutesTheByteAtBf06()
    {
        using IMachine machine = Build(includeClock: true, detect: true, bootLog: false);
        Power(machine);
        ICpu cpu = machine.Cpu;
        cpu.Poke8(0xBF06, 0x60);
        cpu.Poke8(DateLo, 0x11);
        machine.GetComponent<IGuestOsDetector>()!.Detect();

        Assert.That(Registry(machine).HasTrap(0xBF06), Is.False);
        WriteProgram(cpu, Program, 0x20, 0x06, 0xBF, 0x00);
        cpu.SetPC(Program);
        machine.Step();
        CpuStepResult rts = machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(rts.CyclesConsumed.Value, Is.EqualTo(6), "The real RTS runs.");
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(0x11));
        });
    }

    /// <summary>
    /// Verifies that a DOS 3.3 vector page does not install the ProDOS clock trap.
    /// </summary>
    [Test]
    public void Dos33_DoesNotHandleBf06()
    {
        using IMachine machine = Build(includeClock: true, detect: true, bootLog: false);
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantDos(cpu);
        cpu.Poke8(0xBF06, 0x60);
        cpu.Poke8(DateLo, 0x22);
        GuestOsInfo detected = machine.GetComponent<IGuestOsDetector>()!.Detect();

        Assert.Multiple(() =>
        {
            Assert.That(detected.Id, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(Registry(machine).HasTrap(0xBF06), Is.False);
        });

        WriteProgram(cpu, Program, 0x20, 0x06, 0xBF, 0x00);
        cpu.SetPC(Program);
        machine.Step();
        CpuStepResult rts = machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(rts.CyclesConsumed.Value, Is.EqualTo(6));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(0x22));
        });
    }

    /// <summary>
    /// Verifies that a ProDOS <c>JMP</c> driver runs and the slotless trap is not invoked.
    /// </summary>
    [Test]
    public void CardJmp_RunsTheDriver_SlotlessTrapIsNotInvoked()
    {
        using JsonDocument cardConfig = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00"}""");
        using IMachine machine = Build(
            includeClock: true,
            detect: true,
            bootLog: false,
            pocketWatch: true,
            cardConfig: cardConfig.RootElement);
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantPage(cpu, 0x4C, driver: 0xD000);
        WriteProgram(cpu, 0xD000, 0x20, 0x08, 0xC4, 0x60);
        cpu.Poke8(DateLo, 0x11);
        machine.GetComponent<IGuestOsDetector>()!.Detect();

        var names = new List<string>();
        ITrapRegistryObserver observer = machine.GetComponent<ITrapRegistryObserver>()!;
        observer.TrapInvoked += (info, _, _) => names.Add(info.Name);

        Assert.That(Registry(machine).HasTrap(0xBF06), Is.False);
        WriteProgram(cpu, Program, 0x20, 0x06, 0xBF, 0x00);
        cpu.SetPC(Program);
        for (var step = 0; step < 6 && cpu.GetPC() != 0x0303u; step++)
        {
            machine.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u), "The card driver returns to the caller.");
            Assert.That(ReadAscii(cpu, 0x0200, 14), Is.EqualTo("06,00,15,10,30"));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(0x11), "The slotless clock does not write DATE.");
            Assert.That(names, Does.Contain("PW_READ"));
            Assert.That(names, Does.Not.Contain("ProDOS DATETIME"));
        });
    }

    /// <summary>
    /// Verifies that RAMRD makes the trap return not-handled so the auxiliary byte runs.
    /// </summary>
    [Test]
    public void AuxRamRd_DoesNotHandle()
    {
        using IMachine machine = Build(includeClock: true, detect: true, bootLog: false, extended80: true);
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantPage(cpu, 0x60);
        cpu.Poke8(DateLo, 0x33);
        machine.GetComponent<IGuestOsDetector>()!.Detect();
        Assert.That(Registry(machine).HasTrap(0xBF06), Is.True);

        cpu.Write8(0xC005, 0);
        cpu.Write8(0xC003, 0);
        cpu.Write8(Program, 0x20);
        cpu.Write8(Program + 1, 0x06);
        cpu.Write8(Program + 2, 0xBF);
        cpu.Write8(Program + 3, 0x00);
        cpu.Write8(0xBF06, 0x60);
        cpu.Write8(0xC004, 0);
        Assert.That(machine.GetComponent<Extended80ColumnDevice>()!.IsRamRdEnabled, Is.True);

        var handled = new List<bool>();
        machine.GetComponent<ITrapRegistryObserver>()!.TrapInvoked += (info, result, _) =>
        {
            if (info.Name == "ProDOS DATETIME")
            {
                handled.Add(result.Handled);
            }
        };

        cpu.SetPC(Program);
        machine.Step();
        CpuStepResult dateTime = machine.Step();
        cpu.Write8(0xC002, 0);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.EqualTo(new[] { false }));
            Assert.That(dateTime.CyclesConsumed.Value, Is.EqualTo(6), "Auxiliary RTS runs.");
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(0x33));
        });
    }

    /// <summary>
    /// Verifies that removing a slot card while the machine is off leaves the slotless trap.
    /// </summary>
    [Test]
    public void RemoveSlotCard_LeavesTheSessionTrap()
    {
        using IMachine machine = Build(includeClock: true, detect: true, bootLog: false);
        Power(machine);
        PlantPage(machine.Cpu, 0x60);
        machine.GetComponent<IGuestOsDetector>()!.Detect();
        Assert.That(Registry(machine).HasTrap(0xBF06), Is.True);

        machine.PowerOffAsync().GetAwaiter().GetResult();
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));
        machine.InsertSlotCard(4, new PocketWatchCard(new PocketWatchConfig()));
        Assert.That(Registry(machine).HasTrap(0xC408), Is.True);

        machine.RemoveSlotCard(4);

        Assert.Multiple(() =>
        {
            Assert.That(Registry(machine).HasTrap(0xBF06), Is.True);
            Assert.That(Registry(machine).HasTrap(0xC408), Is.False);
            Assert.That(Registry(machine).HasTrap(0xC40B), Is.False);
        });
    }

    /// <summary>
    /// Verifies that a booted ProDOS session installs the trap on RTS, and the first MLI call
    /// sets MACHID. Reset clears that once-flag so the next MLI call sets it again.
    /// </summary>
    [Test]
    public void BootReady_MliCallSetsMachId_AndResetArmsTheNextCall()
    {
        using IMachine machine = BootMachine();
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantBoot(cpu, dateTimeOpcode: 0x60);
        cpu.SetPC(Firmware);
        for (var step = 0; step < 6; step++)
        {
            machine.Step();
        }

        BootSequenceMonitor monitor = machine.GetComponent<BootSequenceMonitor>()!;
        Assert.Multiple(() =>
        {
            Assert.That(monitor.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(Registry(machine).HasTrap(0xBF06), Is.True);
            Assert.That(cpu.Peek8(MachId) & 0x01, Is.EqualTo(0), "MACHID waits for the first MLI call.");
        });

        cpu.SetPC(Program);
        machine.Step();
        machine.Step();
        Assert.That(cpu.Peek8(MachId) & 0x01, Is.EqualTo(1));

        cpu.Poke8(MachId, 0x00);
        machine.Reset();
        Assert.That(cpu.Peek8(MachId) & 0x01, Is.EqualTo(0));
        cpu.SetPC(Program);
        machine.Step();
        machine.Step();
        Assert.That(cpu.Peek8(MachId) & 0x01, Is.EqualTo(1), "The next MLI call sets the bit again.");
    }

    /// <summary>
    /// Verifies that a card's JMP defers, and ProDOS storing RTS is picked up on the next MLI call.
    /// </summary>
    [Test]
    public void BootJmp_RtsStoreOnMliCall_InstallsTheTrap()
    {
        using JsonDocument config = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00"}""");
        using IMachine machine = BootMachine(config.RootElement);
        Power(machine);
        ICpu cpu = machine.Cpu;
        PlantBoot(cpu, dateTimeOpcode: 0x4C);
        cpu.SetPC(Firmware);
        for (var step = 0; step < 6; step++)
        {
            machine.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(machine.GetComponent<BootSequenceMonitor>()!.Stage, Is.EqualTo(GuestOsBootStage.Ready));
            Assert.That(Registry(machine).HasTrap(0xBF06), Is.False, "JMP means the card driver owns DATETIME.");
        });

        cpu.Poke8(0xBF06, 0x60);
        cpu.SetPC(Program);
        machine.Step();
        machine.Step();
        Assert.That(Registry(machine).HasTrap(0xBF06), Is.True, "The MLI call sees the stored RTS.");

        WriteProgram(cpu, 0x0310, 0x20, 0x06, 0xBF, 0x00);
        cpu.SetPC(0x0310);
        machine.Step();
        machine.Step();
        ProDosDateTime packed = ProDosDateTime.Pack(new DateTime(1986, 6, 15, 10, 30, 0));

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0313u));
            Assert.That(cpu.Read8(DateLo), Is.EqualTo(packed.DateLo));
            Assert.That(cpu.Read8(0xBF93), Is.EqualTo(packed.Hour));
        });
    }

    /// <summary>
    /// Verifies the profile warning when a clock card is installed beside the slotless clock.
    /// </summary>
    [Test]
    public void ClockCardPresent_LogsAWarning()
    {
        var sink = new CaptureSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var clock = new ProDosClockDevice(logger);
        using JsonDocument cardConfig = JsonDocument.Parse("""{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00"}""");
        using IMachine machine = new MachineBuilder()
            .RegisterCompositeIOHandler()
            .RegisterStandardDeviceFactories()
            .AddComponent(clock)
            .AddDevice(clock)
            .FromProfile(Profile(
                includeClock: false,
                detect: false,
                bootLog: false,
                pocketWatch: true,
                cardConfig: cardConfig.RootElement))
            .WithCpuFactory(context => new BadMango.Emulator.Emulation.Cpu.Cpu65C02(context))
            .Build();

        Assert.That(machine.GetComponent<ProDosClockDevice>(), Is.SameAs(clock));
        Assert.That(
            sink.Lines,
            Has.Some.Contains("clock card"));
    }

    private static IMachine BootMachine(JsonElement? clockConfig = null)
    {
        IMachine machine = Build(includeClock: true, detect: true, bootLog: true, clockConfig: clockConfig);
        machine.InsertSlotCard(Slot, new BlockDeviceCard());
        return machine;
    }

    private static IMachine Build(
        bool includeClock,
        bool detect,
        bool bootLog,
        bool clockEnabled = true,
        bool extended80 = false,
        bool pocketWatch = false,
        JsonElement? clockConfig = null,
        JsonElement? cardConfig = null)
    {
        return MachineFactory.CreateMachine(Profile(
            includeClock,
            detect,
            bootLog,
            clockEnabled,
            extended80,
            pocketWatch,
            clockConfig,
            cardConfig));
    }

    private static MachineProfile Profile(
        bool includeClock,
        bool detect,
        bool bootLog,
        bool clockEnabled = true,
        bool extended80 = false,
        bool pocketWatch = false,
        JsonElement? clockConfig = null,
        JsonElement? cardConfig = null)
    {
        var motherboard = new List<MotherboardDeviceEntry>();
        if (extended80)
        {
            motherboard.Add(new MotherboardDeviceEntry { Type = "extended80column", Enabled = true });
        }

        if (includeClock)
        {
            motherboard.Add(new MotherboardDeviceEntry
            {
                Type = "prodos-clock",
                Name = "ProDOS Clock",
                Enabled = clockEnabled,
                Config = clockConfig,
            });
        }

        var cards = new List<SlotCardProfile>();
        if (pocketWatch)
        {
            cards.Add(new SlotCardProfile { Slot = 4, Type = "pocketwatch", Config = cardConfig });
        }

        return new MachineProfile
        {
            Name = "prodos-clock",
            DisplayName = "ProDOS clock",
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
                Motherboard = motherboard,
                Slots = new SlotSystemProfile { Enabled = true, Cards = cards },
            },
            GuestOs = new GuestOsProfile { Detect = detect, BootLog = bootLog },
        };
    }

    private static void Power(IMachine machine) => machine.PowerOnAsync().GetAwaiter().GetResult();

    private static ITrapRegistry Registry(IMachine machine) => machine.GetComponent<ITrapRegistry>()!;

    private static void PlantPage(ICpu cpu, byte dateTimeOpcode, ushort driver = 0xD000)
    {
        cpu.Poke8(0xBF00, 0x4C);
        cpu.Poke8(0xBF06, dateTimeOpcode);
        cpu.Poke8(0xBF07, (byte)driver);
        cpu.Poke8(0xBF08, (byte)(driver >> 8));
        cpu.Poke8(MachId, 0x00);
        cpu.Poke8(0xBFFD, 0x00);
        cpu.Poke8(0xBFFF, 0x24);
        cpu.Poke8(DateLo, 0x00);
        cpu.Poke8(DateLo + 1, 0x00);
        cpu.Poke8(0xBF92, 0x00);
        cpu.Poke8(0xBF93, 0x00);
    }

    private static void PlantBoot(ICpu cpu, byte dateTimeOpcode)
    {
        WriteProgram(cpu, 0x0800, 0x01, 0x38, 0xB0, 0x03, 0xEA, 0xEA, 0xEA, 0x4C, 0x00, 0x20);
        WriteProgram(cpu, 0x2000, 0x4C, 0x00, 0x20);
        WriteProgram(cpu, Program, 0x20, 0x00, 0xBF, 0xC8, 0x00, 0x00, 0x00);
        PlantPage(cpu, dateTimeOpcode);
        cpu.Poke8(0xBF00, 0x4C);
        cpu.Poke8(0xBF01, 0x00);
        cpu.Poke8(0xBF02, 0xBF);
    }

    private static void PlantDos(ICpu cpu)
    {
        PlantJmp(cpu, 0x03D0, 0x9DBF);
        PlantJmp(cpu, 0x03D3, 0x9D84);
        PlantJmp(cpu, 0x03D6, 0xAAFD);
        PlantJmp(cpu, 0x03D9, 0xB7B5);
        PlantJmp(cpu, 0x03EA, 0xA851);
    }

    private static void PlantJmp(ICpu cpu, ushort address, ushort target)
    {
        WriteProgram(cpu, address, 0x4C, (byte)target, (byte)(target >> 8));
    }

    private static void WriteProgram(ICpu cpu, ushort address, params byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            cpu.Poke8((Addr)(address + index), bytes[index]);
        }
    }

    private static string ReadAscii(ICpu cpu, ushort address, int length)
    {
        var chars = new char[length];
        for (var index = 0; index < length; index++)
        {
            chars[index] = (char)(cpu.Read8((Addr)(address + index)) & 0x7F);
        }

        return new string(chars);
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent) => Lines.Add(logEvent.RenderMessage());
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
            ROMRegion = new MaskedSlotRom(bytes);
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

    private sealed class MaskedSlotRom : IBusTarget
    {
        private readonly byte[] bytes;

        public MaskedSlotRom(byte[] bytes) => this.bytes = bytes;

        public TargetCaps Capabilities => TargetCaps.SupportsPeek;

        public byte Read8(Addr offset, in BusAccess access)
        {
            _ = access;
            return bytes[offset & 0xFF];
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            _ = offset;
            _ = value;
            _ = access;
        }
    }
}