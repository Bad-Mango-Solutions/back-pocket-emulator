// <copyright file="Instructions.Transfer.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Register transfer instructions (TAX, TAY, TXA, TYA, TXS, TSX).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for TAX implied.</summary>
    internal static readonly OpcodeDispatch TAXImpliedDispatch = DispatchTAXImplied;

    /// <summary>Cached direct-dispatch delegate for TAY implied.</summary>
    internal static readonly OpcodeDispatch TAYImpliedDispatch = DispatchTAYImplied;

    /// <summary>Cached direct-dispatch delegate for TXA implied.</summary>
    internal static readonly OpcodeDispatch TXAImpliedDispatch = DispatchTXAImplied;

    /// <summary>Cached direct-dispatch delegate for TYA implied.</summary>
    internal static readonly OpcodeDispatch TYAImpliedDispatch = DispatchTYAImplied;

    /// <summary>Cached direct-dispatch delegate for TXS implied.</summary>
    internal static readonly OpcodeDispatch TXSImpliedDispatch = DispatchTXSImplied;

    /// <summary>Cached direct-dispatch delegate for TSX implied.</summary>
    internal static readonly OpcodeDispatch TSXImpliedDispatch = DispatchTSXImplied;

    private static void DispatchTAXImplied(ICpu cpu, in OpcodeEntry entry) => OpTAX<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchTAYImplied(ICpu cpu, in OpcodeEntry entry) => OpTAY<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchTXAImplied(ICpu cpu, in OpcodeEntry entry) => OpTXA<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchTYAImplied(ICpu cpu, in OpcodeEntry entry) => OpTYA<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchTXSImplied(ICpu cpu, in OpcodeEntry entry) => OpTXS<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchTSXImplied(ICpu cpu, in OpcodeEntry entry) => OpTSX<Cpu65C02, ImpliedMode>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTAX<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.Registers.A.GetValue(size);
        cpu.Registers.X.SetValue(value, size);
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.TCU += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTAY<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.Registers.A.GetValue(size);
        cpu.Registers.Y.SetValue(value, size);
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.TCU += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTXA<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        var value = cpu.Registers.X.GetValue(size);
        cpu.Registers.A.SetValue(value, size);
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.TCU += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTYA<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        var value = cpu.Registers.Y.GetValue(size);
        cpu.Registers.A.SetValue(value, size);
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.TCU += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTXS<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.SP.SetDWord(cpu.Registers.X.GetByte());
        cpu.Registers.TCU += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTSX<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        byte value = cpu.Registers.SP.GetByte();
        cpu.Registers.X.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.TCU += 1;
    }
}