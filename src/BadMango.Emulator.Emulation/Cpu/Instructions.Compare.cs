// <copyright file="Instructions.Compare.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Comparison instructions (CMP, CPX, CPY).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for CMP immediate.</summary>
    internal static readonly OpcodeDispatch CMPImmediateDispatch = DispatchCMPImmediate;

    /// <summary>Cached direct-dispatch delegate for CMP zero page.</summary>
    internal static readonly OpcodeDispatch CMPZeroPageDispatch = DispatchCMPZeroPage;

    /// <summary>Cached direct-dispatch delegate for CMP zero page,X.</summary>
    internal static readonly OpcodeDispatch CMPZeroPageXDispatch = DispatchCMPZeroPageX;

    /// <summary>Cached direct-dispatch delegate for CMP absolute.</summary>
    internal static readonly OpcodeDispatch CMPAbsoluteDispatch = DispatchCMPAbsolute;

    /// <summary>Cached direct-dispatch delegate for CMP absolute,X.</summary>
    internal static readonly OpcodeDispatch CMPAbsoluteXDispatch = DispatchCMPAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for CMP absolute,Y.</summary>
    internal static readonly OpcodeDispatch CMPAbsoluteYDispatch = DispatchCMPAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for CMP (zero page,X).</summary>
    internal static readonly OpcodeDispatch CMPIndirectXDispatch = DispatchCMPIndirectX;

    /// <summary>Cached direct-dispatch delegate for CMP (zero page),Y.</summary>
    internal static readonly OpcodeDispatch CMPIndirectYDispatch = DispatchCMPIndirectY;

    /// <summary>Cached direct-dispatch delegate for CMP (zero page).</summary>
    internal static readonly OpcodeDispatch CMPZeroPageIndirectDispatch = DispatchCMPZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for CPX immediate.</summary>
    internal static readonly OpcodeDispatch CPXImmediateDispatch = DispatchCPXImmediate;

    /// <summary>Cached direct-dispatch delegate for CPX zero page.</summary>
    internal static readonly OpcodeDispatch CPXZeroPageDispatch = DispatchCPXZeroPage;

    /// <summary>Cached direct-dispatch delegate for CPX absolute.</summary>
    internal static readonly OpcodeDispatch CPXAbsoluteDispatch = DispatchCPXAbsolute;

    /// <summary>Cached direct-dispatch delegate for CPY immediate.</summary>
    internal static readonly OpcodeDispatch CPYImmediateDispatch = DispatchCPYImmediate;

    /// <summary>Cached direct-dispatch delegate for CPY zero page.</summary>
    internal static readonly OpcodeDispatch CPYZeroPageDispatch = DispatchCPYZeroPage;

    /// <summary>Cached direct-dispatch delegate for CPY absolute.</summary>
    internal static readonly OpcodeDispatch CPYAbsoluteDispatch = DispatchCPYAbsolute;

    private static void DispatchCMPImmediate(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchCMPZeroPage(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchCMPZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchCMPAbsolute(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchCMPAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchCMPAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchCMPIndirectX(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchCMPIndirectY(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, IndirectYMode>(As65C02(cpu));

    private static void DispatchCMPZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpCMP<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchCPXImmediate(ICpu cpu, in OpcodeEntry entry) => OpCPX<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchCPXZeroPage(ICpu cpu, in OpcodeEntry entry) => OpCPX<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchCPXAbsolute(ICpu cpu, in OpcodeEntry entry) => OpCPX<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchCPYImmediate(ICpu cpu, in OpcodeEntry entry) => OpCPY<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchCPYZeroPage(ICpu cpu, in OpcodeEntry entry) => OpCPY<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchCPYAbsolute(ICpu cpu, in OpcodeEntry entry) => OpCPY<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpCMP<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte a = cpu.Registers.A.GetByte();
        byte result = (byte)(a - value);

        // Set carry if A >= value
        if (a >= value)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

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
    private static void OpCPX<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte x = cpu.Registers.X.GetByte();
        byte result = (byte)(x - value);

        // Set carry if X >= value
        if (x >= value)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

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
    private static void OpCPY<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        byte y = cpu.Registers.Y.GetByte();
        byte result = (byte)(y - value);

        // Set carry if Y >= value
        if (y >= value)
        {
            cpu.Registers.P |= ProcessorStatusFlags.C;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.C;
        }

        cpu.Registers.P.SetZeroAndNegative(result);

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = value;
            cpu.Trace = cpu.Trace with { OperandSize = 1, Operands = operands };
        }

        cpu.Registers.TCU += opCycles;
    }
}