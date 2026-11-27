// <copyright file="Cpu65C02DecimalSbcTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Locks 65C02 decimal-mode SBC: the BCD result, N/Z/V/C, the extra cycle,
/// and all nine addressing modes.
/// </summary>
[TestFixture]
public sealed class Cpu65C02DecimalSbcTests : CpuTestBase
{
    private const ProcessorStatusFlags DecimalCarry = ProcessorStatusFlags.R | ProcessorStatusFlags.D | ProcessorStatusFlags.C;

    private const ProcessorStatusFlags Decimal = ProcessorStatusFlags.R | ProcessorStatusFlags.D;

    /// <summary>
    /// Verifies immediate decimal SBC against results the nibble-local correction gets wrong,
    /// including N, Z, V, and C.
    /// </summary>
    /// <param name="accumulator">Starting A.</param>
    /// <param name="operand">The immediate operand.</param>
    /// <param name="carry">Whether C is set before the subtract.</param>
    /// <param name="expectedA">A after the instruction.</param>
    /// <param name="expectedP">P after the instruction.</param>
    /// <param name="cycles">Cycles, including the decimal-mode penalty.</param>
    [TestCase((byte)0x62, (byte)0xEE, true, (byte)0x0E, (byte)0x28, 3UL)]
    [TestCase((byte)0x20, (byte)0xBE, false, (byte)0xFB, (byte)0xA8, 3UL)]
    [TestCase((byte)0x80, (byte)0x01, true, (byte)0x79, (byte)0x69, 3UL)]
    [TestCase((byte)0x00, (byte)0x00, true, (byte)0x00, (byte)0x2B, 3UL)]
    [TestCase((byte)0x50, (byte)0x25, true, (byte)0x25, (byte)0x29, 3UL)]
    [TestCase((byte)0x00, (byte)0x01, true, (byte)0x99, (byte)0xA8, 3UL)]
    public void SBC_ImmediateDecimal_MatchesResultAndFlags(
        byte accumulator,
        byte operand,
        bool carry,
        byte expectedA,
        byte expectedP,
        ulong cycles)
    {
        Write(0x1000, 0xE9);
        Write(0x1001, operand);

        CpuStepResult result = StepAt(0x1000, accumulator, x: 0, y: 0, carry ? DecimalCarry : Decimal);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(expectedA));
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(expectedP));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(cycles));
        });
    }

    /// <summary>
    /// Verifies that binary immediate SBC does not take the decimal penalty and keeps binary flags.
    /// </summary>
    [Test]
    public void SBC_ImmediateBinary_KeepsBinaryResultAndCycleCount()
    {
        Write(0x1000, 0xE9);
        Write(0x1001, 0xEE);

        CpuStepResult result = StepAt(0x1000, 0x62, x: 0, y: 0, ProcessorStatusFlags.R | ProcessorStatusFlags.C);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0x74));
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(0x20));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(2UL));
        });
    }

    /// <summary>
    /// Verifies that every SBC addressing mode applies the same decimal result, flags, and penalty.
    /// </summary>
    [Test]
    public void SBC_DecimalMode_AllNineModes_Match()
    {
        Write(0x0034, 0xEE);
        Write(0x1234, 0xEE);
        Write(0x1300, 0xEE);
        Write(0x0012, 0x34);
        Write(0x0013, 0x12);
        Write(0x0020, 0x30);
        Write(0x0021, 0x12);
        Write(0x0040, 0x34);
        Write(0x0041, 0x12);

        Write(0x1000, 0xE9);
        Write(0x1001, 0xEE);
        Write(0x1002, 0xE5);
        Write(0x1003, 0x34);
        Write(0x1004, 0xF5);
        Write(0x1005, 0x30);
        Write(0x1006, 0xED);
        Write(0x1007, 0x34);
        Write(0x1008, 0x12);
        Write(0x1009, 0xFD);
        Write(0x100A, 0x30);
        Write(0x100B, 0x12);
        Write(0x100C, 0xF9);
        Write(0x100D, 0x30);
        Write(0x100E, 0x12);
        Write(0x100F, 0xE1);
        Write(0x1010, 0x10);
        Write(0x1011, 0xF1);
        Write(0x1012, 0x20);
        Write(0x1013, 0xF2);
        Write(0x1014, 0x40);
        Write(0x1015, 0xFD);
        Write(0x1016, 0xFF);
        Write(0x1017, 0x12);

        (string Name, Word Pc, byte X, byte Y, ulong Cycles)[] modes =
        [
            ("immediate", 0x1000, 0, 0, 3),
            ("zero page", 0x1002, 0, 0, 4),
            ("zero page,X", 0x1004, 4, 0, 5),
            ("absolute", 0x1006, 0, 0, 5),
            ("absolute,X", 0x1009, 4, 0, 5),
            ("absolute,Y", 0x100C, 0, 4, 5),
            ("(zero page,X)", 0x100F, 2, 0, 7),
            ("(zero page),Y", 0x1011, 0, 4, 6),
            ("(zero page)", 0x1013, 0, 0, 6),
            ("absolute,X page cross", 0x1015, 1, 0, 6),
        ];

        Assert.Multiple(() =>
        {
            foreach ((string name, Word pc, byte x, byte y, ulong cycles) in modes)
            {
                CpuStepResult result = StepAt(pc, 0x62, x, y, DecimalCarry);
                Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0x0E), name);
                Assert.That((byte)Cpu.Registers.P, Is.EqualTo(0x28), name);
                Assert.That(result.CyclesConsumed.Value, Is.EqualTo(cycles), name);
                Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None), name);
            }
        });
    }

    private CpuStepResult StepAt(Word pc, byte accumulator, byte x, byte y, ProcessorStatusFlags status)
    {
        Cpu.Reset();
        Cpu.Registers.PC.SetWord(pc);
        Cpu.Registers.A.SetByte(accumulator);
        Cpu.Registers.X.SetByte(x);
        Cpu.Registers.Y.SetByte(y);
        Cpu.Registers.P = status;
        return Cpu.Step();
    }
}