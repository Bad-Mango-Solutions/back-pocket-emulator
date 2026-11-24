// <copyright file="PocketWatchClockTrapTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using System.Text;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// ProDOS clock-driver traps for a profile-built PocketWatch card.
/// </summary>
[TestFixture]
public class PocketWatchClockTrapTests
{
    private const ushort ProgramAddress = 0x0300;
    private const ushort TimeBuffer = 0x0200;
    private const int TimeLength = 14;
    private const string FrozenInstant = "1986-06-15T10:30:00";
    private const string ExpectedTime = "06,00,15,10,30";
    private const byte ReadEntry = 0x08;
    private const byte WriteEntry = 0x0B;

    /// <summary>
    /// Verifies that <see cref="DeviceFactoryExtensions.RegisterPocketWatchCardFactory"/>
    /// installs the frozen time from the profile card config.
    /// </summary>
    [Test]
    public void RegisterPocketWatchCardFactory_FrozenConfig_InstallsConfiguredTime()
    {
        using var configDocument = JsonDocument.Parse(FrozenConfigJson());
        using var machine = new MachineBuilder()
            .RegisterCompositeIOHandler()
            .RegisterPocketWatchCardFactory()
            .FromProfile(CreateProfile(configDocument.RootElement, includeExtended80: false))
            .WithCpuFactory(context => new Cpu65C02(context))
            .Build();

        var card = InstalledCard(machine);

        AssertFrozenInstant(card);
    }

