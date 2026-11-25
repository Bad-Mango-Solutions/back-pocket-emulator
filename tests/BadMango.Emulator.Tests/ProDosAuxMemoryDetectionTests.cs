// <copyright file="ProDosAuxMemoryDetectionTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Systems;

/// <summary>
/// ProDOS 2.4.3 auxiliary-memory checks on a synthetic 128K IIe.
/// </summary>
/// <remarks>
/// <para>
/// The relocator in the public ProDOS 2.0.3 <c>PROLDR.S</c> listing, and the same
/// instruction stream in ProDOS 2.4.3, decides whether <c>/RAM</c> is installed.
/// <c>MACHID</c> bits 5–4 become <c>11</c> only when the zero-page routine
/// <c>Test128</c> can read-modify-write <c>$0C00</c> and prove it is not a mirror of
/// <c>$0800</c> under RAMRD and RAMWRT. The slot-3 aux-card check is separate: with
/// 80STORE and PAGE2 on, it read-modify-writes the text-page hole at <c>$0478</c> and
/// only updates <c>MACHID</c> bit 1. Neither routine reads a disk.
/// </para>
/// <para>
/// On a real IIe, ALTZP also selects the auxiliary language-card RAM. ProDOS stores
/// the IRQ vector at <c>$FFFE</c> in that bank, and the <c>/RAM</c> driver keeps
/// blocks <c>$60</c>–<c>$7F</c> there (<c>RAM0.S</c>). The programs below are
/// hand-assembled. No ROM or operating-system image is loaded.
/// </para>
/// </remarks>
[TestFixture]
public class ProDosAuxMemoryDetectionTests
{
    private const FastPathOps RamAllOps = FastPathOps.Read | FastPathOps.Write | FastPathOps.Fetch;

    private const ushort TextHole = 0x0478;

    private const ushort AuxProbeLow = 0x0800;

    private const ushort AuxProbeHigh = 0x0C00;

    private const ushort CardResult = 0x02FE;

    private const ushort MemoryResult = 0x02FF;

    /// <summary>
    /// ProDOS 2.4.3 <c>Test128</c>, relocated to <c>$0080</c> the way <c>PROLDR</c> does.
    /// The six bytes after <c>BCS</c> are padding. Carry clear means the two probes differed.
    /// </summary>
    private static readonly byte[] Test128AtZeroPage =
    [
        0x85, 0x0C,
        0x10, 0x35,
        0xA9, 0xEE,
        0x8D, 0x05, 0xC0,
        0x8D, 0x03, 0xC0,
        0x8D, 0x00, 0x0C,
        0x8D, 0x00, 0x08,
        0xAD, 0x00, 0x0C,
        0xC9, 0xEE,
        0xD0, 0x0E,
        0x0E, 0x00, 0x0C,
        0x0A,
        0xCD, 0x00, 0x0C,
        0xD0, 0x05,
        0xCD, 0x00, 0x08,
        0xD0, 0x03,
        0x38,
        0xB0, 0x01,
        0x18,
        0x8D, 0x04, 0xC0,
        0x8D, 0x02, 0xC0,
        0xB0, 0x06,
        0xEA, 0xEA, 0xEA, 0xEA, 0xEA, 0xEA,
        0x90, 0x06,
        0xA9, 0x00,
        0x8D, 0xFF, 0x02,
        0xDB,
        0xA9, 0x01,
        0x8D, 0xFF, 0x02,
        0xDB,
    ];

