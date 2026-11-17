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

        // LDA - Load Accumulator (true compositional pattern)
        SetOpcode(0xA9, Instructions.LDA(AddressingModes.Immediate), CpuInstructions.LDA, CpuAddressingModes.Immediate);
        SetOpcode(0xA5, Instructions.LDA(AddressingModes.ZeroPage), CpuInstructions.LDA, CpuAddressingModes.ZeroPage);
        SetOpcode(0xB5, Instructions.LDA(AddressingModes.ZeroPageX), CpuInstructions.LDA, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xAD, Instructions.LDA(AddressingModes.Absolute), CpuInstructions.LDA, CpuAddressingModes.Absolute);
        SetOpcode(0xBD, Instructions.LDA(AddressingModes.AbsoluteX), CpuInstructions.LDA, CpuAddressingModes.AbsoluteX);
        SetOpcode(0xB9, Instructions.LDA(AddressingModes.AbsoluteY), CpuInstructions.LDA, CpuAddressingModes.AbsoluteY);
        SetOpcode(0xA1, Instructions.LDA(AddressingModes.IndirectX), CpuInstructions.LDA, CpuAddressingModes.IndirectX);
        SetOpcode(0xB1, Instructions.LDA(AddressingModes.IndirectY), CpuInstructions.LDA, CpuAddressingModes.IndirectY);
        SetOpcode(0xB2, Instructions.LDA(AddressingModes.ZeroPageIndirect), CpuInstructions.LDA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        // STA - Store Accumulator
        SetOpcode(0x85, Instructions.STA(AddressingModes.ZeroPage), CpuInstructions.STA, CpuAddressingModes.ZeroPage);
        SetOpcode(0x95, Instructions.STA(AddressingModes.ZeroPageX), CpuInstructions.STA, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x8D, Instructions.STA(AddressingModes.Absolute), CpuInstructions.STA, CpuAddressingModes.Absolute);
        SetOpcode(0x9D, Instructions.STA(AddressingModes.AbsoluteXWrite), CpuInstructions.STA, CpuAddressingModes.AbsoluteX); // Write version always takes max cycles
        SetOpcode(0x99, Instructions.STA(AddressingModes.AbsoluteYWrite), CpuInstructions.STA, CpuAddressingModes.AbsoluteY); // Write version always takes max cycles
        SetOpcode(0x81, Instructions.STA(AddressingModes.IndirectX), CpuInstructions.STA, CpuAddressingModes.IndirectX);
        SetOpcode(0x91, Instructions.STA(AddressingModes.IndirectYWrite), CpuInstructions.STA, CpuAddressingModes.IndirectY); // Write version always takes max cycles
        SetOpcode(0x92, Instructions.STA(AddressingModes.ZeroPageIndirect), CpuInstructions.STA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        // LDX - Load X Register
        SetOpcode(0xA2, Instructions.LDX(AddressingModes.Immediate), CpuInstructions.LDX, CpuAddressingModes.Immediate);
        SetOpcode(0xA6, Instructions.LDX(AddressingModes.ZeroPage), CpuInstructions.LDX, CpuAddressingModes.ZeroPage);
        SetOpcode(0xB6, Instructions.LDX(AddressingModes.ZeroPageY), CpuInstructions.LDX, CpuAddressingModes.ZeroPageY);
        SetOpcode(0xAE, Instructions.LDX(AddressingModes.Absolute), CpuInstructions.LDX, CpuAddressingModes.Absolute);
        SetOpcode(0xBE, Instructions.LDX(AddressingModes.AbsoluteY), CpuInstructions.LDX, CpuAddressingModes.AbsoluteY);

        // LDY - Load Y Register
        SetOpcode(0xA0, Instructions.LDY(AddressingModes.Immediate), CpuInstructions.LDY, CpuAddressingModes.Immediate);
        SetOpcode(0xA4, Instructions.LDY(AddressingModes.ZeroPage), CpuInstructions.LDY, CpuAddressingModes.ZeroPage);
        SetOpcode(0xB4, Instructions.LDY(AddressingModes.ZeroPageX), CpuInstructions.LDY, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xAC, Instructions.LDY(AddressingModes.Absolute), CpuInstructions.LDY, CpuAddressingModes.Absolute);
        SetOpcode(0xBC, Instructions.LDY(AddressingModes.AbsoluteX), CpuInstructions.LDY, CpuAddressingModes.AbsoluteX);

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
        SetOpcode(0x86, Instructions.STX(AddressingModes.ZeroPage), CpuInstructions.STX, CpuAddressingModes.ZeroPage);
        SetOpcode(0x96, Instructions.STX(AddressingModes.ZeroPageY), CpuInstructions.STX, CpuAddressingModes.ZeroPageY);
        SetOpcode(0x8E, Instructions.STX(AddressingModes.Absolute), CpuInstructions.STX, CpuAddressingModes.Absolute);

        // STY - Store Y Register
        SetOpcode(0x84, Instructions.STY(AddressingModes.ZeroPage), CpuInstructions.STY, CpuAddressingModes.ZeroPage);
        SetOpcode(0x94, Instructions.STY(AddressingModes.ZeroPageX), CpuInstructions.STY, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x8C, Instructions.STY(AddressingModes.Absolute), CpuInstructions.STY, CpuAddressingModes.Absolute);

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
        SetOpcode(0x64, Instructions.STZ(AddressingModes.ZeroPage), CpuInstructions.STZ, CpuAddressingModes.ZeroPage); // Store Zero
        SetOpcode(0x74, Instructions.STZ(AddressingModes.ZeroPageX), CpuInstructions.STZ, CpuAddressingModes.ZeroPageX); // Store Zero
        SetOpcode(0x9C, Instructions.STZ(AddressingModes.Absolute), CpuInstructions.STZ, CpuAddressingModes.Absolute); // Store Zero
        SetOpcode(0x9E, Instructions.STZ(AddressingModes.AbsoluteXWrite), CpuInstructions.STZ, CpuAddressingModes.AbsoluteX); // Store Zero
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
        SetOpcode(0xC9, Instructions.CMP(AddressingModes.Immediate), CpuInstructions.CMP, CpuAddressingModes.Immediate);
        SetOpcode(0xC5, Instructions.CMP(AddressingModes.ZeroPage), CpuInstructions.CMP, CpuAddressingModes.ZeroPage);
        SetOpcode(0xD5, Instructions.CMP(AddressingModes.ZeroPageX), CpuInstructions.CMP, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xCD, Instructions.CMP(AddressingModes.Absolute), CpuInstructions.CMP, CpuAddressingModes.Absolute);
        SetOpcode(0xDD, Instructions.CMP(AddressingModes.AbsoluteX), CpuInstructions.CMP, CpuAddressingModes.AbsoluteX);
        SetOpcode(0xD9, Instructions.CMP(AddressingModes.AbsoluteY), CpuInstructions.CMP, CpuAddressingModes.AbsoluteY);
        SetOpcode(0xC1, Instructions.CMP(AddressingModes.IndirectX), CpuInstructions.CMP, CpuAddressingModes.IndirectX);
        SetOpcode(0xD1, Instructions.CMP(AddressingModes.IndirectY), CpuInstructions.CMP, CpuAddressingModes.IndirectY);
        SetOpcode(0xD2, Instructions.CMP(AddressingModes.ZeroPageIndirect), CpuInstructions.CMP, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0xE0, Instructions.CPX(AddressingModes.Immediate), CpuInstructions.CPX, CpuAddressingModes.Immediate);
        SetOpcode(0xE4, Instructions.CPX(AddressingModes.ZeroPage), CpuInstructions.CPX, CpuAddressingModes.ZeroPage);
        SetOpcode(0xEC, Instructions.CPX(AddressingModes.Absolute), CpuInstructions.CPX, CpuAddressingModes.Absolute);

        SetOpcode(0xC0, Instructions.CPY(AddressingModes.Immediate), CpuInstructions.CPY, CpuAddressingModes.Immediate);
        SetOpcode(0xC4, Instructions.CPY(AddressingModes.ZeroPage), CpuInstructions.CPY, CpuAddressingModes.ZeroPage);
        SetOpcode(0xCC, Instructions.CPY(AddressingModes.Absolute), CpuInstructions.CPY, CpuAddressingModes.Absolute);

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
        SetOpcode(0x69, Instructions.ADC(AddressingModes.Immediate), CpuInstructions.ADC, CpuAddressingModes.Immediate);
        SetOpcode(0x65, Instructions.ADC(AddressingModes.ZeroPage), CpuInstructions.ADC, CpuAddressingModes.ZeroPage);
        SetOpcode(0x75, Instructions.ADC(AddressingModes.ZeroPageX), CpuInstructions.ADC, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x6D, Instructions.ADC(AddressingModes.Absolute), CpuInstructions.ADC, CpuAddressingModes.Absolute);
        SetOpcode(0x7D, Instructions.ADC(AddressingModes.AbsoluteX), CpuInstructions.ADC, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x79, Instructions.ADC(AddressingModes.AbsoluteY), CpuInstructions.ADC, CpuAddressingModes.AbsoluteY);
        SetOpcode(0x61, Instructions.ADC(AddressingModes.IndirectX), CpuInstructions.ADC, CpuAddressingModes.IndirectX);
        SetOpcode(0x71, Instructions.ADC(AddressingModes.IndirectY), CpuInstructions.ADC, CpuAddressingModes.IndirectY);
        SetOpcode(0x72, Instructions.ADC(AddressingModes.ZeroPageIndirect), CpuInstructions.ADC, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0xE9, Instructions.SBC(AddressingModes.Immediate), CpuInstructions.SBC, CpuAddressingModes.Immediate);
        SetOpcode(0xE5, Instructions.SBC(AddressingModes.ZeroPage), CpuInstructions.SBC, CpuAddressingModes.ZeroPage);
        SetOpcode(0xF5, Instructions.SBC(AddressingModes.ZeroPageX), CpuInstructions.SBC, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xED, Instructions.SBC(AddressingModes.Absolute), CpuInstructions.SBC, CpuAddressingModes.Absolute);
        SetOpcode(0xFD, Instructions.SBC(AddressingModes.AbsoluteX), CpuInstructions.SBC, CpuAddressingModes.AbsoluteX);
        SetOpcode(0xF9, Instructions.SBC(AddressingModes.AbsoluteY), CpuInstructions.SBC, CpuAddressingModes.AbsoluteY);
        SetOpcode(0xE1, Instructions.SBC(AddressingModes.IndirectX), CpuInstructions.SBC, CpuAddressingModes.IndirectX);
        SetOpcode(0xF1, Instructions.SBC(AddressingModes.IndirectY), CpuInstructions.SBC, CpuAddressingModes.IndirectY);
        SetOpcode(0xF2, Instructions.SBC(AddressingModes.ZeroPageIndirect), CpuInstructions.SBC, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0xE6, Instructions.INC(AddressingModes.ZeroPage), CpuInstructions.INC, CpuAddressingModes.ZeroPage);
        SetOpcode(0xF6, Instructions.INC(AddressingModes.ZeroPageX), CpuInstructions.INC, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xEE, Instructions.INC(AddressingModes.Absolute), CpuInstructions.INC, CpuAddressingModes.Absolute);
        SetOpcode(0xFE, Instructions.INC(AddressingModes.AbsoluteXWrite), CpuInstructions.INC, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x1A, Instructions.INA(AddressingModes.Accumulator), CpuInstructions.INA, CpuAddressingModes.Accumulator); // 65C02 INC A

        SetOpcode(0xC6, Instructions.DEC(AddressingModes.ZeroPage), CpuInstructions.DEC, CpuAddressingModes.ZeroPage);
        SetOpcode(0xD6, Instructions.DEC(AddressingModes.ZeroPageX), CpuInstructions.DEC, CpuAddressingModes.ZeroPageX);
        SetOpcode(0xCE, Instructions.DEC(AddressingModes.Absolute), CpuInstructions.DEC, CpuAddressingModes.Absolute);
        SetOpcode(0xDE, Instructions.DEC(AddressingModes.AbsoluteXWrite), CpuInstructions.DEC, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x3A, Instructions.DEA(AddressingModes.Accumulator), CpuInstructions.DEA, CpuAddressingModes.Accumulator); // 65C02 DEC A

        SetOpcode(0xE8, Instructions.INX(AddressingModes.Implied), CpuInstructions.INX, CpuAddressingModes.Implied);
        SetOpcode(0xC8, Instructions.INY(AddressingModes.Implied), CpuInstructions.INY, CpuAddressingModes.Implied);
        SetOpcode(0xCA, Instructions.DEX(AddressingModes.Implied), CpuInstructions.DEX, CpuAddressingModes.Implied);
        SetOpcode(0x88, Instructions.DEY(AddressingModes.Implied), CpuInstructions.DEY, CpuAddressingModes.Implied);

        // Logical Operations
        SetOpcode(0x29, Instructions.AND(AddressingModes.Immediate), CpuInstructions.AND, CpuAddressingModes.Immediate);
        SetOpcode(0x25, Instructions.AND(AddressingModes.ZeroPage), CpuInstructions.AND, CpuAddressingModes.ZeroPage);
        SetOpcode(0x35, Instructions.AND(AddressingModes.ZeroPageX), CpuInstructions.AND, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x2D, Instructions.AND(AddressingModes.Absolute), CpuInstructions.AND, CpuAddressingModes.Absolute);
        SetOpcode(0x3D, Instructions.AND(AddressingModes.AbsoluteX), CpuInstructions.AND, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x39, Instructions.AND(AddressingModes.AbsoluteY), CpuInstructions.AND, CpuAddressingModes.AbsoluteY);
        SetOpcode(0x21, Instructions.AND(AddressingModes.IndirectX), CpuInstructions.AND, CpuAddressingModes.IndirectX);
        SetOpcode(0x31, Instructions.AND(AddressingModes.IndirectY), CpuInstructions.AND, CpuAddressingModes.IndirectY);
        SetOpcode(0x32, Instructions.AND(AddressingModes.ZeroPageIndirect), CpuInstructions.AND, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0x09, Instructions.ORA(AddressingModes.Immediate), CpuInstructions.ORA, CpuAddressingModes.Immediate);
        SetOpcode(0x05, Instructions.ORA(AddressingModes.ZeroPage), CpuInstructions.ORA, CpuAddressingModes.ZeroPage);
        SetOpcode(0x15, Instructions.ORA(AddressingModes.ZeroPageX), CpuInstructions.ORA, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x0D, Instructions.ORA(AddressingModes.Absolute), CpuInstructions.ORA, CpuAddressingModes.Absolute);
        SetOpcode(0x1D, Instructions.ORA(AddressingModes.AbsoluteX), CpuInstructions.ORA, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x19, Instructions.ORA(AddressingModes.AbsoluteY), CpuInstructions.ORA, CpuAddressingModes.AbsoluteY);
        SetOpcode(0x01, Instructions.ORA(AddressingModes.IndirectX), CpuInstructions.ORA, CpuAddressingModes.IndirectX);
        SetOpcode(0x11, Instructions.ORA(AddressingModes.IndirectY), CpuInstructions.ORA, CpuAddressingModes.IndirectY);
        SetOpcode(0x12, Instructions.ORA(AddressingModes.ZeroPageIndirect), CpuInstructions.ORA, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0x49, Instructions.EOR(AddressingModes.Immediate), CpuInstructions.EOR, CpuAddressingModes.Immediate);
        SetOpcode(0x45, Instructions.EOR(AddressingModes.ZeroPage), CpuInstructions.EOR, CpuAddressingModes.ZeroPage);
        SetOpcode(0x55, Instructions.EOR(AddressingModes.ZeroPageX), CpuInstructions.EOR, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x4D, Instructions.EOR(AddressingModes.Absolute), CpuInstructions.EOR, CpuAddressingModes.Absolute);
        SetOpcode(0x5D, Instructions.EOR(AddressingModes.AbsoluteX), CpuInstructions.EOR, CpuAddressingModes.AbsoluteX);
        SetOpcode(0x59, Instructions.EOR(AddressingModes.AbsoluteY), CpuInstructions.EOR, CpuAddressingModes.AbsoluteY);
        SetOpcode(0x41, Instructions.EOR(AddressingModes.IndirectX), CpuInstructions.EOR, CpuAddressingModes.IndirectX);
        SetOpcode(0x51, Instructions.EOR(AddressingModes.IndirectY), CpuInstructions.EOR, CpuAddressingModes.IndirectY);
        SetOpcode(0x52, Instructions.EOR(AddressingModes.ZeroPageIndirect), CpuInstructions.EOR, CpuAddressingModes.ZeroPageIndirect); // 65C02

        SetOpcode(0x24, Instructions.BIT(AddressingModes.ZeroPage), CpuInstructions.BIT, CpuAddressingModes.ZeroPage);
        SetOpcode(0x2C, Instructions.BIT(AddressingModes.Absolute), CpuInstructions.BIT, CpuAddressingModes.Absolute);
        SetOpcode(0x34, Instructions.BIT(AddressingModes.ZeroPageX), CpuInstructions.BIT, CpuAddressingModes.ZeroPageX); // 65C02
        SetOpcode(0x3C, Instructions.BIT(AddressingModes.AbsoluteX), CpuInstructions.BIT, CpuAddressingModes.AbsoluteX); // 65C02
        SetOpcode(0x89, Instructions.BITImmediate(AddressingModes.Immediate), CpuInstructions.BIT, CpuAddressingModes.Immediate); // 65C02

        // Shift and Rotate Operations
        SetOpcode(0x0A, Instructions.ASLa(AddressingModes.Accumulator), CpuInstructions.ASL, CpuAddressingModes.Accumulator);
        SetOpcode(0x06, Instructions.ASL(AddressingModes.ZeroPage), CpuInstructions.ASL, CpuAddressingModes.ZeroPage);
        SetOpcode(0x16, Instructions.ASL(AddressingModes.ZeroPageX), CpuInstructions.ASL, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x0E, Instructions.ASL(AddressingModes.Absolute), CpuInstructions.ASL, CpuAddressingModes.Absolute);
        SetOpcode(0x1E, Instructions.ASL(AddressingModes.AbsoluteX), CpuInstructions.ASL, CpuAddressingModes.AbsoluteX);

        SetOpcode(0x4A, Instructions.LSRa(AddressingModes.Accumulator), CpuInstructions.LSR, CpuAddressingModes.Accumulator);
        SetOpcode(0x46, Instructions.LSR(AddressingModes.ZeroPage), CpuInstructions.LSR, CpuAddressingModes.ZeroPage);
        SetOpcode(0x56, Instructions.LSR(AddressingModes.ZeroPageX), CpuInstructions.LSR, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x4E, Instructions.LSR(AddressingModes.Absolute), CpuInstructions.LSR, CpuAddressingModes.Absolute);
        SetOpcode(0x5E, Instructions.LSR(AddressingModes.AbsoluteX), CpuInstructions.LSR, CpuAddressingModes.AbsoluteX);

        SetOpcode(0x2A, Instructions.ROLa(AddressingModes.Accumulator), CpuInstructions.ROL, CpuAddressingModes.Accumulator);
        SetOpcode(0x26, Instructions.ROL(AddressingModes.ZeroPage), CpuInstructions.ROL, CpuAddressingModes.ZeroPage);
        SetOpcode(0x36, Instructions.ROL(AddressingModes.ZeroPageX), CpuInstructions.ROL, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x2E, Instructions.ROL(AddressingModes.Absolute), CpuInstructions.ROL, CpuAddressingModes.Absolute);
        SetOpcode(0x3E, Instructions.ROL(AddressingModes.AbsoluteX), CpuInstructions.ROL, CpuAddressingModes.AbsoluteX);

        SetOpcode(0x6A, Instructions.RORa(AddressingModes.Accumulator), CpuInstructions.ROR, CpuAddressingModes.Accumulator);
        SetOpcode(0x66, Instructions.ROR(AddressingModes.ZeroPage), CpuInstructions.ROR, CpuAddressingModes.ZeroPage);
        SetOpcode(0x76, Instructions.ROR(AddressingModes.ZeroPageX), CpuInstructions.ROR, CpuAddressingModes.ZeroPageX);
        SetOpcode(0x6E, Instructions.ROR(AddressingModes.Absolute), CpuInstructions.ROR, CpuAddressingModes.Absolute);
        SetOpcode(0x7E, Instructions.ROR(AddressingModes.AbsoluteX), CpuInstructions.ROR, CpuAddressingModes.AbsoluteX);

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