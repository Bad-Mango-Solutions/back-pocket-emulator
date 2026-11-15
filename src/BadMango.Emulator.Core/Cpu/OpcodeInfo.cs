// <copyright file="OpcodeInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

/// <summary>
/// Encapsulates decoded opcode information including instruction, addressing mode, operand length, and base cycle cost.
/// </summary>
/// <remarks>
/// This structure provides a complete description of an opcode that can be used for disassembly,
/// debugging, and analysis purposes without relying on static arrays.
/// </remarks>
public readonly record struct OpcodeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpcodeInfo"/> struct.
    /// </summary>
    /// <param name="instruction">The CPU instruction mnemonic.</param>
    /// <param name="addressingMode">The addressing mode used by this opcode.</param>
    /// <param name="operandLength">The length of operands in bytes (0, 1, or 2).</param>
    /// <param name="baseCycles">The base cycle count for this opcode before conditional penalties.</param>
    public OpcodeInfo(CpuInstructions instruction, CpuAddressingModes addressingMode, byte operandLength, byte baseCycles = 0)
    {
        Instruction = instruction;
        AddressingMode = addressingMode;
        OperandLength = operandLength;
        BaseCycles = baseCycles;
    }

    /// <summary>
    /// Gets the CPU instruction mnemonic for this opcode.
    /// </summary>
    public CpuInstructions Instruction { get; }

    /// <summary>
    /// Gets the addressing mode used by this opcode.
    /// </summary>
    public CpuAddressingModes AddressingMode { get; }

    /// <summary>
    /// Gets the operand length in bytes (0, 1, or 2).
    /// </summary>
    public byte OperandLength { get; }

    /// <summary>
    /// Gets the base cycle count for this opcode before conditional penalties.
    /// </summary>
    public byte BaseCycles { get; }

    /// <summary>
    /// Gets a value indicating whether this opcode represents a valid instruction.
    /// </summary>
    public bool IsValid => Instruction != CpuInstructions.None;

    /// <summary>
    /// Gets the total instruction length including opcode and operands.
    /// </summary>
    public int TotalLength => 1 + OperandLength;
}