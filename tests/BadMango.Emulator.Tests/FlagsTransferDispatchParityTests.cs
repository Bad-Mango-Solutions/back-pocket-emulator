// <copyright file="FlagsTransferDispatchParityTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Compares direct-dispatch flag and transfer opcodes with the 65C02 result
/// for registers, flags, the stack pointer, the program counter, and cycles.
/// Each case runs through <see cref="Cpu65C02.Step"/>, which fetches the opcode
/// and calls the cached <see cref="OpcodeDispatch"/> delegate.
/// </summary>
[TestFixture]
public class FlagsTransferDispatchParityTests : CpuTestBase
{
    private const Word StartPc = 0x1000;
    private const ulong StartCycles = 10;

    // C, I, D, R, and V. Transfer instructions must leave these bits alone.
    private const byte Preserved = 0x6D;

    // Preserved bits with Z and N already set, so a clear is visible.
    private const byte PreservedWithZeroAndNegative = 0xEF;

    private const byte SentinelA = 0x11;
    private const byte SentinelX = 0x22;
    private const byte SentinelY = 0x33;
    private const uint SentinelSp = 0x01FD;

    /// <summary>
    /// A flag opcode changes only its own flag. A, X, Y, and SP stay put.
    /// The fetch plus the opcode body is 2 cycles and advances PC by one.
    /// </summary>
    /// <param name="opcode">CLC, SEC, CLI, SEI, CLD, SED, or CLV.</param>
    /// <param name="initialFlags">The processor status before the instruction.</param>
    /// <param name="expectedFlags">The processor status after the instruction.</param>
    [TestCase(0x18, 0xFF, 0xFE)]
    [TestCase(0x18, 0x00, 0x00)]
    [TestCase(0x18, 0xC3, 0xC2)]
    [TestCase(0x38, 0x00, 0x01)]
    [TestCase(0x38, 0xFF, 0xFF)]
    [TestCase(0x38, 0xC2, 0xC3)]
    [TestCase(0x58, 0xFF, 0xFB)]
    [TestCase(0x58, 0x00, 0x00)]
    [TestCase(0x58, 0xC7, 0xC3)]
    [TestCase(0x78, 0x00, 0x04)]
    [TestCase(0x78, 0xFF, 0xFF)]
    [TestCase(0x78, 0xC3, 0xC7)]
    [TestCase(0xD8, 0xFF, 0xF7)]
    [TestCase(0xD8, 0x00, 0x00)]
    [TestCase(0xD8, 0xCB, 0xC3)]
    [TestCase(0xF8, 0x00, 0x08)]
    [TestCase(0xF8, 0xFF, 0xFF)]
    [TestCase(0xF8, 0xC3, 0xCB)]
    [TestCase(0xB8, 0xFF, 0xBF)]
    [TestCase(0xB8, 0x00, 0x00)]
    [TestCase(0xB8, 0xC3, 0x83)]
    public void FlagOpcode_DirectDispatch_ChangesOnlyItsFlag(int opcode, int initialFlags, int expectedFlags)
    {
        AssertDirectStep(
            (byte)opcode,
            SentinelA,
            SentinelX,
            SentinelY,
            SentinelSp,
            (byte)initialFlags,
            SentinelA,
            SentinelX,
            SentinelY,
            SentinelSp,
            (byte)expectedFlags);
    }

