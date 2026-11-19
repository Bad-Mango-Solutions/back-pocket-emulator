// <copyright file="Instructions.Logical.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Logical operations (AND, ORA, EOR, BIT).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for AND immediate.</summary>
    internal static readonly OpcodeDispatch ANDImmediateDispatch = DispatchANDImmediate;

    /// <summary>Cached direct-dispatch delegate for AND zero page.</summary>
    internal static readonly OpcodeDispatch ANDZeroPageDispatch = DispatchANDZeroPage;

    /// <summary>Cached direct-dispatch delegate for AND zero page,X.</summary>
    internal static readonly OpcodeDispatch ANDZeroPageXDispatch = DispatchANDZeroPageX;

    /// <summary>Cached direct-dispatch delegate for AND absolute.</summary>
    internal static readonly OpcodeDispatch ANDAbsoluteDispatch = DispatchANDAbsolute;

    /// <summary>Cached direct-dispatch delegate for AND absolute,X.</summary>
    internal static readonly OpcodeDispatch ANDAbsoluteXDispatch = DispatchANDAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for AND absolute,Y.</summary>
    internal static readonly OpcodeDispatch ANDAbsoluteYDispatch = DispatchANDAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for AND (zero page,X).</summary>
    internal static readonly OpcodeDispatch ANDIndirectXDispatch = DispatchANDIndirectX;

    /// <summary>Cached direct-dispatch delegate for AND (zero page),Y.</summary>
    internal static readonly OpcodeDispatch ANDIndirectYDispatch = DispatchANDIndirectY;

    /// <summary>Cached direct-dispatch delegate for AND (zero page).</summary>
    internal static readonly OpcodeDispatch ANDZeroPageIndirectDispatch = DispatchANDZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for ORA immediate.</summary>
    internal static readonly OpcodeDispatch ORAImmediateDispatch = DispatchORAImmediate;

    /// <summary>Cached direct-dispatch delegate for ORA zero page.</summary>
    internal static readonly OpcodeDispatch ORAZeroPageDispatch = DispatchORAZeroPage;

    /// <summary>Cached direct-dispatch delegate for ORA zero page,X.</summary>
    internal static readonly OpcodeDispatch ORAZeroPageXDispatch = DispatchORAZeroPageX;

    /// <summary>Cached direct-dispatch delegate for ORA absolute.</summary>
    internal static readonly OpcodeDispatch ORAAbsoluteDispatch = DispatchORAAbsolute;

    /// <summary>Cached direct-dispatch delegate for ORA absolute,X.</summary>
    internal static readonly OpcodeDispatch ORAAbsoluteXDispatch = DispatchORAAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for ORA absolute,Y.</summary>
    internal static readonly OpcodeDispatch ORAAbsoluteYDispatch = DispatchORAAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for ORA (zero page,X).</summary>
    internal static readonly OpcodeDispatch ORAIndirectXDispatch = DispatchORAIndirectX;

    /// <summary>Cached direct-dispatch delegate for ORA (zero page),Y.</summary>
    internal static readonly OpcodeDispatch ORAIndirectYDispatch = DispatchORAIndirectY;

    /// <summary>Cached direct-dispatch delegate for ORA (zero page).</summary>
    internal static readonly OpcodeDispatch ORAZeroPageIndirectDispatch = DispatchORAZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for EOR immediate.</summary>
    internal static readonly OpcodeDispatch EORImmediateDispatch = DispatchEORImmediate;

    /// <summary>Cached direct-dispatch delegate for EOR zero page.</summary>
    internal static readonly OpcodeDispatch EORZeroPageDispatch = DispatchEORZeroPage;

    /// <summary>Cached direct-dispatch delegate for EOR zero page,X.</summary>
    internal static readonly OpcodeDispatch EORZeroPageXDispatch = DispatchEORZeroPageX;

    /// <summary>Cached direct-dispatch delegate for EOR absolute.</summary>
    internal static readonly OpcodeDispatch EORAbsoluteDispatch = DispatchEORAbsolute;

    /// <summary>Cached direct-dispatch delegate for EOR absolute,X.</summary>
    internal static readonly OpcodeDispatch EORAbsoluteXDispatch = DispatchEORAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for EOR absolute,Y.</summary>
    internal static readonly OpcodeDispatch EORAbsoluteYDispatch = DispatchEORAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for EOR (zero page,X).</summary>
    internal static readonly OpcodeDispatch EORIndirectXDispatch = DispatchEORIndirectX;

    /// <summary>Cached direct-dispatch delegate for EOR (zero page),Y.</summary>
    internal static readonly OpcodeDispatch EORIndirectYDispatch = DispatchEORIndirectY;

    /// <summary>Cached direct-dispatch delegate for EOR (zero page).</summary>
    internal static readonly OpcodeDispatch EORZeroPageIndirectDispatch = DispatchEORZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for BIT zero page.</summary>
    internal static readonly OpcodeDispatch BITZeroPageDispatch = DispatchBITZeroPage;

    /// <summary>Cached direct-dispatch delegate for BIT absolute.</summary>
    internal static readonly OpcodeDispatch BITAbsoluteDispatch = DispatchBITAbsolute;

    /// <summary>Cached direct-dispatch delegate for BIT zero page,X.</summary>
    internal static readonly OpcodeDispatch BITZeroPageXDispatch = DispatchBITZeroPageX;

    /// <summary>Cached direct-dispatch delegate for BIT absolute,X.</summary>
    internal static readonly OpcodeDispatch BITAbsoluteXDispatch = DispatchBITAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for BIT immediate.</summary>
    internal static readonly OpcodeDispatch BITImmediateDispatch = DispatchBITImmediate;

    private static void DispatchANDImmediate(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchANDZeroPage(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchANDZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchANDAbsolute(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchANDAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchANDAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchANDIndirectX(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchANDIndirectY(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, IndirectYMode>(As65C02(cpu));

    private static void DispatchANDZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpAND<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchORAImmediate(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchORAZeroPage(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchORAZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchORAAbsolute(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchORAAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchORAAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchORAIndirectX(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchORAIndirectY(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, IndirectYMode>(As65C02(cpu));

    private static void DispatchORAZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpORA<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchEORImmediate(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchEORZeroPage(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchEORZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchEORAbsolute(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchEORAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchEORAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchEORIndirectX(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchEORIndirectY(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, IndirectYMode>(As65C02(cpu));

    private static void DispatchEORZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpEOR<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchBITZeroPage(ICpu cpu, in OpcodeEntry entry) => OpBIT<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchBITAbsolute(ICpu cpu, in OpcodeEntry entry) => OpBIT<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchBITZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpBIT<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchBITAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpBIT<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchBITImmediate(ICpu cpu, in OpcodeEntry entry) => OpBITImmediate<Cpu65C02, ImmediateMode>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpAND<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte result = (byte)(cpu.Registers.A.GetByte() & value);
        cpu.Registers.A.SetByte(result);
        cpu.Registers.P.SetZeroAndNegative(result);

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpORA<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte result = (byte)(cpu.Registers.A.GetByte() | value);
        cpu.Registers.A.SetByte(result);
        cpu.Registers.P.SetZeroAndNegative(result);

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpEOR<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte result = (byte)(cpu.Registers.A.GetByte() ^ value);
        cpu.Registers.A.SetByte(result);
        cpu.Registers.P.SetZeroAndNegative(result);

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBIT<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte result = (byte)(cpu.Registers.A.GetByte() & value);

        // Set Z flag based on result
        if (result == 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.Z;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.Z;
        }

        // Set N flag from bit 7 of memory value
        if ((value & 0x80) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.N;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.N;
        }

        // Set V flag from bit 6 of memory value
        if ((value & 0x40) != 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.V;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.V;
        }

        cpu.Registers.TCU += opCycles;
    }

    // BIT immediate sets Z from A AND the operand. N and V are left unchanged.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBITImmediate<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte result = (byte)(cpu.Registers.A.GetByte() & value);

        // Set Z flag based on result. N and V are NOT affected for immediate mode.
        if (result == 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.Z;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.Z;
        }

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }
}