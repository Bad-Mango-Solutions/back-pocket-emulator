// <copyright file="Instructions.Shift.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Shift and rotate instructions (ASL, LSR, ROL, ROR).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for ASL accumulator.</summary>
    internal static readonly OpcodeDispatch ASLAccumulatorDispatch = DispatchASLAccumulator;

    /// <summary>Cached direct-dispatch delegate for ASL zero page.</summary>
    internal static readonly OpcodeDispatch ASLZeroPageDispatch = DispatchASLZeroPage;

    /// <summary>Cached direct-dispatch delegate for ASL zero page,X.</summary>
    internal static readonly OpcodeDispatch ASLZeroPageXDispatch = DispatchASLZeroPageX;

    /// <summary>Cached direct-dispatch delegate for ASL absolute.</summary>
    internal static readonly OpcodeDispatch ASLAbsoluteDispatch = DispatchASLAbsolute;

    /// <summary>Cached direct-dispatch delegate for ASL absolute,X.</summary>
    internal static readonly OpcodeDispatch ASLAbsoluteXDispatch = DispatchASLAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for LSR accumulator.</summary>
    internal static readonly OpcodeDispatch LSRAccumulatorDispatch = DispatchLSRAccumulator;

    /// <summary>Cached direct-dispatch delegate for LSR zero page.</summary>
    internal static readonly OpcodeDispatch LSRZeroPageDispatch = DispatchLSRZeroPage;

    /// <summary>Cached direct-dispatch delegate for LSR zero page,X.</summary>
    internal static readonly OpcodeDispatch LSRZeroPageXDispatch = DispatchLSRZeroPageX;

    /// <summary>Cached direct-dispatch delegate for LSR absolute.</summary>
    internal static readonly OpcodeDispatch LSRAbsoluteDispatch = DispatchLSRAbsolute;

    /// <summary>Cached direct-dispatch delegate for LSR absolute,X.</summary>
    internal static readonly OpcodeDispatch LSRAbsoluteXDispatch = DispatchLSRAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for ROL accumulator.</summary>
    internal static readonly OpcodeDispatch ROLAccumulatorDispatch = DispatchROLAccumulator;

    /// <summary>Cached direct-dispatch delegate for ROL zero page.</summary>
    internal static readonly OpcodeDispatch ROLZeroPageDispatch = DispatchROLZeroPage;

    /// <summary>Cached direct-dispatch delegate for ROL zero page,X.</summary>
    internal static readonly OpcodeDispatch ROLZeroPageXDispatch = DispatchROLZeroPageX;

    /// <summary>Cached direct-dispatch delegate for ROL absolute.</summary>
    internal static readonly OpcodeDispatch ROLAbsoluteDispatch = DispatchROLAbsolute;

    /// <summary>Cached direct-dispatch delegate for ROL absolute,X.</summary>
    internal static readonly OpcodeDispatch ROLAbsoluteXDispatch = DispatchROLAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for ROR accumulator.</summary>
    internal static readonly OpcodeDispatch RORAccumulatorDispatch = DispatchRORAccumulator;

    /// <summary>Cached direct-dispatch delegate for ROR zero page.</summary>
    internal static readonly OpcodeDispatch RORZeroPageDispatch = DispatchRORZeroPage;

    /// <summary>Cached direct-dispatch delegate for ROR zero page,X.</summary>
    internal static readonly OpcodeDispatch RORZeroPageXDispatch = DispatchRORZeroPageX;

    /// <summary>Cached direct-dispatch delegate for ROR absolute.</summary>
    internal static readonly OpcodeDispatch RORAbsoluteDispatch = DispatchRORAbsolute;

    /// <summary>Cached direct-dispatch delegate for ROR absolute,X.</summary>
    internal static readonly OpcodeDispatch RORAbsoluteXDispatch = DispatchRORAbsoluteX;

    private static void DispatchASLAccumulator(ICpu cpu, in OpcodeEntry entry) => OpASLa<AccumulatorMode>(cpu);

    private static void DispatchASLZeroPage(ICpu cpu, in OpcodeEntry entry) => OpASL<ZeroPageMode>(cpu);

    private static void DispatchASLZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpASL<ZeroPageXMode>(cpu);

    private static void DispatchASLAbsolute(ICpu cpu, in OpcodeEntry entry) => OpASL<AbsoluteMode>(cpu);

    private static void DispatchASLAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpASL<AbsoluteXMode>(cpu);

    private static void DispatchLSRAccumulator(ICpu cpu, in OpcodeEntry entry) => OpLSRa<AccumulatorMode>(cpu);

    private static void DispatchLSRZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLSR<ZeroPageMode>(cpu);

    private static void DispatchLSRZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpLSR<ZeroPageXMode>(cpu);

    private static void DispatchLSRAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLSR<AbsoluteMode>(cpu);

    private static void DispatchLSRAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpLSR<AbsoluteXMode>(cpu);

    private static void DispatchROLAccumulator(ICpu cpu, in OpcodeEntry entry) => OpROLa<AccumulatorMode>(cpu);

    private static void DispatchROLZeroPage(ICpu cpu, in OpcodeEntry entry) => OpROL<ZeroPageMode>(cpu);

    private static void DispatchROLZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpROL<ZeroPageXMode>(cpu);

    private static void DispatchROLAbsolute(ICpu cpu, in OpcodeEntry entry) => OpROL<AbsoluteMode>(cpu);

    private static void DispatchROLAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpROL<AbsoluteXMode>(cpu);

    private static void DispatchRORAccumulator(ICpu cpu, in OpcodeEntry entry) => OpRORa<AccumulatorMode>(cpu);

    private static void DispatchRORZeroPage(ICpu cpu, in OpcodeEntry entry) => OpROR<ZeroPageMode>(cpu);

    private static void DispatchRORZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpROR<ZeroPageXMode>(cpu);

    private static void DispatchRORAbsolute(ICpu cpu, in OpcodeEntry entry) => OpROR<AbsoluteMode>(cpu);

    private static void DispatchRORAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpROR<AbsoluteXMode>(cpu);

    private static void OpASLa<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = cpu.Registers.A.GetByte();

        // Set carry from bit 7
        if ((value & 0x80) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value <<= 1;
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpASL<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        // Set carry from bit 7
        if ((value & 0x80) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value <<= 1;
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }

    private static void OpLSRa<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = cpu.Registers.A.GetByte();

        // Set carry from bit 0
        if ((value & 0x01) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value >>= 1;
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpLSR<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        // Set carry from bit 0
        if ((value & 0x01) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value >>= 1;
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }

    private static void OpROLa<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = cpu.Registers.A.GetByte();
        byte oldCarry = (byte)((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C); // C is bit 0, yields 0 or 1

        // Set carry from bit 7
        if ((value & 0x80) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value = (byte)((value << 1) | oldCarry);
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpROL<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte oldCarry = (byte)((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C); // C is bit 0, yields 0 or 1

        // Set carry from bit 7
        if ((value & 0x80) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value = (byte)((value << 1) | oldCarry);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }

    private static void OpRORa<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte value = cpu.Registers.A.GetByte();
        byte oldCarry = (byte)(((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C) << 7); // C is bit 0, shift to bit 7

        // Set carry from bit 0
        if ((value & 0x01) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value = (byte)((value >> 1) | oldCarry);
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Registers.TCU += 1;
    }

    private static void OpROR<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte oldCarry = (byte)(((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C) << 7); // C is bit 0, shift to bit 7

        // Set carry from bit 0
        if ((value & 0x01) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        value = (byte)((value >> 1) | oldCarry);
        cpu.Registers.P.SetZeroAndNegative(value);

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }
}