    /// <summary>
    /// TAX, TAY, TXA, and TYA copy one register and set Z and N from the value.
    /// Zero, a small positive, the largest positive, and two negative values are covered.
    /// C, I, D, R, and V stay set.
    /// </summary>
    /// <param name="opcode">TAX, TAY, TXA, or TYA.</param>
    /// <param name="value">The source register value.</param>
    /// <param name="expectedFlags">Z and N after the transfer, with the other tested flags still set.</param>
    [TestCase(0xAA, 0x00, Preserved | 0x02)]
    [TestCase(0xAA, 0x01, Preserved)]
    [TestCase(0xAA, 0x7F, Preserved)]
    [TestCase(0xAA, 0x80, Preserved | 0x80)]
    [TestCase(0xAA, 0xFF, Preserved | 0x80)]
    [TestCase(0xA8, 0x00, Preserved | 0x02)]
    [TestCase(0xA8, 0x01, Preserved)]
    [TestCase(0xA8, 0x7F, Preserved)]
    [TestCase(0xA8, 0x80, Preserved | 0x80)]
    [TestCase(0xA8, 0xFF, Preserved | 0x80)]
    [TestCase(0x8A, 0x00, Preserved | 0x02)]
    [TestCase(0x8A, 0x01, Preserved)]
    [TestCase(0x8A, 0x7F, Preserved)]
    [TestCase(0x8A, 0x80, Preserved | 0x80)]
    [TestCase(0x8A, 0xFF, Preserved | 0x80)]
    [TestCase(0x98, 0x00, Preserved | 0x02)]
    [TestCase(0x98, 0x01, Preserved)]
    [TestCase(0x98, 0x7F, Preserved)]
    [TestCase(0x98, 0x80, Preserved | 0x80)]
    [TestCase(0x98, 0xFF, Preserved | 0x80)]
    public void TransferOpcode_DirectDispatch_CopiesRegisterAndSetsZeroAndNegative(int opcode, int value, int expectedFlags)
    {
        byte source = (byte)value;
        byte a = SentinelA;
        byte x = SentinelX;
        byte y = SentinelY;
        byte expectedA = a;
        byte expectedX = x;
        byte expectedY = y;

        switch (opcode)
        {
            case 0xAA:
                a = source;
                expectedA = source;
                expectedX = source;
                break;
            case 0xA8:
                a = source;
                expectedA = source;
                expectedY = source;
                break;
            case 0x8A:
                x = source;
                expectedX = source;
                expectedA = source;
                break;
            case 0x98:
                y = source;
                expectedY = source;
                expectedA = source;
                break;
            default:
                Assert.Fail($"${opcode:X2} is not a flag-setting transfer");
                return;
        }

        AssertDirectStep(
            (byte)opcode,
            a,
            x,
            y,
            SentinelSp,
            PreservedWithZeroAndNegative,
            expectedA,
            expectedX,
            expectedY,
            SentinelSp,
            (byte)expectedFlags);
    }

    /// <summary>
    /// TXS copies X into the whole stack pointer and does not change flags.
    /// Zero and negative values must not set Z or N. A stack pointer that already
    /// has a page byte is replaced by the byte from X.
    /// </summary>
    /// <param name="x">The X value copied to SP.</param>
    /// <param name="initialSp">The stack pointer before TXS, including a page byte when one is set.</param>
    /// <param name="flags">The processor status, which TXS must leave unchanged.</param>
    [TestCase(0x00, 0x01FF, 0x00)]
    [TestCase(0x00, 0x0000, 0xFF)]
    [TestCase(0x01, 0x00FF, PreservedWithZeroAndNegative)]
    [TestCase(0x7F, 0x0001, 0xC3)]
    [TestCase(0x80, 0x01FF, 0x00)]
    [TestCase(0xFF, 0x0080, 0xFF)]
    public void TXS_DirectDispatch_CopiesXToStackPointerAndLeavesFlags(int x, int initialSp, int flags)
    {
        AssertDirectStep(
            0x9A,
            SentinelA,
            (byte)x,
            SentinelY,
            (uint)initialSp,
            (byte)flags,
            SentinelA,
            (byte)x,
            SentinelY,
            (uint)x,
            (byte)flags);
    }

