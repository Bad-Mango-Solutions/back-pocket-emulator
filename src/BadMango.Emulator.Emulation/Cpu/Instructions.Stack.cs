// <copyright file="Instructions.Stack.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Stack operations (PHA, PHP, PLA, PLP, PHX, PLX, PHY, PLY).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for PHA implied.</summary>
    internal static readonly OpcodeDispatch PHAImpliedDispatch = DispatchPHAImplied;

    /// <summary>Cached direct-dispatch delegate for PHP implied.</summary>
    internal static readonly OpcodeDispatch PHPImpliedDispatch = DispatchPHPImplied;

    /// <summary>Cached direct-dispatch delegate for PLA implied.</summary>
    internal static readonly OpcodeDispatch PLAImpliedDispatch = DispatchPLAImplied;

    /// <summary>Cached direct-dispatch delegate for PLP implied.</summary>
    internal static readonly OpcodeDispatch PLPImpliedDispatch = DispatchPLPImplied;

    /// <summary>Cached direct-dispatch delegate for PHX implied.</summary>
    internal static readonly OpcodeDispatch PHXImpliedDispatch = DispatchPHXImplied;

    /// <summary>Cached direct-dispatch delegate for PLX implied.</summary>
    internal static readonly OpcodeDispatch PLXImpliedDispatch = DispatchPLXImplied;

    /// <summary>Cached direct-dispatch delegate for PHY implied.</summary>
    internal static readonly OpcodeDispatch PHYImpliedDispatch = DispatchPHYImplied;

    /// <summary>Cached direct-dispatch delegate for PLY implied.</summary>
    internal static readonly OpcodeDispatch PLYImpliedDispatch = DispatchPLYImplied;

    /// <summary>
    /// Turns a status byte pulled by PLP or RTI into the live P register.
    /// </summary>
    /// <param name="status">The byte read from the stack.</param>
    /// <returns>
    /// The live flags. B is clear, bit 5 is set, and N, V, D, I, Z, and C come from <paramref name="status"/>.
    /// </returns>
    /// <remarks>
    /// B and bit 5 are not latches in the 65C02 status register. PHP and BRK set both in the
    /// copy they push. Pulling that byte must not leave B set, and a read of P shows bit 5 set.
    /// </remarks>
    internal static ProcessorStatusFlags PulledProcessorStatus(byte status)
        => (ProcessorStatusFlags)((status | (byte)ProcessorStatusFlags.R) & ~(byte)ProcessorStatusFlags.B);

    private static void DispatchPHAImplied(ICpu cpu, in OpcodeEntry entry) => OpPHA(cpu);

    private static void DispatchPHPImplied(ICpu cpu, in OpcodeEntry entry) => OpPHP(cpu);

    private static void DispatchPLAImplied(ICpu cpu, in OpcodeEntry entry) => OpPLA(cpu);

    private static void DispatchPLPImplied(ICpu cpu, in OpcodeEntry entry) => OpPLP(cpu);

    private static void DispatchPHXImplied(ICpu cpu, in OpcodeEntry entry) => OpPHX(cpu);

    private static void DispatchPLXImplied(ICpu cpu, in OpcodeEntry entry) => OpPLX(cpu);

    private static void DispatchPHYImplied(ICpu cpu, in OpcodeEntry entry) => OpPHY(cpu);

    private static void DispatchPLYImplied(ICpu cpu, in OpcodeEntry entry) => OpPLY(cpu);

    private static void OpPHA(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.A.GetByte());
        cpu.Registers.TCU += 2;
    }

    private static void OpPHP(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), (byte)(cpu.Registers.P | ProcessorStatusFlags.B | ProcessorStatusFlags.R));
        cpu.Registers.TCU += 2;
    }

    private static void OpPLA(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }

    private static void OpPLP(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        cpu.Registers.P = PulledProcessorStatus(cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase)));
        cpu.Registers.TCU += 3;
    }

    private static void OpPHX(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.X.GetByte());
        cpu.Registers.TCU += 2;
    }

    private static void OpPLX(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.X.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }

    private static void OpPHY(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.Y.GetByte());
        cpu.Registers.TCU += 2;
    }

    private static void OpPLY(ICpu cpu)
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.Y.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }
}