    /// <summary>
    /// The slot-3 aux-card check from ProDOS 2.4.3 (<c>STA $C00A</c>, <c>STA $C001</c>,
    /// <c>STA $C055</c>, then <c>$EE</c> shifted at <c>$0478</c>), then a jump into <c>Test128</c>
    /// with the IIe bit set.
    /// </summary>
    private static readonly byte[] AuxCardProgram =
    [
        0x8D, 0x0A, 0xC0,
        0x8D, 0x01, 0xC0,
        0x8D, 0x55, 0xC0,
        0xA9, 0xEE,
        0x8D, 0x78, 0x04,
        0x0A,
        0x0E, 0x78, 0x04,
        0xCD, 0x78, 0x04,
        0xD0, 0x07,
        0x4A,
        0x4E, 0x78, 0x04,
        0xCD, 0x78, 0x04,
        0x8D, 0x54, 0xC0,
        0x8D, 0x00, 0xC0,
        0xF0, 0x07,
        0xA9, 0x00,
        0x8D, 0xFE, 0x02,
        0x80, 0x05,
        0xA9, 0x01,
        0x8D, 0xFE, 0x02,
        0xA9, 0xA0,
        0x4C, 0x80, 0x00,
    ];

    /// <summary>
    /// Writes main bank 2, auxiliary bank 2, and auxiliary bank 1, then checks main again.
    /// </summary>
    private static readonly byte[] AltZpProgram =
    [
        0xAD, 0x83, 0xC0,
        0xAD, 0x83, 0xC0,
        0xA9, 0x11,
        0x8D, 0x00, 0xD0,
        0xA9, 0x22,
        0x8D, 0x00, 0xE0,
        0x8D, 0x09, 0xC0,
        0xA9, 0x33,
        0x8D, 0x00, 0xD0,
        0xA9, 0x44,
        0x8D, 0x00, 0xE0,
        0xAD, 0x8B, 0xC0,
        0xAD, 0x8B, 0xC0,
        0xA9, 0x55,
        0x8D, 0x00, 0xD0,
        0xAD, 0x00, 0xD0,
        0x8D, 0xF0, 0x02,
        0xAD, 0x00, 0xE0,
        0x8D, 0xF1, 0x02,
        0x8D, 0x08, 0xC0,
        0xAD, 0x00, 0xD0,
        0x8D, 0xF2, 0x02,
        0xAD, 0x00, 0xE0,
        0x8D, 0xF3, 0x02,
        0xAD, 0x83, 0xC0,
        0xAD, 0x83, 0xC0,
        0xAD, 0x00, 0xD0,
        0x8D, 0xF4, 0x02,
        0x8D, 0x09, 0xC0,
        0xAD, 0x00, 0xD0,
        0x8D, 0xF5, 0x02,
        0xA9, 0x66,
        0x85, 0x00,
        0xA9, 0x77,
        0x8D, 0x00, 0x01,
        0x8D, 0x08, 0xC0,
        0xA5, 0x00,
        0x8D, 0xF6, 0x02,
        0xAD, 0x00, 0x01,
        0x8D, 0xF7, 0x02,
        0x8D, 0x09, 0xC0,
        0xA5, 0x00,
        0x8D, 0xF8, 0x02,
        0xAD, 0x00, 0x01,
        0x8D, 0xF9, 0x02,
        0x8D, 0x08, 0xC0,
        0xDB,
    ];

    /// <summary>
    /// 80STORE and PAGE2 send the ProDOS text-page hole to auxiliary RAM, including a
    /// read-modify-write, and turning both switches off restores main RAM.
    /// </summary>
    [Test]
    public void EightyStoreAndPage2_RouteTextHoleToAux_AndOffRestoresMain()
    {
        using IMachine machine = BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux);
        Assert.That(machine.State, Is.EqualTo(MachineState.Off), "Banking is tested with the machine off");

        cpu.Write8(TextHole, 0x11);
        aux.WriteAuxRam(TextHole, 0x22);

