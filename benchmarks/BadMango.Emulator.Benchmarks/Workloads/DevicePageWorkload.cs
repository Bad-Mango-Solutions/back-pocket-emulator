// <copyright file="DevicePageWorkload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// A closed 65C02 loop that touches plain RAM, ROM, the <c>$C000</c> page, a language-card
/// bank, and text page 1 under 80STORE.
/// </summary>
/// <remarks>
/// <para>
/// One iteration reads and writes a plain RAM byte at <c>$2000</c>, reads the ROM byte at
/// <c>$E000</c>, reads the soft switch at <c>$C000</c>, reads <c>$C080</c> (bank 2, RAM
/// readable), reads <c>$D000</c> from that bank, writes <c>$C001</c> and <c>$C055</c> so
/// text page 1 comes from auxiliary memory, reads <c>$0400</c>, then restores PAGE1 and ROM.
/// The measured benchmark region is this loop. Setup plants the sentinel bytes and leaves
/// the soft switches at their power-on values.
/// </para>
/// <para>
/// The language card latches on a read of <c>$C080</c>–<c>$C08F</c>. <c>$C080</c> selects
/// bank 2 with RAM read enabled. The next instruction reads <c>$D000</c> from that bank.
/// </para>
/// <para>
/// Each absolute load or store is 4 counted cycles and the closing jump is 3, so one loop
/// is 47 cycles.
/// </para>
/// </remarks>
public static class DevicePageWorkload
{
    /// <summary>The workload name.</summary>
    public const string Name = "device-page";

    /// <summary>The CPU family. The loop is 6502 absolute loads, stores, and a jump.</summary>
    public const string CpuFamily = "6502";

    /// <summary>The address of the first instruction.</summary>
    public const Addr EntryPoint = 0x1000;

    /// <summary>Plain RAM byte the loop reads.</summary>
    public const Addr RamReadAddress = 0x2000;

    /// <summary>Plain RAM byte the loop writes with the value read from <see cref="RamReadAddress"/>.</summary>
    public const Addr RamWriteAddress = 0x2001;

    /// <summary>ROM byte the loop reads while the language-card RAM is switched out.</summary>
    public const Addr RomReadAddress = 0xE000;

    /// <summary>Soft switch in the <c>$C000</c> page. The loop reads it.</summary>
    public const Addr SoftSwitchAddress = 0xC000;

    /// <summary>Language-card switch. A read selects bank 2 RAM at <c>$D000</c>–<c>$FFFF</c>.</summary>
    public const Addr LanguageCardSwitch = 0xC080;

    /// <summary>First byte of the language-card bank the switch selects.</summary>
    public const Addr LanguageCardBank = 0xD000;

    /// <summary>Write address that turns 80STORE on.</summary>
    public const Addr Store80On = 0xC001;

    /// <summary>Write address that turns 80STORE off. Used while planting bytes, not inside the loop.</summary>
    public const Addr Store80Off = 0xC000;

    /// <summary>Write address that selects PAGE2 and remaps text page 1 onto auxiliary memory.</summary>
    public const Addr Page2On = 0xC055;

    /// <summary>Write address that selects PAGE1 again so the next iteration repeats the transition.</summary>
    public const Addr Page2Off = 0xC054;

    /// <summary>Text page 1 byte the loop reads after the 80STORE / PAGE2 transition.</summary>
    public const Addr TextPageAddress = 0x0400;

    /// <summary>Language-card switch that makes ROM visible at <c>$D000</c>–<c>$FFFF</c> again.</summary>
    public const Addr RomRestoreSwitch = 0xC082;

    /// <summary>Bank 1 read/write switch, used only to plant a byte the loop must not observe.</summary>
    public const Addr Bank1WriteSwitch = 0xC08B;

    /// <summary>Bank 2 read/write switch, used only to plant the byte the loop reads.</summary>
    public const Addr Bank2WriteSwitch = 0xC083;

    /// <summary>Value planted at <see cref="RamReadAddress"/>.</summary>
    public const byte RamSentinel = 0x11;

    /// <summary>Value planted at <see cref="RomReadAddress"/>.</summary>
    public const byte RomSentinel = 0xE1;

    /// <summary>Value planted in ROM at <see cref="LanguageCardBank"/>, distinct from both RAM banks.</summary>
    public const byte RomBankWindowSentinel = 0xD1;

    /// <summary>Value planted in language-card bank 1 at <c>$D000</c>.</summary>
    public const byte Bank1Sentinel = 0xB1;

    /// <summary>Value planted in language-card bank 2 at <c>$D000</c>.</summary>
    public const byte Bank2Sentinel = 0xB2;

    /// <summary>Value planted in main memory at <see cref="TextPageAddress"/>.</summary>
    public const byte MainTextSentinel = 0x41;

    /// <summary>Value planted in auxiliary memory at <see cref="TextPageAddress"/>.</summary>
    public const byte AuxTextSentinel = 0x42;

    /// <summary>Instructions in one loop, including the jump back to <see cref="EntryPoint"/>.</summary>
    public const int InstructionsPerLoop = 12;

    /// <summary>Counted cycles in one loop.</summary>
    public const int CyclesPerLoop = 47;

    /// <summary>Loops executed by one benchmark invocation.</summary>
    public const int LoopsPerMeasurement = 1000;

    /// <summary>Instructions executed by one benchmark invocation.</summary>
    public const int StepsPerMeasurement = InstructionsPerLoop * LoopsPerMeasurement;

    private const byte LdaAbsolute = 0xAD;

    private const byte StaAbsolute = 0x8D;