    /// <summary>
    /// TSX copies the low byte of SP into X and sets Z and N from that byte.
    /// The rest of SP, including a non-zero page byte, stays in place.
    /// </summary>
    /// <param name="initialSp">The stack pointer, including page-byte edges <c>$0100</c>, <c>$0180</c>, and <c>$01FF</c>.</param>
    /// <param name="expectedX">The low byte written to X.</param>
    /// <param name="expectedFlags">Z and N for <paramref name="expectedX"/>, with C, I, D, R, and V still set.</param>
    [TestCase(0x00000000, 0x00, Preserved | 0x02)]
    [TestCase(0x00000001, 0x01, Preserved)]
    [TestCase(0x0000007F, 0x7F, Preserved)]
    [TestCase(0x00000080, 0x80, Preserved | 0x80)]
    [TestCase(0x000000FF, 0xFF, Preserved | 0x80)]
    [TestCase(0x00000100, 0x00, Preserved | 0x02)]
    [TestCase(0x00000180, 0x80, Preserved | 0x80)]
    [TestCase(0x000001FF, 0xFF, Preserved | 0x80)]
    public void TSX_DirectDispatch_CopiesStackPointerLowByteAndSetsZeroAndNegative(int initialSp, int expectedX, int expectedFlags)
    {
        AssertDirectStep(
            0xBA,
            SentinelA,
            SentinelX,
            SentinelY,
            (uint)initialSp,
            PreservedWithZeroAndNegative,
            SentinelA,
            (byte)expectedX,
            SentinelY,
            (uint)initialSp,
            (byte)expectedFlags);
    }

    /// <summary>
    /// Runs one implied opcode through the CPU and checks architectural state.
    /// The step uses that opcode's cached dispatch delegate.
    /// </summary>
    /// <param name="opcode">The opcode byte written at <see cref="StartPc"/>.</param>
    /// <param name="a">The accumulator before the step.</param>
    /// <param name="x">The X register before the step.</param>
    /// <param name="y">The Y register before the step.</param>
    /// <param name="sp">The full stack pointer before the step.</param>
    /// <param name="flags">The processor status before the step.</param>
    /// <param name="expectedA">The accumulator after the step.</param>
    /// <param name="expectedX">The X register after the step.</param>
    /// <param name="expectedY">The Y register after the step.</param>
    /// <param name="expectedSp">The full stack pointer after the step.</param>
    /// <param name="expectedFlags">The processor status after the step.</param>
    private void AssertDirectStep(
        byte opcode,
        byte a,
        byte x,
        byte y,
        uint sp,
        byte flags,
        byte expectedA,
        byte expectedX,
        byte expectedY,
        uint expectedSp,
        byte expectedFlags)
    {
        Cpu.Registers.Reset(true);
        Cpu.Registers.A.SetByte(a);
        Cpu.Registers.X.SetByte(x);
        Cpu.Registers.Y.SetByte(y);
        Cpu.Registers.SP.SetDWord(sp);
        Cpu.Registers.P = (ProcessorStatusFlags)flags;
        Cpu.Registers.PC.SetWord(StartPc);
        Cpu.HaltReason = HaltState.None;
        Cpu.SetCycles(StartCycles);
        Write(StartPc, opcode);

        Assert.That(
            Cpu65C02OpcodeTableBuilder.Build().GetDispatch(opcode),
            Is.Not.Null,
            $"${opcode:X2} dispatch");

        Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetDWord(), Is.EqualTo((DWord)expectedA), "A");
            Assert.That(Cpu.Registers.X.GetDWord(), Is.EqualTo((DWord)expectedX), "X");
            Assert.That(Cpu.Registers.Y.GetDWord(), Is.EqualTo((DWord)expectedY), "Y");
            Assert.That(Cpu.Registers.SP.GetDWord(), Is.EqualTo((DWord)expectedSp), "SP");
            Assert.That(Cpu.Registers.P, Is.EqualTo((ProcessorStatusFlags)expectedFlags), "P");
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)(StartPc + 1)), "PC");
            Assert.That(Cpu.GetCycles(), Is.EqualTo(StartCycles + 2), "cycles");
        });
    }
}