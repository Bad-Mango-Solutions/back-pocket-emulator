// <copyright file="Instructions.Bit.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <content>
/// Rockwell/WDC bit manipulation instructions (RMB, SMB, BBR, BBS).
/// </content>
/// <remarks>
/// These instructions are specific to the Rockwell R65C02 and WDC W65C02S variants.
/// They are present on the Apple IIe Enhanced, IIc, and IIc+.
/// </remarks>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for RMB0 zero page.</summary>
    internal static readonly OpcodeDispatch RMB0ZeroPageDispatch = DispatchRMB0ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB1 zero page.</summary>
    internal static readonly OpcodeDispatch RMB1ZeroPageDispatch = DispatchRMB1ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB2 zero page.</summary>
    internal static readonly OpcodeDispatch RMB2ZeroPageDispatch = DispatchRMB2ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB3 zero page.</summary>
    internal static readonly OpcodeDispatch RMB3ZeroPageDispatch = DispatchRMB3ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB4 zero page.</summary>
    internal static readonly OpcodeDispatch RMB4ZeroPageDispatch = DispatchRMB4ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB5 zero page.</summary>
    internal static readonly OpcodeDispatch RMB5ZeroPageDispatch = DispatchRMB5ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB6 zero page.</summary>
    internal static readonly OpcodeDispatch RMB6ZeroPageDispatch = DispatchRMB6ZeroPage;

    /// <summary>Cached direct-dispatch delegate for RMB7 zero page.</summary>
    internal static readonly OpcodeDispatch RMB7ZeroPageDispatch = DispatchRMB7ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB0 zero page.</summary>
    internal static readonly OpcodeDispatch SMB0ZeroPageDispatch = DispatchSMB0ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB1 zero page.</summary>
    internal static readonly OpcodeDispatch SMB1ZeroPageDispatch = DispatchSMB1ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB2 zero page.</summary>
    internal static readonly OpcodeDispatch SMB2ZeroPageDispatch = DispatchSMB2ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB3 zero page.</summary>
    internal static readonly OpcodeDispatch SMB3ZeroPageDispatch = DispatchSMB3ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB4 zero page.</summary>
    internal static readonly OpcodeDispatch SMB4ZeroPageDispatch = DispatchSMB4ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB5 zero page.</summary>
    internal static readonly OpcodeDispatch SMB5ZeroPageDispatch = DispatchSMB5ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB6 zero page.</summary>
    internal static readonly OpcodeDispatch SMB6ZeroPageDispatch = DispatchSMB6ZeroPage;

    /// <summary>Cached direct-dispatch delegate for SMB7 zero page.</summary>
    internal static readonly OpcodeDispatch SMB7ZeroPageDispatch = DispatchSMB7ZeroPage;

    /// <summary>Cached direct-dispatch delegate for BBR0 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR0ZeroPageRelativeDispatch = DispatchBBR0ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR1 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR1ZeroPageRelativeDispatch = DispatchBBR1ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR2 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR2ZeroPageRelativeDispatch = DispatchBBR2ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR3 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR3ZeroPageRelativeDispatch = DispatchBBR3ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR4 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR4ZeroPageRelativeDispatch = DispatchBBR4ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR5 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR5ZeroPageRelativeDispatch = DispatchBBR5ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR6 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR6ZeroPageRelativeDispatch = DispatchBBR6ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBR7 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBR7ZeroPageRelativeDispatch = DispatchBBR7ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS0 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS0ZeroPageRelativeDispatch = DispatchBBS0ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS1 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS1ZeroPageRelativeDispatch = DispatchBBS1ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS2 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS2ZeroPageRelativeDispatch = DispatchBBS2ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS3 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS3ZeroPageRelativeDispatch = DispatchBBS3ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS4 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS4ZeroPageRelativeDispatch = DispatchBBS4ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS5 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS5ZeroPageRelativeDispatch = DispatchBBS5ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS6 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS6ZeroPageRelativeDispatch = DispatchBBS6ZeroPageRelative;

    /// <summary>Cached direct-dispatch delegate for BBS7 zero page relative.</summary>
    internal static readonly OpcodeDispatch BBS7ZeroPageRelativeDispatch = DispatchBBS7ZeroPageRelative;

    private static void DispatchRMB0ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x01);

    private static void DispatchRMB1ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x02);

    private static void DispatchRMB2ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x04);

    private static void DispatchRMB3ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x08);

    private static void DispatchRMB4ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x10);

    private static void DispatchRMB5ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x20);

    private static void DispatchRMB6ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x40);

    private static void DispatchRMB7ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpRMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x80);

    private static void DispatchSMB0ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x01);

    private static void DispatchSMB1ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x02);

    private static void DispatchSMB2ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x04);

    private static void DispatchSMB3ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x08);

    private static void DispatchSMB4ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x10);

    private static void DispatchSMB5ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x20);

    private static void DispatchSMB6ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x40);

    private static void DispatchSMB7ZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSMB<Cpu65C02, ZeroPageMode>(As65C02(cpu), 0x80);

    private static void DispatchBBR0ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x01);

    private static void DispatchBBR1ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x02);

    private static void DispatchBBR2ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x04);

    private static void DispatchBBR3ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x08);

    private static void DispatchBBR4ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x10);

    private static void DispatchBBR5ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x20);

    private static void DispatchBBR6ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x40);

    private static void DispatchBBR7ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBR<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x80);

    private static void DispatchBBS0ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x01);

    private static void DispatchBBS1ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x02);

    private static void DispatchBBS2ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x04);

    private static void DispatchBBS3ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x08);

    private static void DispatchBBS4ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x10);

    private static void DispatchBBS5ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x20);

    private static void DispatchBBS6ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x40);

    private static void DispatchBBS7ZeroPageRelative(ICpu cpu, in OpcodeEntry entry) => OpBBS<Cpu65C02, ZeroPageRelativeMode>(As65C02(cpu), 0x80);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpRMB<TCpu, TMode>(TCpu cpu, byte mask)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        value &= (byte)~mask;

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSMB<TCpu, TMode>(TCpu cpu, byte mask)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte value = cpu.Read8(address);
        opCycles++; // Memory read

        value |= mask;

        cpu.Write8(address, value);
        opCycles += 2; // Memory write + internal operation

        cpu.Registers.TCU += opCycles;
    }

    // 3-byte instruction. ZeroPageRelative adds 2 cycles. This method adds the
    // memory read and the branch-decision cycle, plus one when the branch is
    // taken and one more when that branch crosses a page. Flags are unchanged.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBBR<TCpu, TMode>(TCpu cpu, byte mask)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;

        // ZeroPageRelative fetches the ZP address and the relative offset,
        // advances PC by 2, adds 2 cycles, and packs both values:
        // low byte = ZP address, next byte = relative offset.
        Addr encoded = TMode.Resolve(cpu);
        byte zpAddress = (byte)(encoded & 0xFF);
        sbyte offset = (sbyte)((encoded >> 8) & 0xFF);

        byte zpValue = cpu.Read8(zpAddress);
        opCycles++; // Memory read

        // Branch decision cycle (always charged, even when the branch is not taken)
        opCycles++;

        bool bitIsReset = (zpValue & mask) == 0;

        if (bitIsReset)
        {
            // Branch taken. Use 16-bit PC math so the target wraps
            // $FFFF -> $0000 like the 65C02 (mirrors Instructions.Branch.cs).
            Word oldPC = cpu.Registers.PC.GetWord();
            Word newPC = (Word)(oldPC + offset);

            // Check for page crossing
            if ((oldPC & 0xFF00) != (newPC & 0xFF00))
            {
                opCycles++; // Page cross penalty
            }

            cpu.Registers.PC.SetWord(newPC);
            opCycles++; // Branch taken cycle
        }

        cpu.Registers.TCU += opCycles;
    }

    // Same cycle rules as BBR. The branch is taken when the tested bit is set.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBBS<TCpu, TMode>(TCpu cpu, byte mask)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;

        // ZeroPageRelative fetches the ZP address and the relative offset,
        // advances PC by 2, adds 2 cycles, and packs both values:
        // low byte = ZP address, next byte = relative offset.
        Addr encoded = TMode.Resolve(cpu);
        byte zpAddress = (byte)(encoded & 0xFF);
        sbyte offset = (sbyte)((encoded >> 8) & 0xFF);

        byte zpValue = cpu.Read8(zpAddress);
        opCycles++; // Memory read

        // Branch decision cycle (always charged, even when the branch is not taken)
        opCycles++;

        bool bitIsSet = (zpValue & mask) != 0;

        if (bitIsSet)
        {
            // Branch taken. Use 16-bit PC math so the target wraps
            // $FFFF -> $0000 like the 65C02 (mirrors Instructions.Branch.cs).
            Word oldPC = cpu.Registers.PC.GetWord();
            Word newPC = (Word)(oldPC + offset);

            // Check for page crossing
            if ((oldPC & 0xFF00) != (newPC & 0xFF00))
            {
                opCycles++; // Page cross penalty
            }

            cpu.Registers.PC.SetWord(newPC);
            opCycles++; // Branch taken cycle
        }

        cpu.Registers.TCU += opCycles;
    }
}