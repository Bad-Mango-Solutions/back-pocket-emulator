// <copyright file="Instructions.Branch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

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

    private static void DispatchBCCRelative(ICpu cpu, in OpcodeEntry entry) => OpBCC(cpu);

    private static void DispatchBCSRelative(ICpu cpu, in OpcodeEntry entry) => OpBCS(cpu);

    private static void DispatchBEQRelative(ICpu cpu, in OpcodeEntry entry) => OpBEQ(cpu);

    private static void DispatchBNERelative(ICpu cpu, in OpcodeEntry entry) => OpBNE(cpu);

    private static void DispatchBMIRelative(ICpu cpu, in OpcodeEntry entry) => OpBMI(cpu);

    private static void DispatchBPLRelative(ICpu cpu, in OpcodeEntry entry) => OpBPL(cpu);

    private static void DispatchBVCRelative(ICpu cpu, in OpcodeEntry entry) => OpBVC(cpu);

    private static void DispatchBVSRelative(ICpu cpu, in OpcodeEntry entry) => OpBVS(cpu);

    private static void DispatchBRARelative(ICpu cpu, in OpcodeEntry entry) => OpBRA(cpu);

    private static void OpBCC(ICpu cpu)
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

    private static void OpBCS(ICpu cpu)
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

    private static void OpBEQ(ICpu cpu)
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

    private static void OpBNE(ICpu cpu)
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

    private static void OpBMI(ICpu cpu)
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

    private static void OpBPL(ICpu cpu)
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

    private static void OpBVC(ICpu cpu)
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

    private static void OpBVS(ICpu cpu)
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

    private static void OpBRA(ICpu cpu)
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