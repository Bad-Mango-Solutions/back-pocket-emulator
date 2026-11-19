// <copyright file="Cpu65C02OperandFetchWrapTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Locks 16-bit operand fetch at <c>$FFFE</c>: the high byte comes from <c>$0000</c>,
/// and the program counter wraps to <c>$0001</c>.
/// </summary>
[TestFixture]
public sealed class Cpu65C02OperandFetchWrapTests : CpuTestBase
{
    /// <summary>
    /// Verifies that absolute ORA at <c>$FFFE</c> reads its high operand byte from <c>$0000</c>.
    /// </summary>
    [Test]
    public void Absolute_AtFFFE_ReadsHighOperandByteFrom0000()
    {
        Write(0xFFFE, 0x0D);
        Write(0xFFFF, 0x10);
        Write(0x0000, 0x20);
        Write(0x2010, 0x0F);

        CpuStepResult result = StepAt(0xFFFE, 0xF0, x: 0, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0001));
            Assert.That((byte)Cpu.Registers.P, Is.EqualTo(0xA0));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(4UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that absolute,X ORA at <c>$FFFE</c> indexes the address formed across the wrap.
    /// </summary>
    [Test]
    public void AbsoluteX_AtFFFE_IndexesWrappedOperand()
    {
        Write(0xFFFE, 0x1D);
        Write(0xFFFF, 0x00);
        Write(0x0000, 0x20);
        Write(0x2010, 0x0F);

        CpuStepResult result = StepAt(0xFFFE, 0xF0, x: 0x10, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0001));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(4UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that absolute,Y ORA at <c>$FFFE</c> still charges the page-cross cycle.
    /// </summary>
    [Test]
    public void AbsoluteY_AtFFFE_PageCross_ReadsWrappedOperand()
    {
        Write(0xFFFE, 0x19);
        Write(0xFFFF, 0xF0);
        Write(0x0000, 0x20);
        Write(0x2110, 0x0F);

        CpuStepResult result = StepAt(0xFFFE, 0xF0, x: 0, y: 0x20);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0001));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(5UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that STA absolute,X at <c>$FFFE</c> stores through the wrapped address.
    /// </summary>
    [Test]
    public void AbsoluteXWrite_AtFFFE_StoresThroughWrappedOperand()
    {
        Write(0xFFFE, 0x9D);
        Write(0xFFFF, 0x00);
        Write(0x0000, 0x30);

        CpuStepResult result = StepAt(0xFFFE, 0x0D, x: 0x05, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Read(0x3005), Is.EqualTo(0x0D));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0001));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(5UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that STA absolute,Y at <c>$FFFE</c> stores through the wrapped address.
    /// </summary>
    [Test]
    public void AbsoluteYWrite_AtFFFE_StoresThroughWrappedOperand()
    {
        Write(0xFFFE, 0x99);
        Write(0xFFFF, 0x00);
        Write(0x0000, 0x30);

        CpuStepResult result = StepAt(0xFFFE, 0x0D, x: 0, y: 0x05);

        Assert.Multiple(() =>
        {
            Assert.That(Read(0x3005), Is.EqualTo(0x0D));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0001));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(5UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that JMP (abs) at <c>$FFFE</c> fetches the pointer across the wrap.
    /// </summary>
    [Test]
    public void Indirect_AtFFFE_JumpsThroughWrappedPointer()
    {
        Write(0xFFFE, 0x6C);
        Write(0xFFFF, 0x00);
        Write(0x0000, 0x40);
        Write(0x4000, 0xA6);
        Write(0x4001, 0x66);

        CpuStepResult result = StepAt(0xFFFE, 0, x: 0, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x66A6));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(6UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that JMP (abs,X) at <c>$FFFE</c> fetches the base address across the wrap.
    /// </summary>
    [Test]
    public void AbsoluteIndirectX_AtFFFE_JumpsThroughWrappedBase()
    {
        Write(0xFFFE, 0x7C);
        Write(0xFFFF, 0x00);
        Write(0x0000, 0x40);
        Write(0x4002, 0xA6);
        Write(0x4003, 0x66);

        CpuStepResult result = StepAt(0xFFFE, 0, x: 0x02, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x66A6));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(6UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Verifies that a same-page absolute operand still uses the wide read and the usual cycle count.
    /// </summary>
    [Test]
    public void Absolute_SamePage_StillReadsBothOperandBytes()
    {
        Write(0x2000, 0x0D);
        Write(0x2001, 0x10);
        Write(0x2002, 0x20);
        Write(0x2010, 0x0F);

        CpuStepResult result = StepAt(0x2000, 0xF0, x: 0, y: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Cpu.Registers.A.GetByte(), Is.EqualTo(0xFF));
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x2003));
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(4UL));
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    private CpuStepResult StepAt(Word pc, byte accumulator, byte x, byte y)
    {
        Cpu.Reset();
        Cpu.Registers.PC.SetWord(pc);
        Cpu.Registers.A.SetByte(accumulator);
        Cpu.Registers.X.SetByte(x);
        Cpu.Registers.Y.SetByte(y);
        Cpu.Registers.P = ProcessorStatusFlags.R;
        return Cpu.Step();
    }
}