    private const byte JmpAbsolute = 0x4C;

    private const int RomImageBytes = 0x3000;

    /// <summary>
    /// Builds the 12 KB test ROM mapped at <c>$D000</c>.
    /// </summary>
    /// <returns>A ROM image filled with <c>NOP</c>, the sentinel bytes, and a reset vector of <c>$FF00</c>.</returns>
    public static byte[] CreateRomImage()
    {
        var rom = new byte[RomImageBytes];
        Array.Fill(rom, (byte)0xEA);
        rom[0x0000] = RomBankWindowSentinel;
        rom[RomReadAddress - 0xD000] = RomSentinel;
        rom[0x2F00] = JmpAbsolute;
        rom[0x2F01] = 0x00;
        rom[0x2F02] = 0xFF;
        rom[0x2FFA] = 0x00;
        rom[0x2FFB] = 0xFF;
        rom[0x2FFC] = 0x00;
        rom[0x2FFD] = 0xFF;
        rom[0x2FFE] = 0x00;
        rom[0x2FFF] = 0xFF;
        return rom;
    }

    /// <summary>
    /// Writes the loop and the sentinel bytes, then leaves ROM visible with 80STORE and PAGE2 off.
    /// </summary>
    /// <param name="fixture">The machine that will run the loop.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fixture"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a planted byte cannot be read back from the bank the switch selects.</exception>
    public static void Load(DevicePageFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        Cpu65C02 cpu = fixture.Cpu;
        ushort address = (ushort)EntryPoint;
        Emit(cpu, ref address, LdaAbsolute, Low(RamReadAddress), High(RamReadAddress));
        Emit(cpu, ref address, StaAbsolute, Low(RamWriteAddress), High(RamWriteAddress));
        Emit(cpu, ref address, LdaAbsolute, Low(RomReadAddress), High(RomReadAddress));
        Emit(cpu, ref address, LdaAbsolute, Low(SoftSwitchAddress), High(SoftSwitchAddress));
        Emit(cpu, ref address, LdaAbsolute, Low(LanguageCardSwitch), High(LanguageCardSwitch));
        Emit(cpu, ref address, LdaAbsolute, Low(LanguageCardBank), High(LanguageCardBank));
        Emit(cpu, ref address, StaAbsolute, Low(Store80On), High(Store80On));
        Emit(cpu, ref address, StaAbsolute, Low(Page2On), High(Page2On));
        Emit(cpu, ref address, LdaAbsolute, Low(TextPageAddress), High(TextPageAddress));
        Emit(cpu, ref address, StaAbsolute, Low(Page2Off), High(Page2Off));
        Emit(cpu, ref address, LdaAbsolute, Low(RomRestoreSwitch), High(RomRestoreSwitch));
        Emit(cpu, ref address, JmpAbsolute, Low(EntryPoint), High(EntryPoint));
        if (address != EntryPoint + 0x24)
        {
            throw new InvalidOperationException($"Device-page layout drifted: loop ends at ${address:X4}.");
        }

        cpu.Write8(RamReadAddress, RamSentinel);
        cpu.Write8(RamWriteAddress, 0x00);
        cpu.Write8(TextPageAddress, MainTextSentinel);
        fixture.Column.WriteAuxRam((ushort)TextPageAddress, AuxTextSentinel);

        WriteLanguageCardByte(cpu, Bank1WriteSwitch, Bank1Sentinel);
        WriteLanguageCardByte(cpu, Bank2WriteSwitch, Bank2Sentinel);
        cpu.Read8(RomRestoreSwitch);

        byte rom = cpu.Read8(RomReadAddress);
        byte ram = cpu.Read8(RamReadAddress);
        cpu.Read8(LanguageCardSwitch);
        byte bank = cpu.Read8(LanguageCardBank);
        cpu.Read8(RomRestoreSwitch);
        cpu.Write8(Store80On, 0);
        cpu.Write8(Page2On, 0);
        byte text = cpu.Read8(TextPageAddress);
        cpu.Write8(Page2Off, 0);
        cpu.Write8(Store80Off, 0);

        if (rom != RomSentinel || ram != RamSentinel || bank != Bank2Sentinel || text != AuxTextSentinel)
        {
            throw new InvalidOperationException(
                $"Device-page sentinels did not read back (ROM=${rom:X2}, RAM=${ram:X2}, bank=${bank:X2}, text=${text:X2}).");
        }

        if (fixture.LanguageCard.IsRamReadEnabled || fixture.LanguageCard.SelectedBank != 2
            || fixture.Column.Is80StoreEnabled || fixture.Column.IsPage2Selected)
        {
            throw new InvalidOperationException("Device-page setup left ROM switched out, or left 80STORE or PAGE2 on.");
        }
    }

    /// <summary>
    /// Clears a halt and points the CPU at <see cref="EntryPoint"/>.
    /// </summary>
    /// <param name="cpu">The CPU that will run the loop.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cpu"/> is null.</exception>
    public static void Prepare(Cpu65C02 cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.SetPC(EntryPoint);
    }

    private static void WriteLanguageCardByte(Cpu65C02 cpu, Addr switchAddress, byte value)
    {
        cpu.Read8(switchAddress);
        cpu.Read8(switchAddress);
        cpu.Write8(LanguageCardBank, value);
    }

    private static void Emit(Cpu65C02 cpu, ref ushort address, params ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            cpu.Write8(address++, bytes[i]);
        }
    }

    private static byte Low(Addr address) => (byte)address;

    private static byte High(Addr address) => (byte)(address >> 8);
}