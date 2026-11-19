// <copyright file="Instructions.Branch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Branch instructions (BCC, BCS, BEQ, BNE, BMI, BPL, BVC, BVS, BRA).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for BCC relative.</summary>
    internal static readonly OpcodeDispatch BCCRelativeDispatch = DispatchBCCRelative;

    /// <summary>Cached direct-dispatch delegate for BCS relative.</summary>
    internal static readonly OpcodeDispatch BCSRelativeDispatch = DispatchBCSRelative;

    /// <summary>Cached direct-dispatch delegate for BEQ relative.</summary>
    internal static readonly OpcodeDispatch BEQRelativeDispatch = DispatchBEQRelative;

    /// <summary>Cached direct-dispatch delegate for BNE relative.</summary>
    internal static readonly OpcodeDispatch BNERelativeDispatch = DispatchBNERelative;

    /// <summary>Cached direct-dispatch delegate for BMI relative.</summary>
    internal static readonly OpcodeDispatch BMIRelativeDispatch = DispatchBMIRelative;

    /// <summary>Cached direct-dispatch delegate for BPL relative.</summary>
    internal static readonly OpcodeDispatch BPLRelativeDispatch = DispatchBPLRelative;

    /// <summary>Cached direct-dispatch delegate for BVC relative.</summary>
    internal static readonly OpcodeDispatch BVCRelativeDispatch = DispatchBVCRelative;

    /// <summary>Cached direct-dispatch delegate for BVS relative.</summary>
    internal static readonly OpcodeDispatch BVSRelativeDispatch = DispatchBVSRelative;

    /// <summary>Cached direct-dispatch delegate for BRA relative.</summary>
    internal static readonly OpcodeDispatch BRARelativeDispatch = DispatchBRARelative;

    private static void DispatchBCCRelative(ICpu cpu, in OpcodeEntry entry) => OpBCC<Cpu65C02>(As65C02(cpu));

    private static void DispatchBCSRelative(ICpu cpu, in OpcodeEntry entry) => OpBCS<Cpu65C02>(As65C02(cpu));

    private static void DispatchBEQRelative(ICpu cpu, in OpcodeEntry entry) => OpBEQ<Cpu65C02>(As65C02(cpu));

    private static void DispatchBNERelative(ICpu cpu, in OpcodeEntry entry) => OpBNE<Cpu65C02>(As65C02(cpu));

    private static void DispatchBMIRelative(ICpu cpu, in OpcodeEntry entry) => OpBMI<Cpu65C02>(As65C02(cpu));

    private static void DispatchBPLRelative(ICpu cpu, in OpcodeEntry entry) => OpBPL<Cpu65C02>(As65C02(cpu));

    private static void DispatchBVCRelative(ICpu cpu, in OpcodeEntry entry) => OpBVC<Cpu65C02>(As65C02(cpu));

    private static void DispatchBVSRelative(ICpu cpu, in OpcodeEntry entry) => OpBVS<Cpu65C02>(As65C02(cpu));

    private static void DispatchBRARelative(ICpu cpu, in OpcodeEntry entry) => OpBRA<Cpu65C02>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBCC<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C) == 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBCS<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.C) != 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBEQ<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.Z) != 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBNE<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.Z) == 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBMI<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.N) != 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBPL<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.N) == 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBVC<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.V) == 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBVS<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);

        if (((byte)cpu.Registers.P & (byte)ProcessorStatusFlags.V) != 0)
        {
            Word oldPC = cpu.Registers.PC.GetWord();
            cpu.Registers.PC.SetWord((Word)targetAddr);
            opCycles++; // Branch taken

            if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
            {
                opCycles++; // Page boundary crossed
            }
        }

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBRA<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        Addr targetAddr = RelativeMode.Resolve(cpu);
        Word oldPC = cpu.Registers.PC.GetWord();
        cpu.Registers.PC.SetWord((Word)targetAddr);
        opCycles++; // Branch always taken

        if ((oldPC & 0xFF00) != (targetAddr & 0xFF00))
        {
            opCycles++; // Page boundary crossed
        }

        cpu.Registers.TCU += opCycles;
    }
}