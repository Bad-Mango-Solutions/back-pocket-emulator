// <copyright file="Instructions.Arithmetic.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Arithmetic instructions (ADC, SBC, INC, DEC, INX, INY, DEX, DEY, INA, DEA).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for ADC immediate.</summary>
    internal static readonly OpcodeDispatch ADCImmediateDispatch = DispatchADCImmediate;

    /// <summary>Cached direct-dispatch delegate for ADC zero page.</summary>
    internal static readonly OpcodeDispatch ADCZeroPageDispatch = DispatchADCZeroPage;

    /// <summary>Cached direct-dispatch delegate for ADC zero page,X.</summary>
    internal static readonly OpcodeDispatch ADCZeroPageXDispatch = DispatchADCZeroPageX;

    /// <summary>Cached direct-dispatch delegate for ADC absolute.</summary>
    internal static readonly OpcodeDispatch ADCAbsoluteDispatch = DispatchADCAbsolute;

    /// <summary>Cached direct-dispatch delegate for ADC absolute,X.</summary>
    internal static readonly OpcodeDispatch ADCAbsoluteXDispatch = DispatchADCAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for ADC absolute,Y.</summary>
    internal static readonly OpcodeDispatch ADCAbsoluteYDispatch = DispatchADCAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for ADC (zero page,X).</summary>
    internal static readonly OpcodeDispatch ADCIndirectXDispatch = DispatchADCIndirectX;

    /// <summary>Cached direct-dispatch delegate for ADC (zero page),Y.</summary>
    internal static readonly OpcodeDispatch ADCIndirectYDispatch = DispatchADCIndirectY;

    /// <summary>Cached direct-dispatch delegate for ADC (zero page).</summary>
    internal static readonly OpcodeDispatch ADCZeroPageIndirectDispatch = DispatchADCZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for SBC immediate.</summary>
    internal static readonly OpcodeDispatch SBCImmediateDispatch = DispatchSBCImmediate;

    /// <summary>Cached direct-dispatch delegate for SBC zero page.</summary>
    internal static readonly OpcodeDispatch SBCZeroPageDispatch = DispatchSBCZeroPage;

    /// <summary>Cached direct-dispatch delegate for SBC zero page,X.</summary>
    internal static readonly OpcodeDispatch SBCZeroPageXDispatch = DispatchSBCZeroPageX;

    /// <summary>Cached direct-dispatch delegate for SBC absolute.</summary>
    internal static readonly OpcodeDispatch SBCAbsoluteDispatch = DispatchSBCAbsolute;

    /// <summary>Cached direct-dispatch delegate for SBC absolute,X.</summary>
    internal static readonly OpcodeDispatch SBCAbsoluteXDispatch = DispatchSBCAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for SBC absolute,Y.</summary>
    internal static readonly OpcodeDispatch SBCAbsoluteYDispatch = DispatchSBCAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for SBC (zero page,X).</summary>
    internal static readonly OpcodeDispatch SBCIndirectXDispatch = DispatchSBCIndirectX;

    /// <summary>Cached direct-dispatch delegate for SBC (zero page),Y.</summary>
    internal static readonly OpcodeDispatch SBCIndirectYDispatch = DispatchSBCIndirectY;

    /// <summary>Cached direct-dispatch delegate for SBC (zero page).</summary>
    internal static readonly OpcodeDispatch SBCZeroPageIndirectDispatch = DispatchSBCZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for INC zero page.</summary>
    internal static readonly OpcodeDispatch INCZeroPageDispatch = DispatchINCZeroPage;

    /// <summary>Cached direct-dispatch delegate for INC zero page,X.</summary>
    internal static readonly OpcodeDispatch INCZeroPageXDispatch = DispatchINCZeroPageX;

    /// <summary>Cached direct-dispatch delegate for INC absolute.</summary>
    internal static readonly OpcodeDispatch INCAbsoluteDispatch = DispatchINCAbsolute;

    /// <summary>Cached direct-dispatch delegate for INC absolute,X.</summary>
    internal static readonly OpcodeDispatch INCAbsoluteXDispatch = DispatchINCAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for DEC zero page.</summary>
    internal static readonly OpcodeDispatch DECZeroPageDispatch = DispatchDECZeroPage;

    /// <summary>Cached direct-dispatch delegate for DEC zero page,X.</summary>
    internal static readonly OpcodeDispatch DECZeroPageXDispatch = DispatchDECZeroPageX;

    /// <summary>Cached direct-dispatch delegate for DEC absolute.</summary>
    internal static readonly OpcodeDispatch DECAbsoluteDispatch = DispatchDECAbsolute;

    /// <summary>Cached direct-dispatch delegate for DEC absolute,X.</summary>
    internal static readonly OpcodeDispatch DECAbsoluteXDispatch = DispatchDECAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for INA accumulator.</summary>
    internal static readonly OpcodeDispatch INAAccumulatorDispatch = DispatchINAAccumulator;

    /// <summary>Cached direct-dispatch delegate for DEA accumulator.</summary>
    internal static readonly OpcodeDispatch DEAAccumulatorDispatch = DispatchDEAAccumulator;

    /// <summary>Cached direct-dispatch delegate for INX implied.</summary>
    internal static readonly OpcodeDispatch INXImpliedDispatch = DispatchINXImplied;

    /// <summary>Cached direct-dispatch delegate for INY implied.</summary>
    internal static readonly OpcodeDispatch INYImpliedDispatch = DispatchINYImplied;

    /// <summary>Cached direct-dispatch delegate for DEX implied.</summary>
    internal static readonly OpcodeDispatch DEXImpliedDispatch = DispatchDEXImplied;

    /// <summary>Cached direct-dispatch delegate for DEY implied.</summary>
    internal static readonly OpcodeDispatch DEYImpliedDispatch = DispatchDEYImplied;

    private static void DispatchADCImmediate(ICpu cpu, in OpcodeEntry entry) => OpADC<ImmediateMode>(cpu);

    private static void DispatchADCZeroPage(ICpu cpu, in OpcodeEntry entry) => OpADC<ZeroPageMode>(cpu);

    private static void DispatchADCZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpADC<ZeroPageXMode>(cpu);

    private static void DispatchADCAbsolute(ICpu cpu, in OpcodeEntry entry) => OpADC<AbsoluteMode>(cpu);

    private static void DispatchADCAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpADC<AbsoluteXMode>(cpu);

    private static void DispatchADCAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpADC<AbsoluteYMode>(cpu);

    private static void DispatchADCIndirectX(ICpu cpu, in OpcodeEntry entry) => OpADC<IndirectXMode>(cpu);

    private static void DispatchADCIndirectY(ICpu cpu, in OpcodeEntry entry) => OpADC<IndirectYMode>(cpu);

    private static void DispatchADCZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpADC<ZeroPageIndirectMode>(cpu);

    private static void DispatchSBCImmediate(ICpu cpu, in OpcodeEntry entry) => OpSBC<ImmediateMode>(cpu);

    private static void DispatchSBCZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSBC<ZeroPageMode>(cpu);

    private static void DispatchSBCZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSBC<ZeroPageXMode>(cpu);

    private static void DispatchSBCAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSBC<AbsoluteMode>(cpu);

    private static void DispatchSBCAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpSBC<AbsoluteXMode>(cpu);

    private static void DispatchSBCAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpSBC<AbsoluteYMode>(cpu);

    private static void DispatchSBCIndirectX(ICpu cpu, in OpcodeEntry entry) => OpSBC<IndirectXMode>(cpu);

    private static void DispatchSBCIndirectY(ICpu cpu, in OpcodeEntry entry) => OpSBC<IndirectYMode>(cpu);

    private static void DispatchSBCZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpSBC<ZeroPageIndirectMode>(cpu);

    private static void DispatchINCZeroPage(ICpu cpu, in OpcodeEntry entry) => OpINC<ZeroPageMode>(cpu);

    private static void DispatchINCZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpINC<ZeroPageXMode>(cpu);

    private static void DispatchINCAbsolute(ICpu cpu, in OpcodeEntry entry) => OpINC<AbsoluteMode>(cpu);

    private static void DispatchINCAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpINC<AbsoluteXWriteMode>(cpu);

    private static void DispatchDECZeroPage(ICpu cpu, in OpcodeEntry entry) => OpDEC<ZeroPageMode>(cpu);

    private static void DispatchDECZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpDEC<ZeroPageXMode>(cpu);

    private static void DispatchDECAbsolute(ICpu cpu, in OpcodeEntry entry) => OpDEC<AbsoluteMode>(cpu);

    private static void DispatchDECAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpDEC<AbsoluteXWriteMode>(cpu);

    private static void DispatchINAAccumulator(ICpu cpu, in OpcodeEntry entry) => OpINA<AccumulatorMode>(cpu);

    private static void DispatchDEAAccumulator(ICpu cpu, in OpcodeEntry entry) => OpDEA<AccumulatorMode>(cpu);

    private static void DispatchINXImplied(ICpu cpu, in OpcodeEntry entry) => OpINX<ImpliedMode>(cpu);

    private static void DispatchINYImplied(ICpu cpu, in OpcodeEntry entry) => OpINY<ImpliedMode>(cpu);

    private static void DispatchDEXImplied(ICpu cpu, in OpcodeEntry entry) => OpDEX<ImpliedMode>(cpu);

    private static void DispatchDEYImplied(ICpu cpu, in OpcodeEntry entry) => OpDEY<ImpliedMode>(cpu);

    private static void OpADC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte a = cpu.Registers.A.GetByte();
        byte carry = (byte)((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C); // C is bit 0, yields 0 or 1

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.D) != 0)
        {
            // Decimal (BCD) mode — 65C02 sets N, V, Z correctly based on the final BCD
            // result (NMOS 6502 left N/V/Z undefined in decimal mode). This costs one
            // extra cycle compared to binary mode.
            int al = (a & 0x0F) + (value & 0x0F) + carry;
            if (al > 9)
            {
                al += 6;
            }

            int ah = (a >> 4) + (value >> 4) + (al > 15 ? 1 : 0);

            // V flag is computed from the signed-binary overflow of the high-nibble
            // sum BEFORE the BCD correction (WDC W65C02S behavior).
            if ((((ah << 4) ^ a) & ~(a ^ value) & 0x80) != 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.V;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.V;
            }

            if (ah > 9)
            {
                ah += 6;
            }

            byte result = (byte)(((ah << 4) | (al & 0x0F)) & 0xFF);

            if (ah > 15)
            {
                cpu.Registers.P |= ProcessorStatusFlags.C;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.C;
            }

            cpu.Registers.P.SetZeroAndNegative(result);
            cpu.Registers.A.SetByte(result);
            opCycles++; // 65C02: decimal mode takes one extra cycle for correct flag computation
        }
        else
        {
            // Binary mode
            int result = a + value + carry;
            byte result8 = (byte)(result & 0xFF);

            if (result > 0xFF)
            {
                cpu.Registers.P |= ProcessorStatusFlags.C;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.C;
            }

            // Set overflow: (A^result) & (value^result) & 0x80
            if (((a ^ result8) & (value ^ result8) & 0x80) != 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.V;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.V;
            }

            cpu.Registers.P.SetZeroAndNegative(result8);
            cpu.Registers.A.SetByte(result8);
        }

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }

    private static void OpSBC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte a = cpu.Registers.A.GetByte();
        byte borrow = (byte)(((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C) ^ 0x01); // C is bit 0, invert for borrow

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.D) != 0)
        {
            // Decimal SBC on the 65C02. The low-nibble correction is subtracted from the
            // whole byte, so that -6 can borrow into the high nibble. Masking the
            // correction inside the low nibble leaves the high digit one too large
            // whenever the low digit is 0-5. N and Z follow the corrected result.
            // V and C follow the binary subtraction, before the BCD adjustment.
            // The adjustment takes one extra cycle.
            int difference = a - value - borrow;
            int low = (a & 0x0F) - (value & 0x0F) - borrow;
            int high = (a >> 4) - (value >> 4) - (low < 0 ? 1 : 0);

            if (((a ^ value) & (a ^ (difference & 0xFF)) & 0x80) != 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.V;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.V;
            }

            if (difference >= 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.C;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.C;
            }

            int result = ((high << 4) | (low & 0x0F)) & 0xFF;
            if (low < 0)
            {
                result = (result - 6) & 0xFF;
            }

            if (high < 0)
            {
                result = (result - 0x60) & 0xFF;
            }

            byte result8 = (byte)result;
            cpu.Registers.P.SetZeroAndNegative(result8);
            cpu.Registers.A.SetByte(result8);
            opCycles++; // 65C02: decimal mode takes one extra cycle for correct flag computation
        }
        else
        {
            // Binary mode
            int result = a - value - borrow;
            byte result8 = (byte)(result & 0xFF);

            if (result >= 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.C;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.C;
            }

            // Set overflow: (A^value) & (A^result) & 0x80
            if (((a ^ value) & (a ^ result8) & 0x80) != 0)
            {
                cpu.Registers.P |= ProcessorStatusFlags.V;
            }
            else
            {
                cpu.Registers.P &= ~ProcessorStatusFlags.V;
            }

            cpu.Registers.P.SetZeroAndNegative(result8);
            cpu.Registers.A.SetByte(result8);
        }

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }

    private static void OpINC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        value++;
        cpu.Write8(address, value);
        opCycles++; // Memory write

        cpu.Registers.P.SetZeroAndNegative(value);
        opCycles++; // Internal operation

        cpu.Registers.TCU += opCycles;
    }

    private static void OpDEC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        value--;
        cpu.Write8(address, value);
        opCycles++; // Memory write

        cpu.Registers.P.SetZeroAndNegative(value);
        opCycles++; // Internal operation

        cpu.Registers.TCU += opCycles;
    }

    // 65C02 INC A. Operates on the accumulator.
    private static void OpINA<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.A.GetByte() + 1);
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    // 65C02 DEC A. Operates on the accumulator.
    private static void OpDEA<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.A.GetByte() - 1);
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpINX<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.X.GetByte() + 1);
        cpu.Registers.X.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpINY<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.Y.GetByte() + 1);
        cpu.Registers.Y.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpDEX<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.X.GetByte() - 1);
        cpu.Registers.X.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpDEY<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = (byte)(cpu.Registers.Y.GetByte() - 1);
        cpu.Registers.Y.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }
}