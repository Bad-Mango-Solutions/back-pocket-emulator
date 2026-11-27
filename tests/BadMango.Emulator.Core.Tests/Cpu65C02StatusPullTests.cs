// <copyright file="Cpu65C02StatusPullTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Locks the 65C02 rule that B and bit 5 are not latches in P.
/// PLP and RTI keep the other flags from the pulled byte, clear B, and set bit 5.
/// </summary>
[TestFixture]
public sealed class Cpu65C02StatusPullTests : CpuTestBase
{
    /// <summary>
    /// Verifies that PLP stores the pulled flags with B clear and bit 5 set.
    /// </summary>
    /// <param name="pulled">The status byte on the stack.</param>
    /// <param name="expected">The P value after PLP.</param>
    [TestCase((byte)0x3E, (byte)0x2E)]
    [TestCase((byte)0x7E, (byte)0x6E)]
    [TestCase((byte)0x00, (byte)0x20)]
    [TestCase((byte)0xFF, (byte)0xEF)]
    [TestCase((byte)0x2E, (byte)0x2E)]
    public void PLP_ClearsBreakAndSetsBit5(byte pulled, byte expected)
    {
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, 0x28);
        Write(0x01FF, pulled);
        Cpu.Reset();
        Cpu.Registers.SP.SetByte(0xFE);
        Cpu.Registers.A.SetByte(0x11);
        Cpu.Registers.X.SetByte(0x22);
        Cpu.Registers.Y.SetByte(0x33);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(expected));
            Assert.That(Cpu.Registers.P & ProcessorStatusFlags.B, Is.EqualTo((ProcessorStatusFlags)0));
            Assert.That(Cpu.Registers.P & ProcessorStatusFlags.R, Is.EqualTo(ProcessorStatusFlags.R));
            Assert.That(Cpu.Registers.SP.GetByte(), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x1001));
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0x11));
            Assert.That(Cpu.Registers.X.GetByte(), Is.EqualTo(0x22));
            Assert.That(Cpu.Registers.Y.GetByte(), Is.EqualTo(0x33));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(4UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that RTI stores the pulled flags with B clear and bit 5 set, then loads PC.
    /// </summary>
    /// <param name="pulled">The status byte on the stack.</param>
    /// <param name="expected">The P value after RTI.</param>
    [TestCase((byte)0x7E, (byte)0x6E)]
    [TestCase((byte)0x3E, (byte)0x2E)]
    [TestCase((byte)0x00, (byte)0x20)]
    [TestCase((byte)0xFF, (byte)0xEF)]
    public void RTI_ClearsBreakAndSetsBit5(byte pulled, byte expected)
    {
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, 0x40);
        Write(0x01FD, pulled);
        Write(0x01FE, 0x34);
        Write(0x01FF, 0x12);
        Cpu.Reset();
        Cpu.Registers.SP.SetByte(0xFC);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(expected));
            Assert.That(Cpu.Registers.P & ProcessorStatusFlags.B, Is.EqualTo((ProcessorStatusFlags)0));
            Assert.That(Cpu.Registers.P & ProcessorStatusFlags.R, Is.EqualTo(ProcessorStatusFlags.R));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x1234));
            Assert.That(Cpu.Registers.SP.GetByte(), Is.EqualTo(0xFF));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(6UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that PHP still pushes B and bit 5 after PLP has cleared B in the live register.
    /// </summary>
    [Test]
    public void PHP_PushesBreakAfterPlpClearedIt()
    {
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, 0x28);
        Write(0x1001, 0x08);
        Write(0x01FF, 0xFF);
        Cpu.Reset();
        Cpu.Registers.SP.SetByte(0xFE);

        Cpu.Step();
        Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(0xEF));
            Assert.That(Read(0x01FF), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.SP.GetByte(), Is.EqualTo(0xFE));
        });
    }
}