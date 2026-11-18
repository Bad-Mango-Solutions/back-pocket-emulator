// <copyright file="Instructions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using Core;
using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>
/// Provides instruction implementations that compose with addressing modes.
/// </summary>
/// <remarks>
/// <para>
/// Instructions are higher-order functions that take addressing mode delegates
/// and return opcode handlers. This enables true composition and eliminates
/// the need for separate methods for each instruction/addressing-mode combination.
/// </para>
/// <para>
/// This partial class is split across multiple files by instruction category:
/// <list type="bullet">
/// <item><description><c>Instructions.cs</c> - Load/Store direct dispatch (LDA, LDX, LDY, STA, STX, STY, STZ), NOP, BRK</description></item>
/// <item><description><c>Instructions.Flags.cs</c> - Flag manipulation (CLC, SEC, CLI, SEI, CLD, SED, CLV)</description></item>
/// <item><description><c>Instructions.Transfer.cs</c> - Register transfers (TAX, TAY, TXA, TYA, TXS, TSX)</description></item>
/// <item><description><c>Instructions.Stack.cs</c> - Stack direct dispatch (PHA, PHP, PLA, PLP, PHX, PLX, PHY, PLY)</description></item>
/// <item><description><c>Instructions.Jump.cs</c> - Jump direct dispatch (JMP, JSR, RTS, RTI)</description></item>
/// <item><description><c>Instructions.Branch.cs</c> - Branch direct dispatch (BCC, BCS, BEQ, BNE, BMI, BPL, BVC, BVS, BRA)</description></item>
/// <item><description><c>Instructions.Arithmetic.cs</c> - Arithmetic direct dispatch (ADC, SBC, INC, DEC, INX, INY, DEX, DEY, INA, DEA)</description></item>
/// <item><description><c>Instructions.Logical.cs</c> - Logical direct dispatch (AND, ORA, EOR, BIT)</description></item>
/// <item><description><c>Instructions.Shift.cs</c> - Shift direct dispatch (ASL, LSR, ROL, ROR)</description></item>
/// <item><description><c>Instructions.Compare.cs</c> - Comparison direct dispatch (CMP, CPX, CPY)</description></item>
/// <item><description><c>Instructions.65C02.cs</c> - 65C02-specific (TSB, TRB, WAI, STP). STZ lives with the other stores.</description></item>
/// </list>
/// </para>
/// </remarks>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for LDA immediate.</summary>
    internal static readonly OpcodeDispatch LDAImmediateDispatch = DispatchLDAImmediate;

    /// <summary>Cached direct-dispatch delegate for LDA zero page.</summary>
    internal static readonly OpcodeDispatch LDAZeroPageDispatch = DispatchLDAZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDA zero page,X.</summary>
    internal static readonly OpcodeDispatch LDAZeroPageXDispatch = DispatchLDAZeroPageX;

    /// <summary>Cached direct-dispatch delegate for LDA absolute.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteDispatch = DispatchLDAAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDA absolute,X.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteXDispatch = DispatchLDAAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for LDA absolute,Y.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteYDispatch = DispatchLDAAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page,X).</summary>
    internal static readonly OpcodeDispatch LDAIndirectXDispatch = DispatchLDAIndirectX;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page),Y.</summary>
    internal static readonly OpcodeDispatch LDAIndirectYDispatch = DispatchLDAIndirectY;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page).</summary>
    internal static readonly OpcodeDispatch LDAZeroPageIndirectDispatch = DispatchLDAZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for LDX immediate.</summary>
    internal static readonly OpcodeDispatch LDXImmediateDispatch = DispatchLDXImmediate;

    /// <summary>Cached direct-dispatch delegate for LDX zero page.</summary>
    internal static readonly OpcodeDispatch LDXZeroPageDispatch = DispatchLDXZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDX zero page,Y.</summary>
    internal static readonly OpcodeDispatch LDXZeroPageYDispatch = DispatchLDXZeroPageY;

    /// <summary>Cached direct-dispatch delegate for LDX absolute.</summary>
    internal static readonly OpcodeDispatch LDXAbsoluteDispatch = DispatchLDXAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDX absolute,Y.</summary>
    internal static readonly OpcodeDispatch LDXAbsoluteYDispatch = DispatchLDXAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for LDY immediate.</summary>
    internal static readonly OpcodeDispatch LDYImmediateDispatch = DispatchLDYImmediate;

    /// <summary>Cached direct-dispatch delegate for LDY zero page.</summary>
    internal static readonly OpcodeDispatch LDYZeroPageDispatch = DispatchLDYZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDY zero page,X.</summary>
    internal static readonly OpcodeDispatch LDYZeroPageXDispatch = DispatchLDYZeroPageX;

    /// <summary>Cached direct-dispatch delegate for LDY absolute.</summary>
    internal static readonly OpcodeDispatch LDYAbsoluteDispatch = DispatchLDYAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDY absolute,X.</summary>
    internal static readonly OpcodeDispatch LDYAbsoluteXDispatch = DispatchLDYAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for STA zero page.</summary>
    internal static readonly OpcodeDispatch STAZeroPageDispatch = DispatchSTAZeroPage;

    /// <summary>Cached direct-dispatch delegate for STA zero page,X.</summary>
    internal static readonly OpcodeDispatch STAZeroPageXDispatch = DispatchSTAZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STA absolute.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteDispatch = DispatchSTAAbsolute;

    /// <summary>Cached direct-dispatch delegate for STA absolute,X.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteXDispatch = DispatchSTAAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for STA absolute,Y.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteYDispatch = DispatchSTAAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for STA (zero page,X).</summary>
    internal static readonly OpcodeDispatch STAIndirectXDispatch = DispatchSTAIndirectX;

    /// <summary>Cached direct-dispatch delegate for STA (zero page),Y.</summary>
    internal static readonly OpcodeDispatch STAIndirectYDispatch = DispatchSTAIndirectY;

    /// <summary>Cached direct-dispatch delegate for STA (zero page).</summary>
    internal static readonly OpcodeDispatch STAZeroPageIndirectDispatch = DispatchSTAZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for STX zero page.</summary>
    internal static readonly OpcodeDispatch STXZeroPageDispatch = DispatchSTXZeroPage;

    /// <summary>Cached direct-dispatch delegate for STX zero page,Y.</summary>
    internal static readonly OpcodeDispatch STXZeroPageYDispatch = DispatchSTXZeroPageY;

    /// <summary>Cached direct-dispatch delegate for STX absolute.</summary>
    internal static readonly OpcodeDispatch STXAbsoluteDispatch = DispatchSTXAbsolute;

    /// <summary>Cached direct-dispatch delegate for STY zero page.</summary>
    internal static readonly OpcodeDispatch STYZeroPageDispatch = DispatchSTYZeroPage;

    /// <summary>Cached direct-dispatch delegate for STY zero page,X.</summary>
    internal static readonly OpcodeDispatch STYZeroPageXDispatch = DispatchSTYZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STY absolute.</summary>
    internal static readonly OpcodeDispatch STYAbsoluteDispatch = DispatchSTYAbsolute;

    /// <summary>Cached direct-dispatch delegate for STZ zero page.</summary>
    internal static readonly OpcodeDispatch STZZeroPageDispatch = DispatchSTZZeroPage;

    /// <summary>Cached direct-dispatch delegate for STZ zero page,X.</summary>
    internal static readonly OpcodeDispatch STZZeroPageXDispatch = DispatchSTZZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STZ absolute.</summary>
    internal static readonly OpcodeDispatch STZAbsoluteDispatch = DispatchSTZAbsolute;

    /// <summary>Cached direct-dispatch delegate for STZ absolute,X.</summary>
    internal static readonly OpcodeDispatch STZAbsoluteXDispatch = DispatchSTZAbsoluteX;

    /// <summary>
    /// NOP - No Operation instruction.
    /// </summary>
    /// <param name="addressingMode">The addressing mode function to use (typically Implied).</param>
    /// <param name="instructionCycles">
    /// Number of cycles the instruction itself consumes in addition to the base
    /// fetch cycle and any cycles already added by <paramref name="addressingMode"/>.
    /// Defaults to <c>1</c>, which produces a standard 2-cycle Implied NOP
    /// (<c>$EA</c>). Pass <c>0</c> for the WDC65C02S 1-byte/1-cycle "unused"
    /// NOP slots (column 3 of the opcode matrix). Pass a larger value to cover
    /// the 8-cycle "loud" NOP at <c>$5C</c>.
    /// </param>
    /// <returns>An opcode handler that executes NOP.</returns>
    /// <remarks>
    /// All NOP variants — including the WDC65C02S unused opcode slots that
    /// decode as multi-byte/multi-cycle NOPs — share this implementation. Flags
    /// and registers are never modified. PC advancement and addressing-mode
    /// cycles are entirely delegated to <paramref name="addressingMode"/>; the
    /// only state mutation here is the TCU increment.
    /// </remarks>
    public static OpcodeHandler NOP(AddressingModeHandler addressingMode, int instructionCycles = 1)
    {
        return cpu =>
        {
            addressingMode(cpu); // Call addressing mode (usually does nothing for Implied)

            if (instructionCycles > 0)
            {
                cpu.Registers.TCU += (ulong)instructionCycles;
            }
        };
    }

    /// <summary>
    /// BRK - Force Break instruction. Causes a software interrupt.
    /// </summary>
    /// <param name="addressingMode">The addressing mode function to use (typically Implied).</param>
    /// <returns>An opcode handler that executes BRK.</returns>
    public static OpcodeHandler BRK(AddressingModeHandler addressingMode)
    {
        return cpu =>
        {
            byte opCycles = 0;
            addressingMode(cpu);

            // BRK causes a software interrupt
            // Total 7 cycles: 1 (opcode fetch) + 1 (PC increment) + 2 (push PC) + 1 (push P) + 2 (read IRQ vector)
            cpu.Registers.PC.Advance();
            opCycles++; // PC increment cycle

            Word pc = cpu.Registers.PC.GetWord();

            cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), pc.HighByte());
            opCycles++; // Push PC high byte
            cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), pc.LowByte());
            opCycles++; // Push PC low byte
            cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), (byte)(cpu.Registers.P | ProcessorStatusFlags.B | ProcessorStatusFlags.R));
            opCycles++; // Push P

            cpu.Registers.P |= ProcessorStatusFlags.I;

            // 65C02: BRK clears the D flag after pushing status (NMOS 6502 does not)
            cpu.Registers.P &= ~ProcessorStatusFlags.D;
            cpu.Registers.PC.SetWord(cpu.Read16(Cpu65C02Constants.IrqVector));
            opCycles += 2; // Read IRQ vector (2 bytes)

            cpu.Registers.TCU += opCycles;
        };
    }

    private static void DispatchLDAImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDA<ImmediateMode>(cpu);

    private static void DispatchLDAZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDA<ZeroPageMode>(cpu);

    private static void DispatchLDAZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpLDA<ZeroPageXMode>(cpu);

    private static void DispatchLDAAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDA<AbsoluteMode>(cpu);

    private static void DispatchLDAAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpLDA<AbsoluteXMode>(cpu);

    private static void DispatchLDAAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpLDA<AbsoluteYMode>(cpu);

    private static void DispatchLDAIndirectX(ICpu cpu, in OpcodeEntry entry) => OpLDA<IndirectXMode>(cpu);

    private static void DispatchLDAIndirectY(ICpu cpu, in OpcodeEntry entry) => OpLDA<IndirectYMode>(cpu);

    private static void DispatchLDAZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpLDA<ZeroPageIndirectMode>(cpu);

    private static void DispatchLDXImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDX<ImmediateMode>(cpu);

    private static void DispatchLDXZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDX<ZeroPageMode>(cpu);

    private static void DispatchLDXZeroPageY(ICpu cpu, in OpcodeEntry entry) => OpLDX<ZeroPageYMode>(cpu);

    private static void DispatchLDXAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDX<AbsoluteMode>(cpu);

    private static void DispatchLDXAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpLDX<AbsoluteYMode>(cpu);

    private static void DispatchLDYImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDY<ImmediateMode>(cpu);

    private static void DispatchLDYZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDY<ZeroPageMode>(cpu);

    private static void DispatchLDYZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpLDY<ZeroPageXMode>(cpu);

    private static void DispatchLDYAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDY<AbsoluteMode>(cpu);

    private static void DispatchLDYAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpLDY<AbsoluteXMode>(cpu);

    private static void DispatchSTAZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTA<ZeroPageMode>(cpu);

    private static void DispatchSTAZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTA<ZeroPageXMode>(cpu);

    private static void DispatchSTAAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTA<AbsoluteMode>(cpu);

    private static void DispatchSTAAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpSTA<AbsoluteXWriteMode>(cpu);

    private static void DispatchSTAAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpSTA<AbsoluteYWriteMode>(cpu);

    private static void DispatchSTAIndirectX(ICpu cpu, in OpcodeEntry entry) => OpSTA<IndirectXMode>(cpu);

    private static void DispatchSTAIndirectY(ICpu cpu, in OpcodeEntry entry) => OpSTA<IndirectYWriteMode>(cpu);

    private static void DispatchSTAZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpSTA<ZeroPageIndirectMode>(cpu);

    private static void DispatchSTXZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTX<ZeroPageMode>(cpu);

    private static void DispatchSTXZeroPageY(ICpu cpu, in OpcodeEntry entry) => OpSTX<ZeroPageYMode>(cpu);

    private static void DispatchSTXAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTX<AbsoluteMode>(cpu);

    private static void DispatchSTYZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTY<ZeroPageMode>(cpu);

    private static void DispatchSTYZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTY<ZeroPageXMode>(cpu);

    private static void DispatchSTYAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTY<AbsoluteMode>(cpu);

    private static void DispatchSTZZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTZ<ZeroPageMode>(cpu);

    private static void DispatchSTZZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTZ<ZeroPageXMode>(cpu);

    private static void DispatchSTZAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTZ<AbsoluteMode>(cpu);

    private static void DispatchSTZAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpSTZ<AbsoluteXWriteMode>(cpu);

    private static void OpLDA<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.A.SetValue(value, size);
    }

    private static void OpLDX<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.X.SetValue(value, size);
    }

    private static void OpLDY<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.Y.SetValue(value, size);
    }

    private static void OpSTA<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        cpu.WriteValue(address, cpu.Registers.A.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    private static void OpSTX<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        cpu.WriteValue(address, cpu.Registers.X.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    private static void OpSTY<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        cpu.WriteValue(address, cpu.Registers.Y.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    private static void OpSTZ<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        cpu.Write8(address, 0x00);

        cpu.Registers.TCU += 1; // Memory write
    }
}