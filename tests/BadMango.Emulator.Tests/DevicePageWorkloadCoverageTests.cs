// <copyright file="DevicePageWorkloadCoverageTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Proves the device-page loop touches plain RAM, ROM, <c>$C000</c>, a language-card bank,
/// and an 80STORE text page.
/// </summary>
[TestFixture]
public sealed class DevicePageWorkloadCoverageTests
{
    private const byte LdaAbsolute = 0xAD;

    private const byte StaAbsolute = 0x8D;

    /// <summary>
    /// Steps one iteration and accounts for every required access. The assertions fail when
    /// the loop misses RAM, ROM, <c>$C000</c>, the language-card bank, or the 80STORE page.
    /// </summary>
    [Test]
    public void OneLoop_TouchesRamRomSoftSwitchLanguageCardAndTextPage()
    {
        Assert.That(DevicePageWorkload.RamReadAddress, Is.EqualTo((Addr)0x2000), "RAM read");
        Assert.That(DevicePageWorkload.RamWriteAddress, Is.EqualTo((Addr)0x2001), "RAM write");
        Assert.That(DevicePageWorkload.RomReadAddress, Is.EqualTo((Addr)0xE000), "ROM");
        Assert.That(DevicePageWorkload.SoftSwitchAddress, Is.EqualTo((Addr)0xC000), "$C000");
        Assert.That(DevicePageWorkload.LanguageCardSwitch, Is.EqualTo((Addr)0xC080), "language-card switch");
        Assert.That(DevicePageWorkload.LanguageCardBank, Is.EqualTo((Addr)0xD000), "language-card bank");
        Assert.That(DevicePageWorkload.TextPageAddress, Is.EqualTo((Addr)0x0400), "80STORE page");

        var fixture = new DevicePageFixture();
        DevicePageWorkload.Load(fixture);
        Cpu65C02 cpu = fixture.Cpu;
        DevicePageWorkload.Prepare(cpu);

        var reads = new HashSet<Addr>();
        var writes = new HashSet<Addr>();
        byte accumulatorAtRom = 0;
        byte accumulatorAtSwitch = 0;
        byte accumulatorAtBank = 0;
        byte accumulatorAtText = 0;
        bool sawRomWhileRomVisible = false;
        bool sawSelectedBank = false;
        bool sawRemappedTextPage = false;
        ulong startCycles = cpu.GetCycles();

        for (int i = 0; i < DevicePageWorkload.InstructionsPerLoop; i++)
        {
            Addr pc = cpu.GetPC();
            byte opcode = cpu.Peek8(pc);
            Addr operand = 0;
            if (opcode is LdaAbsolute or StaAbsolute or 0x4C)
            {
                operand = (Addr)(cpu.Peek8(pc + 1) | (cpu.Peek8(pc + 2) << 8));
            }

            CpuStepResult result = cpu.Step();
            Assert.That(result.State, Is.EqualTo(CpuRunState.Running), $"step {i} state");

            if (opcode == LdaAbsolute)
            {
                reads.Add(operand);
                byte accumulator = cpu.Registers.A.GetByte();
                if (operand == DevicePageWorkload.RomReadAddress)
                {
                    accumulatorAtRom = accumulator;
                    sawRomWhileRomVisible = !fixture.LanguageCard.IsRamReadEnabled;
                }
                else if (operand == DevicePageWorkload.SoftSwitchAddress)
                {
                    accumulatorAtSwitch = accumulator;
                }
                else if (operand == DevicePageWorkload.LanguageCardBank)
                {
                    accumulatorAtBank = accumulator;
                    sawSelectedBank = fixture.LanguageCard.IsRamReadEnabled
                        && fixture.LanguageCard.SelectedBank == 2;
                }
                else if (operand == DevicePageWorkload.TextPageAddress)
                {
                    accumulatorAtText = accumulator;
                    sawRemappedTextPage = fixture.Column.Is80StoreEnabled && fixture.Column.IsPage2Selected;
                }
            }
            else if (opcode == StaAbsolute)
            {
                writes.Add(operand);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(DevicePageWorkload.EntryPoint), "PC");
            Assert.That(cpu.Halted, Is.False, "halted");
            Assert.That(cpu.GetCycles() - startCycles, Is.EqualTo((ulong)DevicePageWorkload.CyclesPerLoop), "cycles");
            Assert.That(reads, Does.Contain(DevicePageWorkload.RamReadAddress), "RAM read");
            Assert.That(writes, Does.Contain(DevicePageWorkload.RamWriteAddress), "RAM write");
            Assert.That(cpu.Peek8(DevicePageWorkload.RamWriteAddress), Is.EqualTo(DevicePageWorkload.RamSentinel), "RAM byte written");
            Assert.That(reads, Does.Contain(DevicePageWorkload.RomReadAddress), "ROM read");
            Assert.That(sawRomWhileRomVisible, Is.True, "ROM was visible for the ROM read");
            Assert.That(accumulatorAtRom, Is.EqualTo(DevicePageWorkload.RomSentinel), "ROM sentinel");
            Assert.That(reads, Does.Contain(DevicePageWorkload.SoftSwitchAddress), "$C000 read");
            Assert.That(accumulatorAtSwitch, Is.EqualTo(0xFF), "$C000 floating bus");
            Assert.That(reads, Does.Contain(DevicePageWorkload.LanguageCardSwitch), "language-card switch");
            Assert.That(reads, Does.Contain(DevicePageWorkload.LanguageCardBank), "language-card bank");
            Assert.That(sawSelectedBank, Is.True, "bank 2 was selected for the $D000 read");
            Assert.That(accumulatorAtBank, Is.EqualTo(DevicePageWorkload.Bank2Sentinel), "selected bank");
            Assert.That(accumulatorAtBank, Is.Not.EqualTo(DevicePageWorkload.RomBankWindowSentinel), "bank is not ROM");
            Assert.That(accumulatorAtBank, Is.Not.EqualTo(DevicePageWorkload.Bank1Sentinel), "bank is not bank 1");
            Assert.That(writes, Does.Contain(DevicePageWorkload.Store80On), "80STORE");
            Assert.That(writes, Does.Contain(DevicePageWorkload.Page2On), "PAGE2");
            Assert.That(reads, Does.Contain(DevicePageWorkload.TextPageAddress), "text page");
            Assert.That(sawRemappedTextPage, Is.True, "80STORE and PAGE2 were on for the text read");
            Assert.That(accumulatorAtText, Is.EqualTo(DevicePageWorkload.AuxTextSentinel), "auxiliary text page");
            Assert.That(accumulatorAtText, Is.Not.EqualTo(DevicePageWorkload.MainTextSentinel), "text page left main RAM");
        });
    }

    /// <summary>
    /// A second iteration, after the first has warmed the soft-switch paths, allocates nothing
    /// on this thread.
    /// </summary>
    [Test]
    public void SecondLoop_AllocatesNothing()
    {
        var fixture = new DevicePageFixture();
        DevicePageWorkload.Load(fixture);
        Cpu65C02 cpu = fixture.Cpu;
        DevicePageWorkload.Prepare(cpu);
        StepLoop(cpu);

        long before = GC.GetAllocatedBytesForCurrentThread();
        StepLoop(cpu);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero);
    }

    private static void StepLoop(Cpu65C02 cpu)
    {
        for (int i = 0; i < DevicePageWorkload.InstructionsPerLoop; i++)
        {
            cpu.Step();
        }
    }
}