    /// <summary>
    /// Verifies that <c>JSR $C40B</c> then <c>JSR $C408</c> writes the latched ProDOS time at <c>$0200</c>.
    /// </summary>
    [Test]
    public void ClockEntries_WriteLatchedAsciiAt0200()
    {
        using var configDocument = JsonDocument.Parse(FrozenConfigJson());
        using var machine = MachineFactory.CreateMachine(
            CreateProfile(configDocument.RootElement, includeExtended80: true));
        machine.PowerOnAsync().GetAwaiter().GetResult();

        var cpu = machine.Cpu;
        var card = InstalledCard(machine);
        AssertFrozenInstant(card);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0xC400), Is.EqualTo(0x08), "Stub signature $Cn00 should stay in place.");
            Assert.That(cpu.Read8(0xC408), Is.EqualTo(0xEA), "READ entry should stay a NOP in the stub ROM.");
            Assert.That(cpu.Read8(0xC40B), Is.EqualTo(0xEA), "WRITE entry should stay a NOP in the stub ROM.");
            Assert.That(cpu.Read8(0xC4FB), Is.EqualTo(0x60), "Stub slide should still end in RTS.");
        });

        Fill(cpu, TimeBuffer, TimeLength + 1, 0x11);
        WriteProgram(cpu, 0x20, 0x0B, 0xC4, 0x20, 0x08, 0xC4, 0xDB);
        cpu.SetPC(ProgramAddress);

        machine.Step();
        machine.Step();
        Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u), "WRITE trap should return like RTS.");

        card.SetFixedTime(new(1999, 12, 31, 23, 59, 0));

        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0306u), "READ trap should return like RTS.");
            Assert.That(ReadAscii(cpu, TimeBuffer, TimeLength), Is.EqualTo(ExpectedTime));
            Assert.That(cpu.Read8(TimeBuffer + TimeLength), Is.EqualTo(0x11), "The trap should write only the 14-byte string.");
        });
    }

    /// <summary>
    /// Verifies that INTCXROM runs internal ROM at <c>$C408</c> and the READ trap does not fire.
    /// </summary>
    [Test]
    public void IntCxRom_ReadEntry_ExecutesInternalRom()
    {
        using var configDocument = JsonDocument.Parse(FrozenConfigJson());
        using var machine = MachineFactory.CreateMachine(
            CreateProfile(configDocument.RootElement, includeExtended80: true));

        var extended80 = machine.GetComponent<Extended80ColumnDevice>();
        Assert.That(extended80, Is.Not.Null);
        var internalPage = new byte[0x309];
        internalPage[0x308] = 0x60;
        extended80!.LoadExpansionRom(internalPage);

        machine.PowerOnAsync().GetAwaiter().GetResult();

        var cpu = machine.Cpu;
        var registry = machine.GetComponent<ITrapRegistry>();
        Assert.That(registry, Is.Not.Null);
        Assert.That(registry!.HasTrap(0xC408), Is.True, "The READ trap should be registered.");

        Fill(cpu, TimeBuffer, TimeLength, 0xA5);
        cpu.Write8(0xC007, 0);

        Assert.Multiple(() =>
        {
            Assert.That(extended80.IsIntCXRomEnabled, Is.True);
            Assert.That(cpu.Read8(0xC408), Is.EqualTo(0x60), "$C408 should be the internal ROM RTS.");
        });

        WriteProgram(cpu, 0x20, 0x08, 0xC4, 0xDB);
        cpu.SetPC(ProgramAddress);
        machine.Step();
        Assert.That(cpu.GetPC(), Is.EqualTo(0xC408u));
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0303u), "Internal RTS should return from the JSR.");
            for (var index = 0; index < TimeLength; index++)
            {
                Assert.That(cpu.Read8((Addr)(TimeBuffer + index)), Is.EqualTo(0xA5), "The READ trap should not write $0200.");
            }
        });
    }

    /// <summary>
    /// Verifies that removing a PocketWatch while the machine is off drops its traps,
    /// and inserting that same card in another slot registers them once at the new slot.
    /// </summary>
    [Test]
    public void RemoveAndReinsertSameCard_MovesTrapsToTheNewSlot()
    {
        using var machine = CreateOffMachine();
        var card = CreateFrozenCard();
        machine.InsertSlotCard(4, card);

        Power(machine);
        AssertClockTrapFires(machine, 4);
        machine.PowerOffAsync().GetAwaiter().GetResult();
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));

        machine.RemoveSlotCard(4);
        AssertNoPocketWatchTraps(machine);

        machine.InsertSlotCard(6, card);
        AssertPocketWatchTraps(machine, 6, exclusive: true);

        Power(machine);
        AssertClockTrapFires(machine, 6);
    }

    /// <summary>
    /// Verifies that a new PocketWatch inserted in a vacated slot registers its traps once.
    /// </summary>
    [Test]
    public void RemoveAndInsertNewCard_RegistersTrapsOnce()
    {
        using var machine = CreateOffMachine();
        machine.InsertSlotCard(4, CreateFrozenCard());

        Power(machine);
        AssertClockTrapFires(machine, 4);
        machine.PowerOffAsync().GetAwaiter().GetResult();

        machine.RemoveSlotCard(4);
        AssertNoPocketWatchTraps(machine);

        machine.InsertSlotCard(4, CreateFrozenCard());
        AssertPocketWatchTraps(machine, 4, exclusive: true);

        Power(machine);
        AssertClockTrapFires(machine, 4);
    }

    /// <summary>
    /// Verifies that removing a card from another slot leaves the PocketWatch traps in place.
    /// </summary>
    [Test]
    public void RemoveOtherSlot_LeavesPocketWatchTraps()
    {
        using var machine = CreateOffMachine();
        machine.InsertSlotCard(4, CreateFrozenCard());
        machine.InsertSlotCard(5, CreateFrozenCard());
        AssertPocketWatchTraps(machine, 4);
        AssertPocketWatchTraps(machine, 5);

        machine.RemoveSlotCard(5);

        AssertPocketWatchTraps(machine, 4, exclusive: true);
        AssertNoPocketWatchTraps(machine, 5);

        Power(machine);
        AssertClockTrapFires(machine, 4);
    }

    private static string FrozenConfigJson()
        => $$"""{"timeSource":"frozen","frozenTime":"{{FrozenInstant}}"}""";

    private static void AssertFrozenInstant(PocketWatchCard card)
    {
        Assert.Multiple(() =>
        {
            Assert.That(card.UseHostTime, Is.False);
            Assert.That(card.CurrentTime.Year, Is.EqualTo(1986));
            Assert.That(card.CurrentTime.Month, Is.EqualTo(6));
            Assert.That(card.CurrentTime.Day, Is.EqualTo(15));
            Assert.That(card.CurrentTime.Hour, Is.EqualTo(10));
            Assert.That(card.CurrentTime.Minute, Is.EqualTo(30));
        });
    }

    private static PocketWatchCard InstalledCard(IMachine machine)
    {
        var installed = machine.GetComponent<ISlotManager>()?.GetCard(4);
        Assert.That(installed, Is.InstanceOf<PocketWatchCard>());
        return (PocketWatchCard)installed!;
    }

    private static MachineProfile CreateProfile(
        JsonElement cardConfig,
        bool includeExtended80,
        bool includePocketWatch = true)
    {
        List<MotherboardDeviceEntry> motherboard = includeExtended80
            ? [new() { Type = "extended80column", Enabled = true }]
            : [];
        var cards = new List<SlotCardProfile>();
        if (includePocketWatch)
        {
            cards.Add(new()
            {
                Slot = 4,
                Type = "pocketwatch",
                Config = cardConfig,
            });
        }

        return new()
        {
            Name = "pocketwatch-clock",
            DisplayName = "PocketWatch clock",
            Cpu = new()
            {
                Type = "65C02",
                ClockSpeed = 1_000_000,
            },
            AddressSpace = 16,
            Memory = new()
            {
                Physical =
                [
                    new()
                    {
                        Name = "main-ram",
                        Size = "0xC000",
                        Fill = "0x00",
                    },
                    new()
                    {
                        Name = "vector-ram",
                        Size = "0x3000",
                        Fill = "0x00",
                    },
                ],
                Regions =
                [
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
                    new()
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
            Devices = new()
            {
                Motherboard = motherboard,
                Slots = new()
                {
                    Enabled = true,
                    Cards = cards,
                },
            },
        };
    }

    private static void WriteProgram(ICpu cpu, params byte[] program)
    {
        for (var index = 0; index < program.Length; index++)
        {
            cpu.Write8((Addr)(ProgramAddress + index), program[index]);
        }
    }

    private static void Fill(ICpu cpu, Addr address, int length, byte value)
    {
        for (var index = 0; index < length; index++)
        {
            cpu.Write8(address + (uint)index, value);
        }
    }

    private static string ReadAscii(ICpu cpu, Addr address, int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = cpu.Read8(address + (uint)index);
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static IMachine CreateOffMachine()
        => MachineFactory.CreateMachine(CreateProfile(default, includeExtended80: false, includePocketWatch: false));

    private static PocketWatchCard CreateFrozenCard()
        => new(new PocketWatchConfig
        {
            TimeSource = "frozen",
            FrozenTime = FrozenInstant,
        });

    private static void Power(IMachine machine)
        => machine.PowerOnAsync().GetAwaiter().GetResult();

    private static void AssertClockTrapFires(IMachine machine, int slot)
    {
        var cpu = machine.Cpu;
        Fill(cpu, TimeBuffer, TimeLength + 1, 0x11);
        byte high = (byte)(0xC0 + slot);
        WriteProgram(cpu, 0x20, 0x0B, high, 0x20, 0x08, high, 0xDB);
        cpu.SetPC(ProgramAddress);

        machine.Step();
        machine.Step();
        machine.Step();
        machine.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0306u));
            Assert.That(ReadAscii(cpu, TimeBuffer, TimeLength), Is.EqualTo(ExpectedTime));
            Assert.That(cpu.Read8(TimeBuffer + TimeLength), Is.EqualTo(0x11));
        });
    }

    private static void AssertPocketWatchTraps(IMachine machine, int slot, bool exclusive = false)
    {
        var traps = PocketWatchTraps(machine).ToList();
        var forSlot = traps.Where(trap => trap.SlotNumber == slot).ToList();
        Addr page = (Addr)(0xC000 + (slot << 8));
        Assert.Multiple(() =>
        {
            Assert.That(forSlot, Has.Count.EqualTo(2));
            Assert.That(forSlot.Any(trap => trap.Address == page + ReadEntry), Is.True);
            Assert.That(forSlot.Any(trap => trap.Address == page + WriteEntry), Is.True);
            if (exclusive)
            {
                Assert.That(traps, Has.Count.EqualTo(2));
            }
        });
    }

    private static void AssertNoPocketWatchTraps(IMachine machine, int? slot = null)
    {
        var registry = machine.GetComponent<ITrapRegistry>();
        Assert.That(registry, Is.Not.Null);
        if (slot is int removedSlot)
        {
            Addr page = (Addr)(0xC000 + (removedSlot << 8));
            Assert.Multiple(() =>
            {
                Assert.That(registry!.HasTrap(page + ReadEntry), Is.False);
                Assert.That(registry.HasTrap(page + WriteEntry), Is.False);
                Assert.That(PocketWatchTraps(machine).Any(trap => trap.SlotNumber == removedSlot), Is.False);
            });

            return;
        }

        Assert.That(PocketWatchTraps(machine), Is.Empty);
    }

    private static IEnumerable<TrapInfo> PocketWatchTraps(IMachine machine)
    {
        var registry = machine.GetComponent<ITrapRegistry>();
        Assert.That(registry, Is.Not.Null);
        return registry!.GetAllTraps().Where(trap => trap.Name.StartsWith("PW_", StringComparison.Ordinal));
    }
}