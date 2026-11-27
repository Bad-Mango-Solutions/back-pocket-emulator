// <copyright file="Instructions.Jump.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core;
using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Jump and subroutine instructions (JMP, JSR, RTS, RTI).
/// </content>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for JMP absolute.</summary>
    internal static readonly OpcodeDispatch JMPAbsoluteDispatch = DispatchJMPAbsolute;

    /// <summary>Cached direct-dispatch delegate for JMP indirect.</summary>
    internal static readonly OpcodeDispatch JMPIndirectDispatch = DispatchJMPIndirect;

    /// <summary>Cached direct-dispatch delegate for JMP absolute indirect,X.</summary>
    internal static readonly OpcodeDispatch JMPAbsoluteIndirectXDispatch = DispatchJMPAbsoluteIndirectX;

    /// <summary>Cached direct-dispatch delegate for JSR absolute.</summary>
    internal static readonly OpcodeDispatch JSRAbsoluteDispatch = DispatchJSRAbsolute;

    /// <summary>Cached direct-dispatch delegate for RTS implied.</summary>
    internal static readonly OpcodeDispatch RTSImpliedDispatch = DispatchRTSImplied;

    /// <summary>Cached direct-dispatch delegate for RTI implied.</summary>
    internal static readonly OpcodeDispatch RTIImpliedDispatch = DispatchRTIImplied;

    private static void DispatchJMPAbsolute(ICpu cpu, in OpcodeEntry entry) => OpJMP<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchJMPIndirect(ICpu cpu, in OpcodeEntry entry) => OpJMP<Cpu65C02, IndirectMode>(As65C02(cpu));

    private static void DispatchJMPAbsoluteIndirectX(ICpu cpu, in OpcodeEntry entry) => OpJMP<Cpu65C02, AbsoluteIndirectXMode>(As65C02(cpu));

    private static void DispatchJSRAbsolute(ICpu cpu, in OpcodeEntry entry) => OpJSR<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchRTSImplied(ICpu cpu, in OpcodeEntry entry) => OpRTS<Cpu65C02>(As65C02(cpu));

    private static void DispatchRTIImplied(ICpu cpu, in OpcodeEntry entry) => OpRTI<Cpu65C02>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpJMP<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr targetAddr = TMode.Resolve(cpu);
        cpu.Registers.PC.SetWord((Word)targetAddr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpJSR<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr targetAddr = TMode.Resolve(cpu);

        // JSR pushes PC - 1 to the stack (6502 behavior)
        // This is because RTS increments the pulled address before setting PC
        Word returnAddr = (Word)(cpu.Registers.PC.GetWord() - 1);

        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), returnAddr.HighByte());
        opCycles++; // Push high byte
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), returnAddr.LowByte());
        opCycles++; // Push low byte
        cpu.Registers.PC.SetAddr(targetAddr);
        opCycles++; // Internal operation

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpRTS<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        ImpliedMode.Resolve(cpu);

        byte lo = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        opCycles++; // Pull low byte
        byte hi = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        opCycles++; // Pull high byte
        cpu.Registers.PC.SetWord((Word)(((hi << 8) | lo) + 1));
        opCycles += 3; // Internal operations

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpRTI<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        byte opCycles = 0;
        ImpliedMode.Resolve(cpu);
        cpu.Registers.P = PulledProcessorStatus(cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase)));
        opCycles++; // Pull P
        byte lo = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        opCycles++; // Pull PC low byte
        byte hi = cpu.Read8(cpu.PopByte(Cpu65C02Constants.StackBase));
        opCycles++; // Pull PC high byte
        cpu.Registers.PC.SetWord((Word)((hi << 8) | lo));
        cpu.HaltReason = HaltState.None;
        opCycles += 2; // Internal operations

        cpu.Registers.TCU += opCycles;
    }
}