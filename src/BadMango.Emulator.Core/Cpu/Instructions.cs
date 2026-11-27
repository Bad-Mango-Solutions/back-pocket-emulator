// <copyright file="Instructions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

// ReSharper disable InconsistentNaming
namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using Core;
using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>
/// Provides instruction implementations that compose with addressing modes.
/// </summary>
/// <remarks>
/// <para>
/// Each opcode is one <see cref="OpcodeDispatch"/> cached in its own <c>static readonly</c> field.
/// That method casts to <see cref="Cpu65C02"/> and calls <c>Op&lt;TCpu, TMode&gt;</c>
/// (or <c>Op&lt;TCpu&gt;</c> when the instruction has no addressing-mode argument) closed over
/// <see cref="Cpu65C02"/>, so register and bus access see the concrete CPU.
/// A 65816 or 65832 table adds its own dispatch methods on this partial class, calls the same
/// <c>Op</c> method closed over its CPU, and stores that delegate in its own cache.
/// It does not reuse the 65C02 delegates.
/// Addressing modes stay <see cref="IAddressingMode"/> structs, so one operation specializes
/// per mode without allocating a closure.
/// </para>
/// <para>
/// Opcode $42 is <see cref="WdmDispatch"/>. It is not one of the shared NOP delegates.
/// On the 65C02 the handler is a 2-byte, 2-cycle NOP.
/// </para>
/// <para>
/// This partial class is split across multiple files by instruction category:
/// <list type="bullet">
/// <item><description><c>Instructions.cs</c> - Load/Store direct dispatch (LDA, LDX, LDY, STA, STX, STY, STZ), NOP, BRK, and opcode $42</description></item>
/// <item><description><c>Instructions.Flags.cs</c> - Flag manipulation (CLC, SEC, CLI, SEI, CLD, SED, CLV)</description></item>
/// <item><description><c>Instructions.Transfer.cs</c> - Register transfers (TAX, TAY, TXA, TYA, TXS, TSX)</description></item>
/// <item><description><c>Instructions.Stack.cs</c> - Stack direct dispatch (PHA, PHP, PLA, PLP, PHX, PLX, PHY, PLY)</description></item>
/// <item><description><c>Instructions.Jump.cs</c> - Jump direct dispatch (JMP, JSR, RTS, RTI)</description></item>
/// <item><description><c>Instructions.Branch.cs</c> - Branch direct dispatch (BCC, BCS, BEQ, BNE, BMI, BPL, BVC, BVS, BRA)</description></item>
/// <item><description><c>Instructions.Arithmetic.cs</c> - Arithmetic direct dispatch (ADC, SBC, INC, DEC, INX, INY, DEX, DEY, INA, DEA)</description></item>
/// <item><description><c>Instructions.Logical.cs</c> - Logical direct dispatch (AND, ORA, EOR, BIT)</description></item>
/// <item><description><c>Instructions.Shift.cs</c> - Shift direct dispatch (ASL, LSR, ROL, ROR)</description></item>
/// <item><description><c>Instructions.Compare.cs</c> - Comparison direct dispatch (CMP, CPX, CPY)</description></item>
/// <item><description><c>Instructions.65C02.cs</c> - 65C02-specific (TSB, TRB, WAI, STP). STZ lives with the other stores.</description></item>
/// </list>
/// </para>
/// </remarks>
public static partial class Instructions
{
    /// <summary>Cached direct-dispatch delegate for LDA immediate.</summary>
    internal static readonly OpcodeDispatch LDAImmediateDispatch = DispatchLDAImmediate;

    /// <summary>Cached direct-dispatch delegate for LDA zero page.</summary>
    internal static readonly OpcodeDispatch LDAZeroPageDispatch = DispatchLDAZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDA zero page,X.</summary>
    internal static readonly OpcodeDispatch LDAZeroPageXDispatch = DispatchLDAZeroPageX;

