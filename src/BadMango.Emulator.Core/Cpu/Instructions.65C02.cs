// <copyright file="Instructions.65C02.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// 65C02-specific instructions (TSB, TRB, WAI, STP). STZ is dispatched with the other stores.
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for TSB zero page.</summary>
    internal static readonly OpcodeDispatch TSBZeroPageDispatch = DispatchTSBZeroPage;

    /// <summary>Cached direct-dispatch delegate for TSB absolute.</summary>
    internal static readonly OpcodeDispatch TSBAbsoluteDispatch = DispatchTSBAbsolute;

    /// <summary>Cached direct-dispatch delegate for TRB zero page.</summary>
    internal static readonly OpcodeDispatch TRBZeroPageDispatch = DispatchTRBZeroPage;

    /// <summary>Cached direct-dispatch delegate for TRB absolute.</summary>
    internal static readonly OpcodeDispatch TRBAbsoluteDispatch = DispatchTRBAbsolute;

    /// <summary>Cached direct-dispatch delegate for WAI implied.</summary>
    internal static readonly OpcodeDispatch WAIImpliedDispatch = DispatchWAIImplied;

    /// <summary>Cached direct-dispatch delegate for STP implied.</summary>
    internal static readonly OpcodeDispatch STPImpliedDispatch = DispatchSTPImplied;

    private static void DispatchTSBZeroPage(ICpu cpu, in OpcodeEntry entry) => OpTSB<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchTSBAbsolute(ICpu cpu, in OpcodeEntry entry) => OpTSB<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchTRBZeroPage(ICpu cpu, in OpcodeEntry entry) => OpTRB<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchTRBAbsolute(ICpu cpu, in OpcodeEntry entry) => OpTRB<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchWAIImplied(ICpu cpu, in OpcodeEntry entry) => OpWAI<Cpu65C02>(As65C02(cpu));

    private static void DispatchSTPImplied(ICpu cpu, in OpcodeEntry entry) => OpSTP<Cpu65C02>(As65C02(cpu));

    // TSB tests A AND M into Z, then sets the memory bits that are set in A (M = M OR A).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTSB<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);

        byte a = cpu.Registers.A.GetByte();

        // Set Z flag based on A AND M
        if ((a & value) == 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.Z;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.Z;
        }

        // Set bits in memory (M = M OR A)
        value |= a;
        cpu.Write8(address, value);

        cpu.Registers.TCU += 3; // Memory read + Memory write + internal operation
    }

    // TRB tests A AND M into Z, then clears the memory bits that are set in A (M = M AND NOT A).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpTRB<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);

        byte a = cpu.Registers.A.GetByte();

        // Set Z flag based on A AND M
        if ((a & value) == 0)
        {
            cpu.Registers.P |= ProcessorStatusFlags.Z;
        }
        else
        {
            cpu.Registers.P &= ~ProcessorStatusFlags.Z;
        }

        // Clear bits in memory (M = M AND (NOT A))
        value &= (byte)~a;
        cpu.Write8(address, value);

        cpu.Registers.TCU += 3; // Memory read + Memory write + internal operation
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpWAI<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.HaltReason = HaltState.Wai;

        cpu.Registers.TCU += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSTP<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.HaltReason = HaltState.Stp;

        cpu.Registers.TCU += 2;
    }
}