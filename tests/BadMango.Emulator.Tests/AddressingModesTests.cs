// <copyright file="AddressingModesTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;
using Core.Interfaces.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Comprehensive unit tests for addressing mode implementations.
/// </summary>
[TestFixture]
public class AddressingModesTests : CpuTestBase
{
    /// <summary>
    /// Sets up test environment.
    /// </summary>
    [SetUp]
    public void Setup()
    {
    }

    /// <summary>
    /// Verifies that Implied addressing mode returns zero and doesn't modify state.
    /// </summary>
    [Test]
    public void Implied_ReturnsZeroAndDoesNotModifyState()
    {
        // Arrange
        SetupCpu(pc: 0x1000, cycles: 10);

        // Act
        Addr address = AddressingModes.Implied(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1000)); // PC unchanged
        Assert.That(Cpu.GetCycles(), Is.EqualTo(10)); // Cycles unchanged
    }

    /// <summary>
    /// Verifies that Immediate addressing mode returns PC and increments it.
    /// </summary>
    [Test]
    public void Immediate_ReturnsPCAndIncrementsIt()
    {
        // Arrange
        SetupCpu(pc: 0x1000, cycles: 10);

        // Act
        Addr address = AddressingModes.Immediate(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x1000));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001)); // PC incremented
        Assert.That(Cpu.GetCycles(), Is.EqualTo(10)); // No extra cycles for immediate
    }

    /// <summary>
    /// Verifies that ZeroPage addressing mode reads address and adds 1 cycle.
    /// </summary>
    [Test]
    public void ZeroPage_ReadsAddressAndAdds1Cycle()
    {
        // Arrange
        Write(0x1000, 0x42);
        SetupCpu(pc: 0x1000, cycles: 10);

        // Act
        Addr address = AddressingModes.ZeroPage(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0042));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(11)); // +1 cycle for ZP fetch
    }

    /// <summary>
    /// Verifies that ZeroPageX addressing mode adds X register and wraps in zero page.
    /// </summary>
    [Test]
    public void ZeroPageX_AddsXRegisterAndWrapsInZeroPage()
    {
        // Arrange
        Write(0x1000, 0xF0);
        SetupCpu(pc: 0x1000, x: 0x20, cycles: 10);

        // Act
        Addr address = AddressingModes.ZeroPageX(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0010)); // 0xF0 + 0x20 = 0x110, wrapped to 0x10
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(12)); // +2 cycles (fetch + index)
    }

    /// <summary>
    /// Verifies that ZeroPageY addressing mode adds Y register and wraps in zero page.
    /// </summary>
    [Test]
    public void ZeroPageY_AddsYRegisterAndWrapsInZeroPage()
    {
        // Arrange
        Write(0x1000, 0x80);
        SetupCpu(pc: 0x1000, y: 0x90, cycles: 10);

        // Act
        Addr address = AddressingModes.ZeroPageY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0010)); // 0x80 + 0x90 = 0x110, wrapped to 0x10
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(12)); // +2 cycles (fetch + index)
    }

    /// <summary>
    /// Verifies that Absolute addressing mode reads 16-bit address.
    /// </summary>
    [Test]
    public void Absolute_Reads16BitAddress()
    {
        // Arrange
        WriteWord(0x1000, 0x5678);
        SetupCpu(pc: 0x1000, cycles: 10);

        // Act
        Addr address = AddressingModes.Absolute(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x5678));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(12)); // +2 cycles for 16-bit address fetch
    }

    /// <summary>
    /// Verifies that AbsoluteX adds X register without page boundary crossing.
    /// </summary>
    [Test]
    public void AbsoluteX_AddsXRegister_NoPageBoundaryCrossing()
    {
        // Arrange
        WriteWord(0x1000, 0x1234);
        SetupCpu(pc: 0x1000, x: 0x10, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteX(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x1244)); // 0x1234 + 0x10
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(12)); // +2 cycles, no page boundary penalty
    }

    /// <summary>
    /// Verifies that AbsoluteX adds extra cycle when crossing page boundary.
    /// </summary>
    [Test]
    public void AbsoluteX_AddsXRegister_WithPageBoundaryCrossing()
    {
        // Arrange
        WriteWord(0x1000, 0x12FF);
        SetupCpu(pc: 0x1000, x: 0x02, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteX(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x1301)); // 0x12FF + 0x02
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(13)); // +3 cycles (2 base + 1 page boundary)
    }

    /// <summary>
    /// Verifies that AbsoluteY adds Y register without page boundary crossing.
    /// </summary>
    [Test]
    public void AbsoluteY_AddsYRegister_NoPageBoundaryCrossing()
    {
        // Arrange
        WriteWord(0x1000, 0x2000);
        SetupCpu(pc: 0x1000, y: 0x50, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x2050)); // 0x2000 + 0x50
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(12)); // +2 cycles, no page boundary penalty
    }

    /// <summary>
    /// Verifies that AbsoluteY adds extra cycle when crossing page boundary.
    /// </summary>
    [Test]
    public void AbsoluteY_AddsYRegister_WithPageBoundaryCrossing()
    {
        // Arrange
        WriteWord(0x1000, 0x20F0);
        SetupCpu(pc: 0x1000, y: 0x20, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x2110)); // 0x20F0 + 0x20
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(13)); // +3 cycles (2 base + 1 page boundary)
    }

    /// <summary>
    /// Verifies that IndirectX uses X-indexed zero page pointer.
    /// </summary>
    [Test]
    public void IndirectX_UsesXIndexedZeroPagePointer()
    {
        // Arrange
        Write(0x1000, 0x40); // ZP address
        WriteWord(0x0045, 0x3000); // Pointer at ZP 0x45 (0x40 + 0x05)
        SetupCpu(pc: 0x1000, x: 0x05, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectX(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x3000));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(14)); // +4 cycles (fetch ZP + index + read pointer)
    }

    /// <summary>
    /// Verifies that IndirectY uses zero page pointer indexed by Y without page crossing.
    /// </summary>
    [Test]
    public void IndirectY_UsesZeroPagePointerIndexedByY_NoPageCrossing()
    {
        // Arrange
        Write(0x1000, 0x50); // ZP address
        WriteWord(0x0050, 0x4000); // Pointer at ZP 0x50
        SetupCpu(pc: 0x1000, y: 0x10, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x4010)); // 0x4000 + 0x10
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(13)); // +3 cycles, no page boundary penalty
    }

    /// <summary>
    /// Verifies that IndirectY adds extra cycle when crossing page boundary.
    /// </summary>
    [Test]
    public void IndirectY_UsesZeroPagePointerIndexedByY_WithPageCrossing()
    {
        // Arrange
        Write(0x1000, 0x60); // ZP address
        WriteWord(0x0060, 0x40FF); // Pointer at ZP 0x60
        SetupCpu(pc: 0x1000, y: 0x02, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x4101)); // 0x40FF + 0x02
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(14)); // +4 cycles (3 base + 1 page boundary)
    }

    /// <summary>
    /// Verifies that AbsoluteXWrite always takes maximum cycles regardless of page crossing.
    /// </summary>
    [Test]
    public void AbsoluteXWrite_AlwaysTakesMaximumCycles()
    {
        // Arrange - no page crossing case
        WriteWord(0x1000, 0x2000);
        SetupCpu(pc: 0x1000, x: 0x10, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteXWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x2010));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(13)); // Always +3 cycles for write operations
    }

    /// <summary>
    /// Verifies that AbsoluteYWrite always takes maximum cycles regardless of page crossing.
    /// </summary>
    [Test]
    public void AbsoluteYWrite_AlwaysTakesMaximumCycles()
    {
        // Arrange - no page crossing case
        WriteWord(0x1000, 0x3000);
        SetupCpu(pc: 0x1000, y: 0x20, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteYWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x3020));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1002));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(13)); // Always +3 cycles for write operations
    }

    /// <summary>
    /// Verifies that IndirectYWrite always takes maximum cycles regardless of page crossing.
    /// </summary>
    [Test]
    public void IndirectYWrite_AlwaysTakesMaximumCycles()
    {
        // Arrange - no page crossing case
        Write(0x1000, 0x70); // ZP address
        WriteWord(0x0070, 0x5000); // Pointer at ZP 0x70
        SetupCpu(pc: 0x1000, y: 0x30, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectYWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x5030));
        Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo(0x1001));
        Assert.That(Cpu.GetCycles(), Is.EqualTo(14)); // Always +4 cycles for write operations
    }

    /// <summary>
    /// Verifies that AbsoluteY wraps to 16 bits when base+Y overflows past $FFFF.
    /// On the 65C02, <c>LDA $FF48,Y</c> with Y=$FE must produce effective
    /// address $0046, not the unwrapped $10046. This regression test guards
    /// against the ProDOS-boot halt that occurred when the unwrapped
    /// 17-bit address was passed to the bus and reported as Unmapped.
    /// </summary>
    [Test]
    public void AbsoluteY_WrapsTo16BitsOnOverflow()
    {
        // Arrange - LDA $FF48,Y with Y=$FE -> should wrap to $0046
        WriteWord(0x1000, 0xFF48);
        SetupCpu(pc: 0x1000, y: 0xFE, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0046), "$FF48 + $FE must wrap to $0046 on the 65C02.");
    }

    /// <summary>
    /// Verifies that AbsoluteX wraps to 16 bits when base+X overflows past $FFFF.
    /// </summary>
    [Test]
    public void AbsoluteX_WrapsTo16BitsOnOverflow()
    {
        // Arrange - LDA $FFFE,X with X=$10 -> $0E
        WriteWord(0x1000, 0xFFFE);
        SetupCpu(pc: 0x1000, x: 0x10, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteX(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x000E), "$FFFE + $10 must wrap to $000E on the 65C02.");
    }

    /// <summary>
    /// Verifies that AbsoluteYWrite wraps to 16 bits when base+Y overflows.
    /// </summary>
    [Test]
    public void AbsoluteYWrite_WrapsTo16BitsOnOverflow()
    {
        // Arrange - STA $FFC0,Y with Y=$80 -> $0040
        WriteWord(0x1000, 0xFFC0);
        SetupCpu(pc: 0x1000, y: 0x80, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteYWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0040), "$FFC0 + $80 must wrap to $0040 on the 65C02.");
    }

    /// <summary>
    /// Verifies that AbsoluteXWrite wraps to 16 bits when base+X overflows.
    /// </summary>
    [Test]
    public void AbsoluteXWrite_WrapsTo16BitsOnOverflow()
    {
        // Arrange - STA $FFC0,X with X=$80 -> $0040
        WriteWord(0x1000, 0xFFC0);
        SetupCpu(pc: 0x1000, x: 0x80, cycles: 10);

        // Act
        Addr address = AddressingModes.AbsoluteXWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0040), "$FFC0 + $80 must wrap to $0040 on the 65C02.");
    }

    /// <summary>
    /// Verifies that IndirectY wraps to 16 bits when pointer+Y overflows.
    /// </summary>
    [Test]
    public void IndirectY_WrapsTo16BitsOnOverflow()
    {
        // Arrange - LDA ($70),Y with pointer=$FF80, Y=$90 -> $0010
        Write(0x1000, 0x70);
        WriteWord(0x0070, 0xFF80);
        SetupCpu(pc: 0x1000, y: 0x90, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectY(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0010), "$FF80 + $90 must wrap to $0010 on the 65C02.");
    }

    /// <summary>
    /// Verifies that IndirectYWrite wraps to 16 bits when pointer+Y overflows.
    /// </summary>
    [Test]
    public void IndirectYWrite_WrapsTo16BitsOnOverflow()
    {
        // Arrange - STA ($70),Y with pointer=$FF80, Y=$90 -> $0010
        Write(0x1000, 0x70);
        WriteWord(0x0070, 0xFF80);
        SetupCpu(pc: 0x1000, y: 0x90, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectYWrite(Cpu);

        // Assert
        Assert.That(address, Is.EqualTo(0x0010), "$FF80 + $90 must wrap to $0010 on the 65C02.");
    }

    /// <summary>
    /// Verifies that <c>(zp),Y</c> wraps the high-byte pointer fetch within
    /// the zero page when <c>zp = $FF</c>. Per the 6502/65C02 spec, the high
    /// byte must come from <c>$0000</c>, not <c>$0100</c> (the stack).
    /// </summary>
    /// <remarks>
    /// Without this wrap, code with a zero-page pointer at <c>$FF</c> reads
    /// the high byte from the stack, producing wrong-page targets — exactly
    /// the kind of off-by-one-page symptom observed when booting ProDOS 2.4.3
    /// BITSY BYE.
    /// </remarks>
    [Test]
    public void IndirectY_WrapsZeroPagePointerHighByte_WhenPointerAtFF()
    {
        // Arrange - pointer low byte at $00FF, high byte at $0000 (ZP wrap),
        // with deliberately-different garbage at $0100 so we'd notice a bad fetch.
        Write(0x1000, 0xFF);
        Write(0x00FF, 0x34); // pointer low
        Write(0x0000, 0x12); // pointer high (ZP wrap)
        Write(0x0100, 0xAB); // garbage on the stack page; must NOT be fetched
        SetupCpu(pc: 0x1000, y: 0x01, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectY(Cpu);

        // Assert
        Assert.That(
            address,
            Is.EqualTo(0x1235),
            "Pointer high byte must be fetched from $0000 (ZP wrap), not $0100 (stack).");
    }

    /// <summary>
    /// Same wrap requirement as <see cref="IndirectY_WrapsZeroPagePointerHighByte_WhenPointerAtFF"/>,
    /// but for the write-side <c>(zp),Y</c> addressing mode used by <c>STA</c>.
    /// </summary>
    [Test]
    public void IndirectYWrite_WrapsZeroPagePointerHighByte_WhenPointerAtFF()
    {
        // Arrange - pointer low byte at $00FF, high byte at $0000 (ZP wrap).
        Write(0x1000, 0xFF);
        Write(0x00FF, 0x34); // pointer low
        Write(0x0000, 0x12); // pointer high (ZP wrap)
        Write(0x0100, 0xAB); // garbage on the stack page; must NOT be fetched
        SetupCpu(pc: 0x1000, y: 0x01, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectYWrite(Cpu);

        // Assert
        Assert.That(
            address,
            Is.EqualTo(0x1235),
            "Pointer high byte must be fetched from $0000 (ZP wrap), not $0100 (stack).");
    }

    /// <summary>
    /// Verifies that <c>(zp,X)</c> wraps the high-byte pointer fetch within
    /// the zero page when the X-indexed pointer lands at <c>$FF</c>. Per the
    /// 6502/65C02 spec the high byte must come from <c>$0000</c>, not
    /// <c>$0100</c> (the stack).
    /// </summary>
    [Test]
    public void IndirectX_WrapsZeroPagePointerHighByte_WhenIndexedPointerAtFF()
    {
        // Arrange - operand $FE + X=$01 -> ZP pointer at $FF (wraps to $00 for hi byte).
        Write(0x1000, 0xFE);
        Write(0x00FF, 0x34); // pointer low
        Write(0x0000, 0x12); // pointer high (ZP wrap)
        Write(0x0100, 0xAB); // garbage on the stack page; must NOT be fetched
        SetupCpu(pc: 0x1000, x: 0x01, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectX(Cpu);

        // Assert
        Assert.That(
            address,
            Is.EqualTo(0x1234),
            "Pointer high byte must be fetched from $0000 (ZP wrap), not $0100 (stack).");
    }

    /// <summary>
    /// Verifies that <c>(zp,X)</c> also wraps the ZP-offset add itself: an
    /// operand of <c>$FF</c> with <c>X=$01</c> must produce a pointer at
    /// <c>$00</c> (wrapping within the zero page), not <c>$0100</c>.
    /// </summary>
    [Test]
    public void IndirectX_WrapsZeroPageIndexAddItself()
    {
        // Arrange - operand $FF + X=$01 -> ZP pointer at $00 (full ZP wrap).
        Write(0x1000, 0xFF);
        Write(0x0000, 0x78); // pointer low at $00
        Write(0x0001, 0x56); // pointer high at $01
        SetupCpu(pc: 0x1000, x: 0x01, cycles: 10);

        // Act
        Addr address = AddressingModes.IndirectX(Cpu);

        // Assert
        Assert.That(
            address,
            Is.EqualTo(0x5678),
            "(zp,X) with operand=$FF, X=$01 must read the pointer from $00/$01.");
    }

    /// <summary>
    /// Verifies that <see cref="ImpliedMode"/> resolves identically to the legacy implied handler.
    /// </summary>
    [Test]
    public void ImpliedMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ImpliedMode>(AddressingModes.Implied, () => SetupCpu(pc: 0x1000, cycles: 10));
    }

    /// <summary>
    /// Verifies that <see cref="ImmediateMode"/> resolves identically to the legacy immediate handler.
    /// </summary>
    [Test]
    public void ImmediateMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ImmediateMode>(AddressingModes.Immediate, () => SetupCpu(pc: 0x1000, cycles: 10));
    }

    /// <summary>
    /// Verifies that <see cref="ZeroPageMode"/> resolves identically to the legacy zero-page handler.
    /// </summary>
    [Test]
    public void ZeroPageMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ZeroPageMode>(AddressingModes.ZeroPage, () =>
        {
            Write(0x1000, 0x42);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ZeroPageXMode"/> resolves identically to the legacy zero-page,X handler.
    /// </summary>
    [Test]
    public void ZeroPageXMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ZeroPageXMode>(AddressingModes.ZeroPageX, () =>
        {
            Write(0x1000, 0xF0);
            SetupCpu(pc: 0x1000, x: 0x20, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ZeroPageYMode"/> resolves identically to the legacy zero-page,Y handler.
    /// </summary>
    [Test]
    public void ZeroPageYMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ZeroPageYMode>(AddressingModes.ZeroPageY, () =>
        {
            Write(0x1000, 0x80);
            SetupCpu(pc: 0x1000, y: 0x90, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteMode"/> resolves identically to the legacy absolute handler.
    /// </summary>
    [Test]
    public void AbsoluteMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteMode>(AddressingModes.Absolute, () =>
        {
            WriteWord(0x1000, 0x5678);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteXMode"/> resolves identically to the legacy absolute,X handler.
    /// </summary>
    [Test]
    public void AbsoluteXMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteXMode>(AddressingModes.AbsoluteX, () =>
        {
            WriteWord(0x1000, 0x12FF);
            SetupCpu(pc: 0x1000, x: 0x02, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteYMode"/> resolves identically to the legacy absolute,Y handler.
    /// </summary>
    [Test]
    public void AbsoluteYMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteYMode>(AddressingModes.AbsoluteY, () =>
        {
            WriteWord(0x1000, 0x20F0);
            SetupCpu(pc: 0x1000, y: 0x20, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="IndirectXMode"/> resolves identically to the legacy indirect,X handler.
    /// </summary>
    [Test]
    public void IndirectXMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<IndirectXMode>(AddressingModes.IndirectX, () =>
        {
            Write(0x1000, 0x40);
            WriteWord(0x0045, 0x3000);
            SetupCpu(pc: 0x1000, x: 0x05, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="IndirectYMode"/> resolves identically to the legacy indirect,Y handler.
    /// </summary>
    [Test]
    public void IndirectYMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<IndirectYMode>(AddressingModes.IndirectY, () =>
        {
            Write(0x1000, 0x60);
            WriteWord(0x0060, 0x40FF);
            SetupCpu(pc: 0x1000, y: 0x02, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteXWriteMode"/> resolves identically to the legacy absolute,X write handler.
    /// </summary>
    [Test]
    public void AbsoluteXWriteMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteXWriteMode>(AddressingModes.AbsoluteXWrite, () =>
        {
            WriteWord(0x1000, 0x2000);
            SetupCpu(pc: 0x1000, x: 0x10, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteYWriteMode"/> resolves identically to the legacy absolute,Y write handler.
    /// </summary>
    [Test]
    public void AbsoluteYWriteMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteYWriteMode>(AddressingModes.AbsoluteYWrite, () =>
        {
            WriteWord(0x1000, 0x3000);
            SetupCpu(pc: 0x1000, y: 0x20, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="IndirectYWriteMode"/> resolves identically to the legacy indirect,Y write handler.
    /// </summary>
    [Test]
    public void IndirectYWriteMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<IndirectYWriteMode>(AddressingModes.IndirectYWrite, () =>
        {
            Write(0x1000, 0x70);
            WriteWord(0x0070, 0x5000);
            SetupCpu(pc: 0x1000, y: 0x30, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AccumulatorMode"/> resolves identically to the legacy accumulator handler.
    /// </summary>
    [Test]
    public void AccumulatorMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AccumulatorMode>(AddressingModes.Accumulator, () => SetupCpu(pc: 0x1000, cycles: 10));
    }

    /// <summary>
    /// Verifies that <see cref="RelativeMode"/> resolves identically to the legacy relative handler.
    /// </summary>
    [Test]
    public void RelativeMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<RelativeMode>(AddressingModes.Relative, () =>
        {
            Write(0x1000, 0xF8);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="IndirectMode"/> resolves identically to the legacy indirect handler.
    /// </summary>
    [Test]
    public void IndirectMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<IndirectMode>(AddressingModes.Indirect, () =>
        {
            WriteWord(0x1000, 0x2200);
            WriteWord(0x2200, 0x3456);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ZeroPageIndirectMode"/> resolves identically to the legacy zero-page indirect handler.
    /// </summary>
    [Test]
    public void ZeroPageIndirectMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ZeroPageIndirectMode>(AddressingModes.ZeroPageIndirect, () =>
        {
            Write(0x1000, 0x80);
            WriteWord(0x0080, 0x5678);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="AbsoluteIndirectXMode"/> resolves identically to the legacy absolute indirect,X handler.
    /// </summary>
    [Test]
    public void AbsoluteIndirectXMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<AbsoluteIndirectXMode>(AddressingModes.AbsoluteIndirectX, () =>
        {
            WriteWord(0x1000, 0x2000);
            WriteWord(0x2005, 0x4ABC);
            SetupCpu(pc: 0x1000, x: 0x05, cycles: 10);
        });
    }

    /// <summary>
    /// Verifies that <see cref="ZeroPageRelativeMode"/> resolves identically to the legacy zero-page relative handler.
    /// </summary>
    [Test]
    public void ZeroPageRelativeMode_MatchesLegacyHandler()
    {
        AssertStructMatchesLegacy<ZeroPageRelativeMode>(AddressingModes.ZeroPageRelative, () =>
        {
            Write(0x1000, 0x7F);
            Write(0x1001, 0xFA);
            SetupCpu(pc: 0x1000, cycles: 10);
        });
    }

    /// <summary>
    /// Sets up CPU registers for testing.
    /// </summary>
    private void SetupCpu(
        Word pc = 0,
        byte a = 0,
        byte x = 0,
        byte y = 0,
        byte sp = 0,
        ProcessorStatusFlags p = 0,
        ulong cycles = 0)
    {
        Cpu.Registers.PC.SetWord(pc);
        Cpu.Registers.A.SetByte(a);
        Cpu.Registers.X.SetByte(x);
        Cpu.Registers.Y.SetByte(y);
        Cpu.Registers.SP.SetByte(sp);
        Cpu.Registers.P = p;
        Cpu.Registers.TCU = 0;
        Cpu.SetCycles(cycles);
    }

    /// <summary>
    /// Runs a legacy and generic addressing resolver against identical CPU setup and compares outputs.
    /// </summary>
    /// <typeparam name="TMode">The generic addressing mode struct under test.</typeparam>
    /// <param name="legacyHandler">The existing delegate-based handler.</param>
    /// <param name="arrange">The shared setup action to apply before each execution.</param>
    private void AssertStructMatchesLegacy<TMode>(AddressingModeHandler legacyHandler, Action arrange)
        where TMode : struct, IAddressingMode
    {
        var legacyResult = ExecuteMode(cpu => legacyHandler(cpu), arrange);
        var structResult = ExecuteMode(static cpu => TMode.Resolve(cpu), arrange);

        Assert.Multiple(() =>
        {
            Assert.That(structResult.Address, Is.EqualTo(legacyResult.Address));
            Assert.That(structResult.ProgramCounter, Is.EqualTo(legacyResult.ProgramCounter));
            Assert.That(structResult.Cycles, Is.EqualTo(legacyResult.Cycles));
        });
    }

    /// <summary>
    /// Executes an addressing-mode resolver and captures address, PC, and cycles after execution.
    /// </summary>
    /// <param name="resolver">The resolver to run.</param>
    /// <param name="arrange">The setup action to apply before execution.</param>
    /// <returns>The captured execution result.</returns>
    private ModeExecutionResult ExecuteMode(Func<ICpu, Addr> resolver, Action arrange)
    {
        arrange();
        Addr address = resolver(Cpu);

        return new ModeExecutionResult(address, Cpu.Registers.PC.GetWord(), Cpu.GetCycles());
    }

    /// <summary>
    /// Captures the observable result of one addressing-mode execution.
    /// </summary>
    /// <param name="Address">The resolved address.</param>
    /// <param name="ProgramCounter">The program counter after resolution.</param>
    /// <param name="Cycles">The cycle counter after resolution.</param>
    private readonly record struct ModeExecutionResult(Addr Address, Word ProgramCounter, ulong Cycles);
}