    /// <summary>Cached direct-dispatch delegate for LDA absolute.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteDispatch = DispatchLDAAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDA absolute,X.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteXDispatch = DispatchLDAAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for LDA absolute,Y.</summary>
    internal static readonly OpcodeDispatch LDAAbsoluteYDispatch = DispatchLDAAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page,X).</summary>
    internal static readonly OpcodeDispatch LDAIndirectXDispatch = DispatchLDAIndirectX;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page),Y.</summary>
    internal static readonly OpcodeDispatch LDAIndirectYDispatch = DispatchLDAIndirectY;

    /// <summary>Cached direct-dispatch delegate for LDA (zero page).</summary>
    internal static readonly OpcodeDispatch LDAZeroPageIndirectDispatch = DispatchLDAZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for LDX immediate.</summary>
    internal static readonly OpcodeDispatch LDXImmediateDispatch = DispatchLDXImmediate;

    /// <summary>Cached direct-dispatch delegate for LDX zero page.</summary>
    internal static readonly OpcodeDispatch LDXZeroPageDispatch = DispatchLDXZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDX zero page,Y.</summary>
    internal static readonly OpcodeDispatch LDXZeroPageYDispatch = DispatchLDXZeroPageY;

    /// <summary>Cached direct-dispatch delegate for LDX absolute.</summary>
    internal static readonly OpcodeDispatch LDXAbsoluteDispatch = DispatchLDXAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDX absolute,Y.</summary>
    internal static readonly OpcodeDispatch LDXAbsoluteYDispatch = DispatchLDXAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for LDY immediate.</summary>
    internal static readonly OpcodeDispatch LDYImmediateDispatch = DispatchLDYImmediate;

    /// <summary>Cached direct-dispatch delegate for LDY zero page.</summary>
    internal static readonly OpcodeDispatch LDYZeroPageDispatch = DispatchLDYZeroPage;

    /// <summary>Cached direct-dispatch delegate for LDY zero page,X.</summary>
    internal static readonly OpcodeDispatch LDYZeroPageXDispatch = DispatchLDYZeroPageX;

    /// <summary>Cached direct-dispatch delegate for LDY absolute.</summary>
    internal static readonly OpcodeDispatch LDYAbsoluteDispatch = DispatchLDYAbsolute;

    /// <summary>Cached direct-dispatch delegate for LDY absolute,X.</summary>
    internal static readonly OpcodeDispatch LDYAbsoluteXDispatch = DispatchLDYAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for STA zero page.</summary>
    internal static readonly OpcodeDispatch STAZeroPageDispatch = DispatchSTAZeroPage;

    /// <summary>Cached direct-dispatch delegate for STA zero page,X.</summary>
    internal static readonly OpcodeDispatch STAZeroPageXDispatch = DispatchSTAZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STA absolute.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteDispatch = DispatchSTAAbsolute;

    /// <summary>Cached direct-dispatch delegate for STA absolute,X.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteXDispatch = DispatchSTAAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for STA absolute,Y.</summary>
    internal static readonly OpcodeDispatch STAAbsoluteYDispatch = DispatchSTAAbsoluteY;

    /// <summary>Cached direct-dispatch delegate for STA (zero page,X).</summary>
    internal static readonly OpcodeDispatch STAIndirectXDispatch = DispatchSTAIndirectX;

    /// <summary>Cached direct-dispatch delegate for STA (zero page),Y.</summary>
    internal static readonly OpcodeDispatch STAIndirectYDispatch = DispatchSTAIndirectY;

    /// <summary>Cached direct-dispatch delegate for STA (zero page).</summary>
    internal static readonly OpcodeDispatch STAZeroPageIndirectDispatch = DispatchSTAZeroPageIndirect;

    /// <summary>Cached direct-dispatch delegate for STX zero page.</summary>
    internal static readonly OpcodeDispatch STXZeroPageDispatch = DispatchSTXZeroPage;

    /// <summary>Cached direct-dispatch delegate for STX zero page,Y.</summary>
    internal static readonly OpcodeDispatch STXZeroPageYDispatch = DispatchSTXZeroPageY;

    /// <summary>Cached direct-dispatch delegate for STX absolute.</summary>
    internal static readonly OpcodeDispatch STXAbsoluteDispatch = DispatchSTXAbsolute;

    /// <summary>Cached direct-dispatch delegate for STY zero page.</summary>
    internal static readonly OpcodeDispatch STYZeroPageDispatch = DispatchSTYZeroPage;

    /// <summary>Cached direct-dispatch delegate for STY zero page,X.</summary>
    internal static readonly OpcodeDispatch STYZeroPageXDispatch = DispatchSTYZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STY absolute.</summary>
    internal static readonly OpcodeDispatch STYAbsoluteDispatch = DispatchSTYAbsolute;

    /// <summary>Cached direct-dispatch delegate for STZ zero page.</summary>
    internal static readonly OpcodeDispatch STZZeroPageDispatch = DispatchSTZZeroPage;

    /// <summary>Cached direct-dispatch delegate for STZ zero page,X.</summary>
    internal static readonly OpcodeDispatch STZZeroPageXDispatch = DispatchSTZZeroPageX;

    /// <summary>Cached direct-dispatch delegate for STZ absolute.</summary>
    internal static readonly OpcodeDispatch STZAbsoluteDispatch = DispatchSTZAbsolute;

    /// <summary>Cached direct-dispatch delegate for STZ absolute,X.</summary>
    internal static readonly OpcodeDispatch STZAbsoluteXDispatch = DispatchSTZAbsoluteX;

    /// <summary>Cached direct-dispatch delegate for BRK implied.</summary>
    internal static readonly OpcodeDispatch BRKImpliedDispatch = DispatchBRKImplied;

    /// <summary>Cached direct-dispatch delegate for NOP implied ($EA).</summary>
    internal static readonly OpcodeDispatch NOPImpliedDispatch = DispatchNopImplied;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $02.</summary>
    internal static readonly OpcodeDispatch NOP02ImmediateDispatch = DispatchNopImmediate;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $22.</summary>
    internal static readonly OpcodeDispatch NOP22ImmediateDispatch = DispatchNopImmediate;

    /// <summary>
    /// Cached direct-dispatch delegate for opcode $42.
    /// On the 65C02 this is a 2-byte, 2-cycle NOP. It is not a shared unused-NOP delegate:
    /// the 65816 decodes $42 as WDM, and the 65832 plan uses it as an extended-opcode prefix.
    /// </summary>
    internal static readonly OpcodeDispatch WdmDispatch = DispatchWdm;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $62.</summary>
    internal static readonly OpcodeDispatch NOP62ImmediateDispatch = DispatchNopImmediate;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $82.</summary>
    internal static readonly OpcodeDispatch NOP82ImmediateDispatch = DispatchNopImmediate;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $C2.</summary>
    internal static readonly OpcodeDispatch NOPC2ImmediateDispatch = DispatchNopImmediate;

    /// <summary>Cached direct-dispatch delegate for the 2-byte NOP at $E2.</summary>
    internal static readonly OpcodeDispatch NOPE2ImmediateDispatch = DispatchNopImmediate;

    /// <summary>Cached direct-dispatch delegate for the zero-page NOP at $44.</summary>
    internal static readonly OpcodeDispatch NOP44ZeroPageDispatch = DispatchNopZeroPage;

    /// <summary>Cached direct-dispatch delegate for the zero-page,X NOP at $54.</summary>
    internal static readonly OpcodeDispatch NOP54ZeroPageXDispatch = DispatchNopZeroPageX;

    /// <summary>Cached direct-dispatch delegate for the zero-page,X NOP at $D4.</summary>
    internal static readonly OpcodeDispatch NOPD4ZeroPageXDispatch = DispatchNopZeroPageX;

    /// <summary>Cached direct-dispatch delegate for the zero-page,X NOP at $F4.</summary>
    internal static readonly OpcodeDispatch NOPF4ZeroPageXDispatch = DispatchNopZeroPageX;

    /// <summary>
    /// Cached direct-dispatch delegate for unused NOP <c>$5C</c>.
    /// The handler fetches three bytes, reads <c>$FF</c> plus the low operand, then reads <c>$FFFF</c> four times.
    /// </summary>
    internal static readonly OpcodeDispatch NOP5CAbsoluteDispatch = DispatchNop5C;

    /// <summary>Cached direct-dispatch delegate for the absolute NOP at $DC.</summary>
    internal static readonly OpcodeDispatch NOPDCAbsoluteDispatch = DispatchNopAbsolute;

    /// <summary>Cached direct-dispatch delegate for the absolute NOP at $FC.</summary>
    internal static readonly OpcodeDispatch NOPFCAbsoluteDispatch = DispatchNopAbsolute;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $03.</summary>
    internal static readonly OpcodeDispatch NOP03ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $13.</summary>
    internal static readonly OpcodeDispatch NOP13ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $23.</summary>
    internal static readonly OpcodeDispatch NOP23ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $33.</summary>
    internal static readonly OpcodeDispatch NOP33ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $43.</summary>
    internal static readonly OpcodeDispatch NOP43ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $53.</summary>
    internal static readonly OpcodeDispatch NOP53ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $63.</summary>
    internal static readonly OpcodeDispatch NOP63ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $73.</summary>
    internal static readonly OpcodeDispatch NOP73ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $83.</summary>
    internal static readonly OpcodeDispatch NOP83ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $93.</summary>
    internal static readonly OpcodeDispatch NOP93ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $A3.</summary>
    internal static readonly OpcodeDispatch NOPA3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $B3.</summary>
    internal static readonly OpcodeDispatch NOPB3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $C3.</summary>
    internal static readonly OpcodeDispatch NOPC3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $D3.</summary>
    internal static readonly OpcodeDispatch NOPD3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $E3.</summary>
    internal static readonly OpcodeDispatch NOPE3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $F3.</summary>
    internal static readonly OpcodeDispatch NOPF3ImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $0B.</summary>
    internal static readonly OpcodeDispatch NOP0BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $1B.</summary>
    internal static readonly OpcodeDispatch NOP1BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $2B.</summary>
    internal static readonly OpcodeDispatch NOP2BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $3B.</summary>
    internal static readonly OpcodeDispatch NOP3BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $4B.</summary>
    internal static readonly OpcodeDispatch NOP4BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $5B.</summary>
    internal static readonly OpcodeDispatch NOP5BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $6B.</summary>
    internal static readonly OpcodeDispatch NOP6BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $7B.</summary>
    internal static readonly OpcodeDispatch NOP7BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $8B.</summary>
    internal static readonly OpcodeDispatch NOP8BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $9B.</summary>
    internal static readonly OpcodeDispatch NOP9BImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $AB.</summary>
    internal static readonly OpcodeDispatch NOPABImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $BB.</summary>
    internal static readonly OpcodeDispatch NOPBBImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $EB.</summary>
    internal static readonly OpcodeDispatch NOPEBImpliedDispatch = DispatchNopOneByte;

    /// <summary>Cached direct-dispatch delegate for the 1-byte NOP at $FB.</summary>
    internal static readonly OpcodeDispatch NOPFBImpliedDispatch = DispatchNopOneByte;

    /// <summary>
    /// Narrows the dispatch argument to <see cref="Cpu65C02"/>.
    /// </summary>
    /// <param name="cpu">The CPU passed into the cached <see cref="OpcodeDispatch"/>.</param>
    /// <returns>The same instance, typed as <see cref="Cpu65C02"/>.</returns>
    /// <remarks>
    /// Each 65C02 opcode stores one <see cref="OpcodeDispatch"/> that calls <c>Op&lt;Cpu65C02, ...&gt;</c>
    /// through this cast. A 65816 or 65832 dispatch method on this partial class casts to its own
    /// CPU instead, and does not reuse these delegates.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Cpu65C02 As65C02(ICpu cpu) => (Cpu65C02)cpu;

    private static void DispatchBRKImplied(ICpu cpu, in OpcodeEntry entry) => OpBRK<Cpu65C02, ImpliedMode>(As65C02(cpu));

    private static void DispatchNopImplied(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, ImpliedMode>(As65C02(cpu), 1);

    private static void DispatchNopImmediate(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, ImmediateMode>(As65C02(cpu), 1);

    private static void DispatchWdm(ICpu cpu, in OpcodeEntry entry) => OpWdm<Cpu65C02>(As65C02(cpu));

    private static void DispatchNopZeroPage(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, ZeroPageMode>(As65C02(cpu), 1);

    private static void DispatchNopZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, ZeroPageXMode>(As65C02(cpu), 1);

    private static void DispatchNopAbsolute(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, AbsoluteMode>(As65C02(cpu), 1);

    private static void DispatchNop5C(ICpu cpu, in OpcodeEntry entry) => OpNop5C<Cpu65C02>(As65C02(cpu));

    private static void DispatchNopOneByte(ICpu cpu, in OpcodeEntry entry) => OpNop<Cpu65C02, ImpliedMode>(As65C02(cpu), 0);

    private static void DispatchLDAImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchLDAZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchLDAZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchLDAAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchLDAAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchLDAAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchLDAIndirectX(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchLDAIndirectY(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, IndirectYMode>(As65C02(cpu));

    private static void DispatchLDAZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpLDA<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchLDXImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDX<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchLDXZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDX<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchLDXZeroPageY(ICpu cpu, in OpcodeEntry entry) => OpLDX<Cpu65C02, ZeroPageYMode>(As65C02(cpu));

    private static void DispatchLDXAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDX<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchLDXAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpLDX<Cpu65C02, AbsoluteYMode>(As65C02(cpu));

    private static void DispatchLDYImmediate(ICpu cpu, in OpcodeEntry entry) => OpLDY<Cpu65C02, ImmediateMode>(As65C02(cpu));

    private static void DispatchLDYZeroPage(ICpu cpu, in OpcodeEntry entry) => OpLDY<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchLDYZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpLDY<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchLDYAbsolute(ICpu cpu, in OpcodeEntry entry) => OpLDY<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchLDYAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpLDY<Cpu65C02, AbsoluteXMode>(As65C02(cpu));

    private static void DispatchSTAZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchSTAZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchSTAAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchSTAAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, AbsoluteXWriteMode>(As65C02(cpu));

    private static void DispatchSTAAbsoluteY(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, AbsoluteYWriteMode>(As65C02(cpu));

    private static void DispatchSTAIndirectX(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, IndirectXMode>(As65C02(cpu));

    private static void DispatchSTAIndirectY(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, IndirectYWriteMode>(As65C02(cpu));

    private static void DispatchSTAZeroPageIndirect(ICpu cpu, in OpcodeEntry entry) => OpSTA<Cpu65C02, ZeroPageIndirectMode>(As65C02(cpu));

    private static void DispatchSTXZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTX<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchSTXZeroPageY(ICpu cpu, in OpcodeEntry entry) => OpSTX<Cpu65C02, ZeroPageYMode>(As65C02(cpu));

    private static void DispatchSTXAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTX<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchSTYZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTY<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchSTYZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTY<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchSTYAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTY<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchSTZZeroPage(ICpu cpu, in OpcodeEntry entry) => OpSTZ<Cpu65C02, ZeroPageMode>(As65C02(cpu));

    private static void DispatchSTZZeroPageX(ICpu cpu, in OpcodeEntry entry) => OpSTZ<Cpu65C02, ZeroPageXMode>(As65C02(cpu));

    private static void DispatchSTZAbsolute(ICpu cpu, in OpcodeEntry entry) => OpSTZ<Cpu65C02, AbsoluteMode>(As65C02(cpu));

    private static void DispatchSTZAbsoluteX(ICpu cpu, in OpcodeEntry entry) => OpSTZ<Cpu65C02, AbsoluteXWriteMode>(As65C02(cpu));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpLDA<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.A.SetValue(value, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpLDX<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.X.SetValue(value, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpLDY<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        var value = cpu.ReadValue(address, size);
        opCycles++; // Memory read cycle

        if (cpu.Trace.AddressingMode == CpuAddressingModes.Immediate)
        {
            var operands = cpu.Trace.Operands;
            operands[0] = (byte)(value & 0xff);
            cpu.Trace = cpu.Trace with
            {
                OperandSize = (byte)(size / 8),
                Operands = operands,
            };
        }

        cpu.Registers.TCU += opCycles;
        cpu.Registers.P.SetZeroAndNegative(value, size);
        cpu.Registers.Y.SetValue(value, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSTA<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetAccumulatorSize();
        cpu.WriteValue(address, cpu.Registers.A.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSTX<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        cpu.WriteValue(address, cpu.Registers.X.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSTY<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        byte size = cpu.Registers.GetIndexSize();
        cpu.WriteValue(address, cpu.Registers.Y.GetValue(size), size);

        cpu.Registers.TCU += 1; // Memory write cycle
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpSTZ<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        Addr address = TMode.Resolve(cpu);
        cpu.Write8(address, 0x00);

        cpu.Registers.TCU += 1; // Memory write
    }

    /// <summary>
    /// NOP. Flags and registers stay unchanged. <paramref name="instructionCycles"/> is
    /// added on top of the cycles <typeparamref name="TMode"/> already charged.
    /// Zero leaves only those addressing-mode cycles, which is the 1-byte unused NOP.
    /// </summary>
    /// <typeparam name="TCpu">The concrete CPU. The 65C02 table closes this over <see cref="Cpu65C02"/>.</typeparam>
    /// <typeparam name="TMode">The addressing mode that advances PC and charges its own cycles.</typeparam>
    /// <param name="cpu">The CPU instance.</param>
    /// <param name="instructionCycles">
    /// Extra cycles after the addressing mode. <c>$EA</c> uses 1. The 1-byte unused NOPs pass 0.
    /// <c>$5C</c> does not use this method.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpNop<TCpu, TMode>(TCpu cpu, int instructionCycles)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        TMode.Resolve(cpu);

        if (instructionCycles > 0)
        {
            cpu.Registers.TCU += (ulong)instructionCycles;
        }
    }

    /// <summary>
    /// Unused NOP <c>$5C</c>. Fetches the opcode's two operand bytes, reads <c>$FF</c> plus the low operand byte, then reads <c>$FFFF</c> four times.
    /// </summary>
    /// <typeparam name="TCpu">The concrete CPU. The 65C02 table closes this over <see cref="Cpu65C02"/>.</typeparam>
    /// <param name="cpu">The CPU instance.</param>
    /// <remarks>
    /// Table 7-1 of the WDC W65C02S datasheet lists 8 cycles. KimKlone measured <c>5C 34 12</c> as a 3-byte fetch,
    /// one read of <c>$FF34</c>, then four reads of <c>$FFFF</c>
    /// (https://laughtonelectronics.com/Arcana/KimKlone/Kimklone_opcode_mapping.html), cited by
    /// https://github.com/SingleStepTests/65x02/issues/12. The high operand byte is fetched and discarded.
    /// Absolute addressing charges the two operand-fetch cycles. These five reads charge the rest.
    /// The opcode fetch is the eighth cycle. Registers other than PC stay unchanged.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpNop5C<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        Addr operand = AbsoluteMode.Resolve(cpu);
        cpu.Read8((Addr)(0xFF00 | (operand & 0xFF)));
        cpu.Read8(0xFFFF);
        cpu.Read8(0xFFFF);
        cpu.Read8(0xFFFF);
        cpu.Read8(0xFFFF);
        cpu.Registers.TCU += 5;
    }

    /// <summary>
    /// Opcode $42 on the 65C02: a 2-byte, 2-cycle NOP.
    /// Immediate addressing consumes the operand. The extra cycle is this instruction's
    /// cycle, on top of the opcode fetch performed by the CPU. This method is not a
    /// shared NOP so a 65816 or 65832 table can replace <see cref="WdmDispatch"/> alone.
    /// </summary>
    /// <typeparam name="TCpu">The concrete CPU. The 65C02 table closes this over <see cref="Cpu65C02"/>.</typeparam>
    /// <param name="cpu">The CPU instance.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpWdm<TCpu>(TCpu cpu)
        where TCpu : class, ICpu
    {
        ImmediateMode.Resolve(cpu);
        cpu.Registers.TCU += 1;
    }

    /// <summary>
    /// BRK. Pushes PC+1 and status with B and the reserved bit set, sets I, clears D, and loads the IRQ vector.
    /// </summary>
    /// <typeparam name="TCpu">The concrete CPU. The 65C02 table closes this over <see cref="Cpu65C02"/>.</typeparam>
    /// <typeparam name="TMode">The addressing mode. The 65C02 form is implied.</typeparam>
    /// <param name="cpu">The CPU instance.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void OpBRK<TCpu, TMode>(TCpu cpu)
        where TCpu : class, ICpu
        where TMode : struct, IAddressingMode
    {
        byte opCycles = 0;
        TMode.Resolve(cpu);

        // BRK causes a software interrupt.
        // Total 7 cycles: 1 (opcode fetch) + 1 (PC increment) + 2 (push PC) + 1 (push P) + 2 (read IRQ vector).
        cpu.Registers.PC.Advance();
        opCycles++; // PC increment cycle

        Word pc = cpu.Registers.PC.GetWord();

        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), pc.HighByte());
        opCycles++; // Push PC high byte
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), pc.LowByte());
        opCycles++; // Push PC low byte
        cpu.Write8(cpu.PushByte(Cpu65C02Constants.StackBase), (byte)(cpu.Registers.P | ProcessorStatusFlags.B | ProcessorStatusFlags.R));
        opCycles++; // Push P

        cpu.Registers.P |= ProcessorStatusFlags.I;

        // 65C02: BRK clears the D flag after pushing status (NMOS 6502 does not).
        cpu.Registers.P &= ~ProcessorStatusFlags.D;
        cpu.Registers.PC.SetWord(cpu.Read16(Cpu65C02Constants.IrqVector));
        opCycles += 2; // Read IRQ vector (2 bytes)

        cpu.Registers.TCU += opCycles;
    }
}