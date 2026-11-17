// <copyright file="Instructions.Flags.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Flag manipulation instructions (CLC, SEC, CLI, SEI, CLD, SED, CLV).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for CLC implied.</summary>
    internal static readonly OpcodeDispatch CLCImpliedDispatch = DispatchCLCImplied;

    /// <summary>Cached direct-dispatch delegate for SEC implied.</summary>
    internal static readonly OpcodeDispatch SECImpliedDispatch = DispatchSECImplied;

    /// <summary>Cached direct-dispatch delegate for CLI implied.</summary>
    internal static readonly OpcodeDispatch CLIImpliedDispatch = DispatchCLIImplied;

    /// <summary>Cached direct-dispatch delegate for SEI implied.</summary>
    internal static readonly OpcodeDispatch SEIImpliedDispatch = DispatchSEIImplied;

    /// <summary>Cached direct-dispatch delegate for CLD implied.</summary>
    internal static readonly OpcodeDispatch CLDImpliedDispatch = DispatchCLDImplied;

    /// <summary>Cached direct-dispatch delegate for SED implied.</summary>
    internal static readonly OpcodeDispatch SEDImpliedDispatch = DispatchSEDImplied;

    /// <summary>Cached direct-dispatch delegate for CLV implied.</summary>
    internal static readonly OpcodeDispatch CLVImpliedDispatch = DispatchCLVImplied;

    private static void DispatchCLCImplied(ICpu cpu, in OpcodeEntry entry) => OpCLC<ImpliedMode>(cpu);

    private static void DispatchSECImplied(ICpu cpu, in OpcodeEntry entry) => OpSEC<ImpliedMode>(cpu);

    private static void DispatchCLIImplied(ICpu cpu, in OpcodeEntry entry) => OpCLI<ImpliedMode>(cpu);

    private static void DispatchSEIImplied(ICpu cpu, in OpcodeEntry entry) => OpSEI<ImpliedMode>(cpu);

    private static void DispatchCLDImplied(ICpu cpu, in OpcodeEntry entry) => OpCLD<ImpliedMode>(cpu);

    private static void DispatchSEDImplied(ICpu cpu, in OpcodeEntry entry) => OpSED<ImpliedMode>(cpu);

    private static void DispatchCLVImplied(ICpu cpu, in OpcodeEntry entry) => OpCLV<ImpliedMode>(cpu);

    private static void OpCLC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P &= ~ProcessorStatusFlags.C;
        cpu.Registers.TCU += 1;
    }

    private static void OpSEC<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P |= ProcessorStatusFlags.C;
        cpu.Registers.TCU += 1;
    }

    private static void OpCLI<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P &= ~ProcessorStatusFlags.I;
        cpu.Registers.TCU += 1;
    }

    private static void OpSEI<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P |= ProcessorStatusFlags.I;
        cpu.Registers.TCU += 1;
    }

    private static void OpCLD<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P &= ~ProcessorStatusFlags.D;
        cpu.Registers.TCU += 1;
    }

    private static void OpSED<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P |= ProcessorStatusFlags.D;
        cpu.Registers.TCU += 1;
    }

    private static void OpCLV<TMode>(ICpu cpu)
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);
        cpu.Registers.P &= ~ProcessorStatusFlags.V;
        cpu.Registers.TCU += 1;
    }
}