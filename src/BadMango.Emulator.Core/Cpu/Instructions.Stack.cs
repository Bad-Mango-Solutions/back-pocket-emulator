// <copyright file="Instructions.Stack.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

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

    private static void DispatchPHAImplied(ICpu cpu, in OpcodeEntry entry) => OpPHA<Cpu65C02>(As65C02(cpu));

    private static void DispatchPHPImplied(ICpu cpu, in OpcodeEntry entry) => OpPHP<Cpu65C02>(As65C02(cpu));

    private static void DispatchPLAImplied(ICpu cpu, in OpcodeEntry entry) => OpPLA<Cpu65C02>(As65C02(cpu));

    private static void DispatchPLPImplied(ICpu cpu, in OpcodeEntry entry) => OpPLP<Cpu65C02>(As65C02(cpu));

    private static void DispatchPHXImplied(ICpu cpu, in OpcodeEntry entry) => OpPHX<Cpu65C02>(As65C02(cpu));

    private static void DispatchPLXImplied(ICpu cpu, in OpcodeEntry entry) => OpPLX<Cpu65C02>(As65C02(cpu));

    private static void DispatchPHYImplied(ICpu cpu, in OpcodeEntry entry) => OpPHY<Cpu65C02>(As65C02(cpu));

    private static void DispatchPLYImplied(ICpu cpu, in OpcodeEntry entry) => OpPLY<Cpu65C02>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPHA<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.A.GetByte());
        cpu.Registers.TCU += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPHP<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), (byte)(cpu.Registers.P | ProcessorStatusFlags.B | ProcessorStatusFlags.R));
        cpu.Registers.TCU += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPLA<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.A.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPLP<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.Registers.P = PulledProcessorStatus(cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase)));
        cpu.Registers.TCU += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPHX<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.X.GetByte());
        cpu.Registers.TCU += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPLX<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.X.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPHY<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), cpu.Registers.Y.GetByte());
        cpu.Registers.TCU += 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpPLY<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImpliedMode.Resolve(cpu);
        byte value = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        cpu.Registers.Y.SetByte(value);
        cpu.Registers.P.SetZeroAndNegative(value);
        cpu.Registers.TCU += 3;
    }
}