        cpu.Write8(0xC001, 0);
        cpu.Write8(0xC055, 0);
        cpu.Write8(TextHole, 0xEE);
        byte shifted = cpu.Read8(TextHole);
        cpu.Write8(TextHole, (byte)(shifted << 1));

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(TextHole), Is.EqualTo(0xDC), "ASL of $EE in the aux text page");
            Assert.That(aux.ReadAuxRam(TextHole), Is.EqualTo(0xDC), "The shifted byte is in auxiliary RAM");
        });

        cpu.Write8(0xC054, 0);
        cpu.Write8(0xC000, 0);

        Assert.Multiple(() =>
        {
            Assert.That(aux.Is80StoreEnabled, Is.False);
            Assert.That(aux.IsPage2Selected, Is.False);
            Assert.That(cpu.Read8(TextHole), Is.EqualTo(0x11), "80STORE off reads main RAM again");
            Assert.That(aux.ReadAuxRam(TextHole), Is.EqualTo(0xDC), "Auxiliary RAM keeps the test byte");
        });
    }

    /// <summary>
    /// RAMRD and RAMWRT give <c>$0800</c> and <c>$0C00</c> distinct auxiliary bytes, which is
    /// the ProDOS 128K test, and turning the switches off restores main RAM.
    /// </summary>
    [Test]
    public void RamRdAndRamWrt_GiveDistinctAuxBytes_AndOffRestoresMain()
    {
        using IMachine machine = BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux);
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));

        cpu.Write8(AuxProbeLow, 0x01);
        cpu.Write8(AuxProbeHigh, 0x02);

        cpu.Write8(0xC005, 0);
        cpu.Write8(0xC003, 0);
        cpu.Write8(AuxProbeHigh, 0xEE);
        cpu.Write8(AuxProbeLow, 0xEE);
        byte high = cpu.Read8(AuxProbeHigh);
        cpu.Write8(AuxProbeHigh, (byte)(high << 1));

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(AuxProbeHigh), Is.EqualTo(0xDC));
            Assert.That(cpu.Read8(AuxProbeLow), Is.EqualTo(0xEE), "$0800 is not a mirror of $0C00");
            Assert.That(aux.ReadAuxRam(AuxProbeHigh), Is.EqualTo(0xDC));
            Assert.That(aux.ReadAuxRam(AuxProbeLow), Is.EqualTo(0xEE));
        });

        cpu.Write8(0xC004, 0);
        cpu.Write8(0xC002, 0);

        Assert.Multiple(() =>
        {
            Assert.That(aux.IsRamRdEnabled, Is.False);
            Assert.That(aux.IsRamWrtEnabled, Is.False);
            Assert.That(cpu.Read8(AuxProbeLow), Is.EqualTo(0x01), "RAMRD off reads the main byte");
            Assert.That(cpu.Read8(AuxProbeHigh), Is.EqualTo(0x02));
        });
    }

    /// <summary>
    /// ALTZP switches zero page, the stack, and both language-card banks. Turning it off
    /// restores the main bank without powering the machine on.
    /// </summary>
    [Test]
    public void AltZp_SwitchesZeroPageStackAndLanguageCard_AndOffRestoresMain()
    {
        using IMachine machine = BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux);
        var bus = (MainBus)machine.Bus;
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));

        EnableLanguageCard(cpu, bank2ReadWrite: true);
        cpu.Write8(0x0000, 0x99);
        cpu.Write8(0x0100, 0x88);
        cpu.Write8(0xD000, 0x11);
        cpu.Write8(0xE000, 0x22);
        byte[]? mainHigh = bus.GetPageEntry(0xE000).FastBacking;

        cpu.Write8(0xC009, 0);
        cpu.Write8(0x0000, 0x66);
        cpu.Write8(0x0100, 0x77);
        cpu.Write8(0xD000, 0x33);
        cpu.Write8(0xE000, 0x44);
        EnableLanguageCard(cpu, bank2ReadWrite: false);
        cpu.Write8(0xD000, 0x55);

        PageEntry auxHigh = bus.GetPageEntry(0xE000);
        Assert.Multiple(() =>
        {
            Assert.That(aux.IsAltZpEnabled, Is.True);
            Assert.That(cpu.Read8(0x0000), Is.EqualTo(0x66));
            Assert.That(cpu.Read8(0x0100), Is.EqualTo(0x77));
            Assert.That(cpu.Read8(0xD000), Is.EqualTo(0x55), "Auxiliary $D000 bank 1");
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0x44), "Auxiliary $E000");
            Assert.That(auxHigh.FastOps, Is.EqualTo(RamAllOps), "Auxiliary LC RAM stays on the indexed path");
            Assert.That(auxHigh.FastBacking, Is.Not.Null);
            Assert.That(auxHigh.FastBacking, Is.Not.SameAs(mainHigh));
        });

        EnableLanguageCard(cpu, bank2ReadWrite: true);
        Assert.That(cpu.Read8(0xD000), Is.EqualTo(0x33), "Auxiliary $D000 bank 2");

        cpu.Write8(0xC008, 0);

        Assert.Multiple(() =>
        {
            Assert.That(aux.IsAltZpEnabled, Is.False);
            Assert.That(cpu.Read8(0x0000), Is.EqualTo(0x99), "ALTZP off restores main zero page");
            Assert.That(cpu.Read8(0x0100), Is.EqualTo(0x88), "ALTZP off restores the main stack");
            Assert.That(cpu.Read8(0xD000), Is.EqualTo(0x11), "ALTZP off restores main language-card bank 2");
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0x22));
            Assert.That(bus.GetPageEntry(0xE000).FastBacking, Is.SameAs(mainHigh));
            Assert.That(machine.State, Is.EqualTo(MachineState.Off), "Cleanup ran while the machine stayed off");
        });

        cpu.Write8(0xC009, 0);
        EnableLanguageCard(cpu, bank2ReadWrite: false);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0xD000), Is.EqualTo(0x55), "Auxiliary bank 1 survived the switch to main");
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0x44));
        });
    }

    /// <summary>
    /// Runs the ProDOS 2.4.3 aux-card and 128K sequences as 65C02 code.
    /// </summary>
    [Test]
    public void ProDos243_AuxCardAnd128KSequences_Succeed()
    {
        using IMachine machine = BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux);
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));

        cpu.Write8(TextHole, 0x11);
        cpu.Write8(AuxProbeLow, 0x01);
        cpu.Write8(AuxProbeHigh, 0x02);
        aux.WriteAuxRam(TextHole, 0x22);

        Plant(cpu, 0x0080, Test128AtZeroPage);
        Plant(cpu, 0x0300, AuxCardProgram);
        Run(cpu, 0x0300);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.Read8(CardResult), Is.EqualTo(0x01), "80STORE+PAGE2 read-modify-write at $0478 matched");
            Assert.That(cpu.Read8(MemoryResult), Is.EqualTo(0x01), "RAMRD/RAMWRT $0800/$0C00 reported 128K");
            Assert.That(cpu.Read8(TextHole), Is.EqualTo(0x11), "The aux-card cleanup left main $0478 alone");
            Assert.That(aux.ReadAuxRam(TextHole), Is.EqualTo(0x6E), "$EE shifted left and back on the aux page");
            Assert.That(cpu.Read8(AuxProbeLow), Is.EqualTo(0x01));
            Assert.That(cpu.Read8(AuxProbeHigh), Is.EqualTo(0x02));
            Assert.That(aux.ReadAuxRam(AuxProbeLow), Is.EqualTo(0xEE));
            Assert.That(aux.ReadAuxRam(AuxProbeHigh), Is.EqualTo(0xDC));
            Assert.That(aux.Is80StoreEnabled, Is.False);
            Assert.That(aux.IsRamRdEnabled, Is.False);
            Assert.That(machine.State, Is.EqualTo(MachineState.Off));
        });
    }

    /// <summary>
    /// The 65C02 ALTZP sequence keeps main and auxiliary language-card bytes apart.
    /// </summary>
    [Test]
    public void ProDos243_AltZpLanguageCardSequence_KeepsBanksDistinct()
    {
        using IMachine machine = BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux);
        Assert.That(machine.State, Is.EqualTo(MachineState.Off));

        cpu.Write8(0x0000, 0x99);
        cpu.Write8(0x0100, 0x88);
        Plant(cpu, 0x0900, AltZpProgram);
        Run(cpu, 0x0900);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.Read8(0x02F0), Is.EqualTo(0x55), "Auxiliary $D000 bank 1");
            Assert.That(cpu.Read8(0x02F1), Is.EqualTo(0x44), "Auxiliary $E000");
            Assert.That(cpu.Read8(0x02F2), Is.EqualTo(0x00), "Main $D000 bank 1 was not written");
            Assert.That(cpu.Read8(0x02F3), Is.EqualTo(0x22), "Main $E000");
            Assert.That(cpu.Read8(0x02F4), Is.EqualTo(0x11), "Main $D000 bank 2");
            Assert.That(cpu.Read8(0x02F5), Is.EqualTo(0x33), "Auxiliary $D000 bank 2");
            Assert.That(cpu.Read8(0x02F6), Is.EqualTo(0x99), "Main zero page after ALTZP off");
            Assert.That(cpu.Read8(0x02F7), Is.EqualTo(0x88), "Main stack after ALTZP off");
            Assert.That(cpu.Read8(0x02F8), Is.EqualTo(0x66), "Auxiliary zero page");
            Assert.That(cpu.Read8(0x02F9), Is.EqualTo(0x77), "Auxiliary stack");
            Assert.That(aux.IsAltZpEnabled, Is.False, "The sequence leaves ALTZP off");
            Assert.That(cpu.Read8(0xD000), Is.EqualTo(0x11));
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0x22));
            Assert.That(machine.State, Is.EqualTo(MachineState.Off));
        });
    }

    private static IMachine BuildMachine(out Cpu65C02 cpu, out Extended80ColumnDevice aux)
    {
        var mainRam = new PhysicalMemory(0xC000, "main-ram-48k");
        var builder = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpu(CpuFamily.Cpu65C02)
            .WithCpuFactory(context => new Cpu65C02(context));
        builder.AddPhysicalMemory("main-ram-48k", mainRam);
        builder.ConfigureMemory((bus, registry) =>
        {
            PhysicalMemory ram = builder.GetPhysicalMemory("main-ram-48k")
                ?? throw new InvalidOperationException("main-ram-48k was not registered.");
            var target = new RamTarget(ram.Slice(0, 0xC000), "main");
            int deviceId = registry.GenerateId();
            registry.Register(deviceId, "RAM", "Main RAM", "Memory/MainRAM");
            bus.MapRegion(0x0000, 0xC000, deviceId, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        });

        var video = new VideoDevice();
        builder.AddComponent(video);
        builder.AddDevice(video);

        var rom = new byte[0x3000];
        rom[0] = 0xBE;
        rom[0x1000] = 0xEA;
        rom[0x2000] = 0xCE;
        IMachine machine = builder
            .WithLanguageCard()
            .WithSlotManager()
            .WithPocket2eIOPage()
            .WithExtended80ColumnDevice()
            .WithRom(rom, 0xD000, "synthetic-rom")
            .Build();

        cpu = machine.Cpu as Cpu65C02
            ?? throw new InvalidOperationException("The synthetic machine should use a 65C02.");
        aux = machine.GetComponent<Extended80ColumnDevice>()
            ?? throw new InvalidOperationException("The extended 80-column card should be installed.");
        return machine;
    }

    private static void EnableLanguageCard(Cpu65C02 cpu, bool bank2ReadWrite)
    {
        ushort sw = bank2ReadWrite ? (ushort)0xC083 : (ushort)0xC08B;
        cpu.Read8(sw);
        cpu.Read8(sw);
    }

    private static void Plant(Cpu65C02 cpu, ushort address, byte[] program)
    {
        for (int i = 0; i < program.Length; i++)
        {
            cpu.Write8((ushort)(address + i), program[i]);
        }
    }

    private static void Run(Cpu65C02 cpu, ushort address)
    {
        // A freshly built 65C02 still has the shared register file in 32-bit mode until
        // reset. LDA and STA would then touch four soft switches. Reset selects 8-bit mode.
        cpu.Reset();
        cpu.SetPC(address);
        for (int i = 0; i < 400 && cpu.HaltReason != HaltState.Stp; i++)
        {
            cpu.Step();
        }
    }
}