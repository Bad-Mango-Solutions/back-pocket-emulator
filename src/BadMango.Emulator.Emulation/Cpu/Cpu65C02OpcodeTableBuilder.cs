// <copyright file="Cpu65C02OpcodeTableBuilder.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>
/// Builds the opcode table for the 65C02 CPU using compositional pattern.
/// </summary>
/// <remarks>
/// This builder uses true composition where addressing modes return addresses
/// and instructions are higher-order functions that accept addressing mode delegates.
/// This pattern eliminates duplication and makes it easy to add new instructions
/// and addressing modes without creating combinatorial explosion of methods.
/// </remarks>
public static class Cpu65C02OpcodeTableBuilder
{
    // W65C02S base cycles, indexed by opcode. Penalties are excluded: page-cross (+1),
    // conditional branch taken (+1, plus another +1 when that branch crosses a page),
    // and decimal-mode ADC/SBC (+1). BRA is always taken, so its minimum is 3.
    //
    // Table 4-1 of the WDC W65C02S datasheet supplies the addressing-mode times.
    // Note 1 adds the fixed extra cycle for indexed stores (STA abs,X / abs,Y,
    // STA (zp),Y, and STZ abs,X). Note 3 adds 2 for read-modify-write.
    // For absolute indexed RMW, real hardware keeps DEC/INC abs,X at 7 cycles
    // regardless of page crossing (https://www.6502.org/tutorials/65c02opcodes.html),
    // while ASL/LSR/ROL/ROR abs,X remain 6 plus page-cross penalty.
    // Table 7-1 also lists the unused-opcode NOPs (including $5C = 8 and the
    // 1-byte, 1-cycle column 3 and column B NOPs). JMP absolute is 3 because it
    // does not read a data operand. JMP indirect and JMP (abs,X) are 6.
    // Stack times within Table 4-1's 3-7 range: pushes 3, pulls 4, JSR/RTS/RTI 6, BRK 7.
    // WAI and STP are 3 (they are not the ordinary 2-cycle implied operations).
    private static readonly byte[] BaseCycles =
    {
        7, 6, 2, 1, 5, 3, 5, 5, 3, 2, 2, 1, 6, 4, 6, 5, // $00
        2, 5, 5, 1, 5, 4, 6, 5, 2, 4, 2, 1, 6, 4, 6, 5, // $10
        6, 6, 2, 1, 3, 3, 5, 5, 4, 2, 2, 1, 4, 4, 6, 5, // $20
        2, 5, 5, 1, 4, 4, 6, 5, 2, 4, 2, 1, 4, 4, 6, 5, // $30
        6, 6, 2, 1, 3, 3, 5, 5, 3, 2, 2, 1, 3, 4, 6, 5, // $40
        2, 5, 5, 1, 4, 4, 6, 5, 2, 4, 3, 1, 8, 4, 6, 5, // $50
        6, 6, 2, 1, 3, 3, 5, 5, 4, 2, 2, 1, 6, 4, 6, 5, // $60
        2, 5, 5, 1, 4, 4, 6, 5, 2, 4, 4, 1, 6, 4, 6, 5, // $70
        3, 6, 2, 1, 3, 3, 3, 5, 2, 2, 2, 1, 4, 4, 4, 5, // $80
        2, 6, 5, 1, 4, 4, 4, 5, 2, 5, 2, 1, 4, 5, 5, 5, // $90
        2, 6, 2, 1, 3, 3, 3, 5, 2, 2, 2, 1, 4, 4, 4, 5, // $A0
        2, 5, 5, 1, 4, 4, 4, 5, 2, 4, 2, 1, 4, 4, 4, 5, // $B0
        2, 6, 2, 1, 3, 3, 5, 5, 2, 2, 2, 3, 4, 4, 6, 5, // $C0
        2, 5, 5, 1, 4, 4, 6, 5, 2, 4, 3, 3, 4, 4, 7, 5, // $D0
        2, 6, 2, 1, 3, 3, 5, 5, 2, 2, 2, 1, 4, 4, 6, 5, // $E0
        2, 5, 5, 1, 4, 4, 6, 5, 2, 4, 4, 1, 4, 4, 7, 5, // $F0
    };

    /// <summary>
    /// Builds the opcode table for the 65C02 CPU.
    /// </summary>
    /// <returns>An <see cref="OpcodeTable"/> configured for the 65C02 CPU.</returns>
    public static OpcodeTable Build()
    {
        var handlers = new OpcodeHandler?[256];
        var dispatchTargets = new OpcodeDispatch?[256];
        var metadata = new OpcodeInfo[256];

        // Initialize all opcodes to illegal opcode handler
        for (int i = 0; i < 256; i++)
        {
            handlers[i] = IllegalOpcode;
        }

        void SetOpcode(byte opcode, OpcodeHandler handler, CpuInstructions instruction, CpuAddressingModes addressingMode)
        {
            handlers[opcode] = handler;
            dispatchTargets[opcode] = null;
            metadata[opcode] = new(instruction, addressingMode, GetOperandLength(addressingMode), BaseCycles[opcode]);
        }

        void SetDirectOpcode(byte opcode, OpcodeDispatch dispatchTarget, CpuInstructions instruction, CpuAddressingModes addressingMode)
        {
            handlers[opcode] = null;
            dispatchTargets[opcode] = dispatchTarget;
            metadata[opcode] = new(instruction, addressingMode, GetOperandLength(addressingMode), BaseCycles[opcode]);
        }

        static byte GetOperandLength(CpuAddressingModes addressingMode) => addressingMode switch
        {
            CpuAddressingModes.Implied => 0,
            CpuAddressingModes.Accumulator => 0,
            CpuAddressingModes.Immediate => 1,
            CpuAddressingModes.ZeroPage => 1,
            CpuAddressingModes.ZeroPageX => 1,
            CpuAddressingModes.ZeroPageY => 1,
            CpuAddressingModes.Relative => 1,
            CpuAddressingModes.IndirectX => 1,
            CpuAddressingModes.IndirectY => 1,
            CpuAddressingModes.ZeroPageIndirect => 1,
            CpuAddressingModes.Absolute => 2,
            CpuAddressingModes.AbsoluteX => 2,
            CpuAddressingModes.AbsoluteY => 2,
            CpuAddressingModes.Indirect => 2,
            CpuAddressingModes.AbsoluteIndirectX => 2,
            CpuAddressingModes.ZeroPageRelative => 2,
            _ => 0,
        };

        // BRK - Force Break
        SetOpcode(0x00, Instructions.BRK(AddressingModes.Implied), CpuInstructions.BRK, CpuAddressingModes.Implied);

        // LDA - Load Accumulator
        SetDirectOpcode(0xA9, Instructions.LDAImmediateDispatch, CpuInstructions.LDA, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xA5, Instructions.LDAZeroPageDispatch, CpuInstructions.LDA, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xB5, Instructions.LDAZeroPageXDispatch, CpuInstructions.LDA, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xAD, Instructions.LDAAbsoluteDispatch, CpuInstructions.LDA, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xBD, Instructions.LDAAbsoluteXDispatch, CpuInstructions.LDA, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0xB9, Instructions.LDAAbsoluteYDispatch, CpuInstructions.LDA, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0xA1, Instructions.LDAIndirectXDispatch, CpuInstructions.LDA, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0xB1, Instructions.LDAIndirectYDispatch, CpuInstructions.LDA, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0xB2, Instructions.LDAZeroPageIndirectDispatch, CpuInstructions.LDA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        // STA - Store Accumulator. Indexed and (zp),Y stores use the write-cycle modes.
        SetDirectOpcode(0x85, Instructions.STAZeroPageDispatch, CpuInstructions.STA, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x95, Instructions.STAZeroPageXDispatch, CpuInstructions.STA, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x8D, Instructions.STAAbsoluteDispatch, CpuInstructions.STA, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x9D, Instructions.STAAbsoluteXDispatch, CpuInstructions.STA, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x99, Instructions.STAAbsoluteYDispatch, CpuInstructions.STA, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0x81, Instructions.STAIndirectXDispatch, CpuInstructions.STA, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0x91, Instructions.STAIndirectYDispatch, CpuInstructions.STA, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0x92, Instructions.STAZeroPageIndirectDispatch, CpuInstructions.STA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        // LDX - Load X Register
        SetDirectOpcode(0xA2, Instructions.LDXImmediateDispatch, CpuInstructions.LDX, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xA6, Instructions.LDXZeroPageDispatch, CpuInstructions.LDX, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xB6, Instructions.LDXZeroPageYDispatch, CpuInstructions.LDX, CpuAddressingModes.ZeroPageY);
        SetDirectOpcode(0xAE, Instructions.LDXAbsoluteDispatch, CpuInstructions.LDX, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xBE, Instructions.LDXAbsoluteYDispatch, CpuInstructions.LDX, CpuAddressingModes.AbsoluteY);

        // LDY - Load Y Register
        SetDirectOpcode(0xA0, Instructions.LDYImmediateDispatch, CpuInstructions.LDY, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xA4, Instructions.LDYZeroPageDispatch, CpuInstructions.LDY, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xB4, Instructions.LDYZeroPageXDispatch, CpuInstructions.LDY, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xAC, Instructions.LDYAbsoluteDispatch, CpuInstructions.LDY, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xBC, Instructions.LDYAbsoluteXDispatch, CpuInstructions.LDY, CpuAddressingModes.AbsoluteX);

        // NOP - No Operation
        SetOpcode(0xEA, Instructions.NOP(AddressingModes.Implied), CpuInstructions.NOP, CpuAddressingModes.Implied);

        // WDC W65C02S "unused" opcode NOP slots (see "65C02 Apple II Emulator
        // Correctness Checklist.md" section 6 and the WDC W65C02S datasheet).
        // All of these preserve flags and registers. Software (notably ProDOS
        // 2.4.3's boot1 IIgs-detection idiom at $102C: `C2 02`) relies on these
        // being silent NOPs on a 65C02 (whereas on a 65816 they decode as
        // REP/SEP/PEI/MVN/JML/etc. and have observable side effects).
        // The disassembler renders these as `NOP <operand>` via the standard
        // Instructions.NOP + AddressingModes.* composition.

        // 2 bytes, 2 cycles: immediate-mode NOPs.
        SetOpcode(0x02, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0x22, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0x42, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0x62, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0x82, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0xC2, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);
        SetOpcode(0xE2, Instructions.NOP(AddressingModes.Immediate), CpuInstructions.NOP, CpuAddressingModes.Immediate);

        // 2 bytes, 3 cycles: zero-page NOP.
        // (ZeroPage adds 1 cycle for the operand fetch; NOP adds 1; +1 from
        //  base opcode fetch = 3 cycles total.)
        SetOpcode(0x44, Instructions.NOP(AddressingModes.ZeroPage), CpuInstructions.NOP, CpuAddressingModes.ZeroPage);

        // 2 bytes, 4 cycles: zero-page,X NOPs.
        // (ZeroPageX adds 2 cycles (fetch + indexing); NOP adds 1; +1 from
        //  base opcode fetch = 4 cycles total.)
        SetOpcode(0x54, Instructions.NOP(AddressingModes.ZeroPageX), CpuInstructions.NOP, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xD4, Instructions.NOP(AddressingModes.ZeroPageX), CpuInstructions.NOP, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xF4, Instructions.NOP(AddressingModes.ZeroPageX), CpuInstructions.NOP, CpuAddressingModes.ZeroPageX);

        // 3 bytes, 4 cycles: absolute NOPs.
        // (Absolute adds 2 cycles for the 16-bit operand fetch; NOP adds 1;
        //  +1 from base opcode fetch = 4 cycles total.)
        SetOpcode(0xDC, Instructions.NOP(AddressingModes.Absolute), CpuInstructions.NOP, CpuAddressingModes.Absolute);
        SetOpcode(0xFC, Instructions.NOP(AddressingModes.Absolute), CpuInstructions.NOP, CpuAddressingModes.Absolute);

        // 3 bytes, 8 cycles: "loud" absolute NOP. Per the WDC datasheet this is
        // the only unused-slot NOP that consumes 8 cycles (versus 4 for the
        // other 3-byte absolute NOPs). Modelled with extra instruction cycles
        // so the PC advances by 3 and the bus arbiter sees the correct cycle
        // pressure; the actual bus read at the formed address is not performed
        // since no shipping 65C02 software in scope for this emulator relies on
        // the side effect.
        SetOpcode(0x5C, Instructions.NOP(AddressingModes.Absolute, instructionCycles: 5), CpuInstructions.NOP, CpuAddressingModes.Absolute);

        // 1 byte, 1 cycle: column-3 single-cycle NOPs. These are the slots
        // reserved by WDC for future expansion; on the 65C02 they fully decode
        // in a single cycle with no side effects (no operand fetch, no PC
        // advance beyond the opcode byte itself).
        SetOpcode(0x03, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x13, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x23, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x33, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x43, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x53, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x63, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x73, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x83, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x93, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xA3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xB3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xC3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xD3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xE3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xF3, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);

        // 1 byte, 1 cycle: column-B single-cycle NOPs. Additional reserved slots
        // per WDC datasheet; same behavior as column-3 (no operand, no side effects).
        SetOpcode(0x0B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x1B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x2B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x3B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x4B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x5B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x6B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x7B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x8B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0x9B, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xAB, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xBB, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xEB, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);
        SetOpcode(0xFB, Instructions.NOP(AddressingModes.Implied, instructionCycles: 0), CpuInstructions.NOP, CpuAddressingModes.Implied);

        // STX - Store X Register
        SetDirectOpcode(0x86, Instructions.STXZeroPageDispatch, CpuInstructions.STX, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x96, Instructions.STXZeroPageYDispatch, CpuInstructions.STX, CpuAddressingModes.ZeroPageY);
        SetDirectOpcode(0x8E, Instructions.STXAbsoluteDispatch, CpuInstructions.STX, CpuAddressingModes.Absolute);

        // STY - Store Y Register
        SetDirectOpcode(0x84, Instructions.STYZeroPageDispatch, CpuInstructions.STY, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x94, Instructions.STYZeroPageXDispatch, CpuInstructions.STY, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x8C, Instructions.STYAbsoluteDispatch, CpuInstructions.STY, CpuAddressingModes.Absolute);

        // Flag manipulation instructions (all use Implied addressing)
        SetDirectOpcode(0x18, Instructions.CLCImpliedDispatch, CpuInstructions.CLC, CpuAddressingModes.Implied); // Clear Carry
        SetDirectOpcode(0x38, Instructions.SECImpliedDispatch, CpuInstructions.SEC, CpuAddressingModes.Implied); // Set Carry
        SetDirectOpcode(0x58, Instructions.CLIImpliedDispatch, CpuInstructions.CLI, CpuAddressingModes.Implied); // Clear Interrupt Disable
        SetDirectOpcode(0x78, Instructions.SEIImpliedDispatch, CpuInstructions.SEI, CpuAddressingModes.Implied); // Set Interrupt Disable
        SetDirectOpcode(0xD8, Instructions.CLDImpliedDispatch, CpuInstructions.CLD, CpuAddressingModes.Implied); // Clear Decimal
        SetDirectOpcode(0xF8, Instructions.SEDImpliedDispatch, CpuInstructions.SED, CpuAddressingModes.Implied); // Set Decimal
        SetDirectOpcode(0xB8, Instructions.CLVImpliedDispatch, CpuInstructions.CLV, CpuAddressingModes.Implied); // Clear Overflow

        // Register Transfer Operations
        SetDirectOpcode(0xAA, Instructions.TAXImpliedDispatch, CpuInstructions.TAX, CpuAddressingModes.Implied); // Transfer RegisterAccumulator to X
        SetDirectOpcode(0xA8, Instructions.TAYImpliedDispatch, CpuInstructions.TAY, CpuAddressingModes.Implied); // Transfer RegisterAccumulator to Y
        SetDirectOpcode(0x8A, Instructions.TXAImpliedDispatch, CpuInstructions.TXA, CpuAddressingModes.Implied); // Transfer X to RegisterAccumulator
        SetDirectOpcode(0x98, Instructions.TYAImpliedDispatch, CpuInstructions.TYA, CpuAddressingModes.Implied); // Transfer Y to RegisterAccumulator
        SetDirectOpcode(0x9A, Instructions.TXSImpliedDispatch, CpuInstructions.TXS, CpuAddressingModes.Implied); // Transfer X to SP
        SetDirectOpcode(0xBA, Instructions.TSXImpliedDispatch, CpuInstructions.TSX, CpuAddressingModes.Implied); // Transfer SP to X

        // Stack Operations
        SetOpcode(0x48, Instructions.PHA(AddressingModes.Implied), CpuInstructions.PHA, CpuAddressingModes.Implied); // Push Accumulator
        SetOpcode(0x08, Instructions.PHP(AddressingModes.Implied), CpuInstructions.PHP, CpuAddressingModes.Implied); // Push Processor Status
        SetOpcode(0x68, Instructions.PLA(AddressingModes.Implied), CpuInstructions.PLA, CpuAddressingModes.Implied); // Pull Accumulator
        SetOpcode(0x28, Instructions.PLP(AddressingModes.Implied), CpuInstructions.PLP, CpuAddressingModes.Implied); // Pull Processor Status
        SetOpcode(0xDA, Instructions.PHX(AddressingModes.Implied), CpuInstructions.PHX, CpuAddressingModes.Implied); // Push X (65C02)
        SetOpcode(0xFA, Instructions.PLX(AddressingModes.Implied), CpuInstructions.PLX, CpuAddressingModes.Implied); // Pull X (65C02)
        SetOpcode(0x5A, Instructions.PHY(AddressingModes.Implied), CpuInstructions.PHY, CpuAddressingModes.Implied); // Push Y (65C02)
        SetOpcode(0x7A, Instructions.PLY(AddressingModes.Implied), CpuInstructions.PLY, CpuAddressingModes.Implied); // Pull Y (65C02)

        // 65C02-Specific Instructions
        SetDirectOpcode(0x64, Instructions.STZZeroPageDispatch, CpuInstructions.STZ, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x74, Instructions.STZZeroPageXDispatch, CpuInstructions.STZ, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x9C, Instructions.STZAbsoluteDispatch, CpuInstructions.STZ, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x9E, Instructions.STZAbsoluteXDispatch, CpuInstructions.STZ, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x04, Instructions.TSB(AddressingModes.ZeroPage), CpuInstructions.TSB, CpuAddressingModes.ZeroPage); // Test and Set Bits
        SetOpcode(0x0C, Instructions.TSB(AddressingModes.Absolute), CpuInstructions.TSB, CpuAddressingModes.Absolute); // Test and Set Bits
        SetOpcode(0x14, Instructions.TRB(AddressingModes.ZeroPage), CpuInstructions.TRB, CpuAddressingModes.ZeroPage); // Test and Reset Bits
        SetOpcode(0x1C, Instructions.TRB(AddressingModes.Absolute), CpuInstructions.TRB, CpuAddressingModes.Absolute); // Test and Reset Bits
        SetOpcode(0xCB, Instructions.WAI(AddressingModes.Implied), CpuInstructions.WAI, CpuAddressingModes.Implied); // Wait for Interrupt
        SetOpcode(0xDB, Instructions.STP(AddressingModes.Implied), CpuInstructions.STP, CpuAddressingModes.Implied); // Stop Processor

        // Rockwell/WDC Bit Manipulation Instructions (RMB, SMB, BBR, BBS)
        // Reset Memory Bit (RMB0-RMB7)
        SetOpcode(0x07, Instructions.RMB(AddressingModes.ZeroPage, 0), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x17, Instructions.RMB(AddressingModes.ZeroPage, 1), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x27, Instructions.RMB(AddressingModes.ZeroPage, 2), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x37, Instructions.RMB(AddressingModes.ZeroPage, 3), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x47, Instructions.RMB(AddressingModes.ZeroPage, 4), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x57, Instructions.RMB(AddressingModes.ZeroPage, 5), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x67, Instructions.RMB(AddressingModes.ZeroPage, 6), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x77, Instructions.RMB(AddressingModes.ZeroPage, 7), CpuInstructions.RMB, CpuAddressingModes.ZeroPage);

        // Set Memory Bit (SMB0-SMB7)
        SetOpcode(0x87, Instructions.SMB(AddressingModes.ZeroPage, 0), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0x97, Instructions.SMB(AddressingModes.ZeroPage, 1), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xA7, Instructions.SMB(AddressingModes.ZeroPage, 2), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xB7, Instructions.SMB(AddressingModes.ZeroPage, 3), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xC7, Instructions.SMB(AddressingModes.ZeroPage, 4), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xD7, Instructions.SMB(AddressingModes.ZeroPage, 5), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xE7, Instructions.SMB(AddressingModes.ZeroPage, 6), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);
        SetOpcode(0xF7, Instructions.SMB(AddressingModes.ZeroPage, 7), CpuInstructions.SMB, CpuAddressingModes.ZeroPage);

        // Branch if Bit Reset (BBR0-BBR7)
        SetOpcode(0x0F, Instructions.BBR(AddressingModes.ZeroPageRelative, 0), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x1F, Instructions.BBR(AddressingModes.ZeroPageRelative, 1), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x2F, Instructions.BBR(AddressingModes.ZeroPageRelative, 2), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x3F, Instructions.BBR(AddressingModes.ZeroPageRelative, 3), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x4F, Instructions.BBR(AddressingModes.ZeroPageRelative, 4), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x5F, Instructions.BBR(AddressingModes.ZeroPageRelative, 5), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x6F, Instructions.BBR(AddressingModes.ZeroPageRelative, 6), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x7F, Instructions.BBR(AddressingModes.ZeroPageRelative, 7), CpuInstructions.BBR, CpuAddressingModes.ZeroPageRelative);

        // Branch if Bit Set (BBS0-BBS7)
        SetOpcode(0x8F, Instructions.BBS(AddressingModes.ZeroPageRelative, 0), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0x9F, Instructions.BBS(AddressingModes.ZeroPageRelative, 1), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xAF, Instructions.BBS(AddressingModes.ZeroPageRelative, 2), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xBF, Instructions.BBS(AddressingModes.ZeroPageRelative, 3), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xCF, Instructions.BBS(AddressingModes.ZeroPageRelative, 4), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xDF, Instructions.BBS(AddressingModes.ZeroPageRelative, 5), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xEF, Instructions.BBS(AddressingModes.ZeroPageRelative, 6), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);
        SetOpcode(0xFF, Instructions.BBS(AddressingModes.ZeroPageRelative, 7), CpuInstructions.BBS, CpuAddressingModes.ZeroPageRelative);

        // Jump and Subroutine Operations
        SetOpcode(0x4C, Instructions.JMP(AddressingModes.Absolute), CpuInstructions.JMP, CpuAddressingModes.Absolute); // Jump Absolute
        SetOpcode(0x6C, Instructions.JMP(AddressingModes.Indirect), CpuInstructions.JMP, CpuAddressingModes.Indirect); // Jump Indirect
        SetOpcode(0x7C, Instructions.JMP(AddressingModes.AbsoluteIndirectX), CpuInstructions.JMP, CpuAddressingModes.AbsoluteIndirectX); // Jump Indirect X (65C02)
        SetOpcode(0x20, Instructions.JSR(AddressingModes.Absolute), CpuInstructions.JSR, CpuAddressingModes.Absolute); // Jump to Subroutine
        SetOpcode(0x60, Instructions.RTS(AddressingModes.Implied), CpuInstructions.RTS, CpuAddressingModes.Implied); // Return from Subroutine
        SetOpcode(0x40, Instructions.RTI(AddressingModes.Implied), CpuInstructions.RTI, CpuAddressingModes.Implied); // Return from Interrupt

        // Comparison Operations
        SetDirectOpcode(0xC9, Instructions.CMPImmediateDispatch, CpuInstructions.CMP, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xC5, Instructions.CMPZeroPageDispatch, CpuInstructions.CMP, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xD5, Instructions.CMPZeroPageXDispatch, CpuInstructions.CMP, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xCD, Instructions.CMPAbsoluteDispatch, CpuInstructions.CMP, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xDD, Instructions.CMPAbsoluteXDispatch, CpuInstructions.CMP, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0xD9, Instructions.CMPAbsoluteYDispatch, CpuInstructions.CMP, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0xC1, Instructions.CMPIndirectXDispatch, CpuInstructions.CMP, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0xD1, Instructions.CMPIndirectYDispatch, CpuInstructions.CMP, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0xD2, Instructions.CMPZeroPageIndirectDispatch, CpuInstructions.CMP, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0xE0, Instructions.CPXImmediateDispatch, CpuInstructions.CPX, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xE4, Instructions.CPXZeroPageDispatch, CpuInstructions.CPX, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xEC, Instructions.CPXAbsoluteDispatch, CpuInstructions.CPX, CpuAddressingModes.Absolute);

        SetDirectOpcode(0xC0, Instructions.CPYImmediateDispatch, CpuInstructions.CPY, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xC4, Instructions.CPYZeroPageDispatch, CpuInstructions.CPY, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xCC, Instructions.CPYAbsoluteDispatch, CpuInstructions.CPY, CpuAddressingModes.Absolute);

        // Branch Operations
        SetOpcode(0x90, Instructions.BCC(AddressingModes.Relative), CpuInstructions.BCC, CpuAddressingModes.Relative); // Branch if Carry Clear
        SetOpcode(0xB0, Instructions.BCS(AddressingModes.Relative), CpuInstructions.BCS, CpuAddressingModes.Relative); // Branch if Carry Set
        SetOpcode(0xF0, Instructions.BEQ(AddressingModes.Relative), CpuInstructions.BEQ, CpuAddressingModes.Relative); // Branch if Equal
        SetOpcode(0xD0, Instructions.BNE(AddressingModes.Relative), CpuInstructions.BNE, CpuAddressingModes.Relative); // Branch if Not Equal
        SetOpcode(0x30, Instructions.BMI(AddressingModes.Relative), CpuInstructions.BMI, CpuAddressingModes.Relative); // Branch if Minus
        SetOpcode(0x10, Instructions.BPL(AddressingModes.Relative), CpuInstructions.BPL, CpuAddressingModes.Relative); // Branch if Plus
        SetOpcode(0x50, Instructions.BVC(AddressingModes.Relative), CpuInstructions.BVC, CpuAddressingModes.Relative); // Branch if Overflow Clear
        SetOpcode(0x70, Instructions.BVS(AddressingModes.Relative), CpuInstructions.BVS, CpuAddressingModes.Relative); // Branch if Overflow Set
        SetOpcode(0x80, Instructions.BRA(AddressingModes.Relative), CpuInstructions.BRA, CpuAddressingModes.Relative); // Branch Always (65C02)

        // Arithmetic Operations
        SetDirectOpcode(0x69, Instructions.ADCImmediateDispatch, CpuInstructions.ADC, CpuAddressingModes.Immediate);
        SetDirectOpcode(0x65, Instructions.ADCZeroPageDispatch, CpuInstructions.ADC, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x75, Instructions.ADCZeroPageXDispatch, CpuInstructions.ADC, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x6D, Instructions.ADCAbsoluteDispatch, CpuInstructions.ADC, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x7D, Instructions.ADCAbsoluteXDispatch, CpuInstructions.ADC, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x79, Instructions.ADCAbsoluteYDispatch, CpuInstructions.ADC, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0x61, Instructions.ADCIndirectXDispatch, CpuInstructions.ADC, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0x71, Instructions.ADCIndirectYDispatch, CpuInstructions.ADC, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0x72, Instructions.ADCZeroPageIndirectDispatch, CpuInstructions.ADC, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0xE9, Instructions.SBCImmediateDispatch, CpuInstructions.SBC, CpuAddressingModes.Immediate);
        SetDirectOpcode(0xE5, Instructions.SBCZeroPageDispatch, CpuInstructions.SBC, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xF5, Instructions.SBCZeroPageXDispatch, CpuInstructions.SBC, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xED, Instructions.SBCAbsoluteDispatch, CpuInstructions.SBC, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xFD, Instructions.SBCAbsoluteXDispatch, CpuInstructions.SBC, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0xF9, Instructions.SBCAbsoluteYDispatch, CpuInstructions.SBC, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0xE1, Instructions.SBCIndirectXDispatch, CpuInstructions.SBC, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0xF1, Instructions.SBCIndirectYDispatch, CpuInstructions.SBC, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0xF2, Instructions.SBCZeroPageIndirectDispatch, CpuInstructions.SBC, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0xE6, Instructions.INCZeroPageDispatch, CpuInstructions.INC, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xF6, Instructions.INCZeroPageXDispatch, CpuInstructions.INC, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xEE, Instructions.INCAbsoluteDispatch, CpuInstructions.INC, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xFE, Instructions.INCAbsoluteXDispatch, CpuInstructions.INC, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x1A, Instructions.INAAccumulatorDispatch, CpuInstructions.INA, CpuAddressingModes.Accumulator); // 65C02 INC A

        SetDirectOpcode(0xC6, Instructions.DECZeroPageDispatch, CpuInstructions.DEC, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0xD6, Instructions.DECZeroPageXDispatch, CpuInstructions.DEC, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0xCE, Instructions.DECAbsoluteDispatch, CpuInstructions.DEC, CpuAddressingModes.Absolute);
        SetDirectOpcode(0xDE, Instructions.DECAbsoluteXDispatch, CpuInstructions.DEC, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x3A, Instructions.DEAAccumulatorDispatch, CpuInstructions.DEA, CpuAddressingModes.Accumulator); // 65C02 DEC A

        SetDirectOpcode(0xE8, Instructions.INXImpliedDispatch, CpuInstructions.INX, CpuAddressingModes.Implied);
        SetDirectOpcode(0xC8, Instructions.INYImpliedDispatch, CpuInstructions.INY, CpuAddressingModes.Implied);
        SetDirectOpcode(0xCA, Instructions.DEXImpliedDispatch, CpuInstructions.DEX, CpuAddressingModes.Implied);
        SetDirectOpcode(0x88, Instructions.DEYImpliedDispatch, CpuInstructions.DEY, CpuAddressingModes.Implied);

        // Logical Operations
        SetDirectOpcode(0x29, Instructions.ANDImmediateDispatch, CpuInstructions.AND, CpuAddressingModes.Immediate);
        SetDirectOpcode(0x25, Instructions.ANDZeroPageDispatch, CpuInstructions.AND, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x35, Instructions.ANDZeroPageXDispatch, CpuInstructions.AND, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x2D, Instructions.ANDAbsoluteDispatch, CpuInstructions.AND, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x3D, Instructions.ANDAbsoluteXDispatch, CpuInstructions.AND, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x39, Instructions.ANDAbsoluteYDispatch, CpuInstructions.AND, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0x21, Instructions.ANDIndirectXDispatch, CpuInstructions.AND, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0x31, Instructions.ANDIndirectYDispatch, CpuInstructions.AND, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0x32, Instructions.ANDZeroPageIndirectDispatch, CpuInstructions.AND, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0x09, Instructions.ORAImmediateDispatch, CpuInstructions.ORA, CpuAddressingModes.Immediate);
        SetDirectOpcode(0x05, Instructions.ORAZeroPageDispatch, CpuInstructions.ORA, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x15, Instructions.ORAZeroPageXDispatch, CpuInstructions.ORA, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x0D, Instructions.ORAAbsoluteDispatch, CpuInstructions.ORA, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x1D, Instructions.ORAAbsoluteXDispatch, CpuInstructions.ORA, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x19, Instructions.ORAAbsoluteYDispatch, CpuInstructions.ORA, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0x01, Instructions.ORAIndirectXDispatch, CpuInstructions.ORA, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0x11, Instructions.ORAIndirectYDispatch, CpuInstructions.ORA, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0x12, Instructions.ORAZeroPageIndirectDispatch, CpuInstructions.ORA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0x49, Instructions.EORImmediateDispatch, CpuInstructions.EOR, CpuAddressingModes.Immediate);
        SetDirectOpcode(0x45, Instructions.EORZeroPageDispatch, CpuInstructions.EOR, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x55, Instructions.EORZeroPageXDispatch, CpuInstructions.EOR, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x4D, Instructions.EORAbsoluteDispatch, CpuInstructions.EOR, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x5D, Instructions.EORAbsoluteXDispatch, CpuInstructions.EOR, CpuAddressingModes.AbsoluteX);
        SetDirectOpcode(0x59, Instructions.EORAbsoluteYDispatch, CpuInstructions.EOR, CpuAddressingModes.AbsoluteY);
        SetDirectOpcode(0x41, Instructions.EORIndirectXDispatch, CpuInstructions.EOR, CpuAddressingModes.IndirectX);
        SetDirectOpcode(0x51, Instructions.EORIndirectYDispatch, CpuInstructions.EOR, CpuAddressingModes.IndirectY);
        SetDirectOpcode(0x52, Instructions.EORZeroPageIndirectDispatch, CpuInstructions.EOR, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetDirectOpcode(0x24, Instructions.BITZeroPageDispatch, CpuInstructions.BIT, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x2C, Instructions.BITAbsoluteDispatch, CpuInstructions.BIT, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x34, Instructions.BITZeroPageXDispatch, CpuInstructions.BIT, CpuAddressingModes.ZeroPageX); // 65C02
        SetDirectOpcode(0x3C, Instructions.BITAbsoluteXDispatch, CpuInstructions.BIT, CpuAddressingModes.AbsoluteX); // 65C02
        SetDirectOpcode(0x89, Instructions.BITImmediateDispatch, CpuInstructions.BIT, CpuAddressingModes.Immediate); // 65C02

        // Shift and Rotate Operations
        SetDirectOpcode(0x0A, Instructions.ASLAccumulatorDispatch, CpuInstructions.ASL, CpuAddressingModes.Accumulator);
        SetDirectOpcode(0x06, Instructions.ASLZeroPageDispatch, CpuInstructions.ASL, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x16, Instructions.ASLZeroPageXDispatch, CpuInstructions.ASL, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x0E, Instructions.ASLAbsoluteDispatch, CpuInstructions.ASL, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x1E, Instructions.ASLAbsoluteXDispatch, CpuInstructions.ASL, CpuAddressingModes.AbsoluteX);

        SetDirectOpcode(0x4A, Instructions.LSRAccumulatorDispatch, CpuInstructions.LSR, CpuAddressingModes.Accumulator);
        SetDirectOpcode(0x46, Instructions.LSRZeroPageDispatch, CpuInstructions.LSR, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x56, Instructions.LSRZeroPageXDispatch, CpuInstructions.LSR, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x4E, Instructions.LSRAbsoluteDispatch, CpuInstructions.LSR, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x5E, Instructions.LSRAbsoluteXDispatch, CpuInstructions.LSR, CpuAddressingModes.AbsoluteX);

        SetDirectOpcode(0x2A, Instructions.ROLAccumulatorDispatch, CpuInstructions.ROL, CpuAddressingModes.Accumulator);
        SetDirectOpcode(0x26, Instructions.ROLZeroPageDispatch, CpuInstructions.ROL, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x36, Instructions.ROLZeroPageXDispatch, CpuInstructions.ROL, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x2E, Instructions.ROLAbsoluteDispatch, CpuInstructions.ROL, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x3E, Instructions.ROLAbsoluteXDispatch, CpuInstructions.ROL, CpuAddressingModes.AbsoluteX);

        SetDirectOpcode(0x6A, Instructions.RORAccumulatorDispatch, CpuInstructions.ROR, CpuAddressingModes.Accumulator);
        SetDirectOpcode(0x66, Instructions.RORZeroPageDispatch, CpuInstructions.ROR, CpuAddressingModes.ZeroPage);
        SetDirectOpcode(0x76, Instructions.RORZeroPageXDispatch, CpuInstructions.ROR, CpuAddressingModes.ZeroPageX);
        SetDirectOpcode(0x6E, Instructions.RORAbsoluteDispatch, CpuInstructions.ROR, CpuAddressingModes.Absolute);
        SetDirectOpcode(0x7E, Instructions.RORAbsoluteXDispatch, CpuInstructions.ROR, CpuAddressingModes.AbsoluteX);

        return new(handlers, metadata, dispatchTargets);
    }

    /// <summary>
    /// Handles illegal/undefined opcodes by halting execution.
    /// </summary>
    private static void IllegalOpcode(ICpu cpu)
    {
        cpu.HaltReason = HaltState.Stp; // Halt on illegal opcode (stop execution)
    }
}