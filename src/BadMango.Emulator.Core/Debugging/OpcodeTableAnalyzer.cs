// <copyright file="OpcodeTableAnalyzer.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Debugging;

using Core.Cpu;

/// <summary>
/// Provides opcode metadata tables for debugging and disassembly.
/// </summary>
public static class OpcodeTableAnalyzer
{
    /// <summary>
    /// Builds an operand length lookup table from opcode metadata.
    /// </summary>
    /// <param name="opcodeTable">The opcode table to analyze.</param>
    /// <returns>An array of 256 bytes where each index is an opcode and the value is the operand length.</returns>
    public static byte[] BuildOperandLengthTable(OpcodeTable opcodeTable)
    {
        ArgumentNullException.ThrowIfNull(opcodeTable);

        var operandLengths = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            operandLengths[i] = opcodeTable.GetMetadata((byte)i).OperandLength;
        }

        return operandLengths;
    }

    /// <summary>
    /// Builds a complete opcode information table from opcode metadata.
    /// </summary>
    /// <param name="opcodeTable">The opcode table to analyze.</param>
    /// <returns>
    /// A dictionary mapping opcode bytes (0x00-0xFF) to <see cref="OpcodeInfo"/> structures
    /// containing the instruction mnemonic, addressing mode, and operand length.
    /// Invalid or unimplemented opcodes will have <see cref="CpuInstructions.None"/> as the instruction.
    /// </returns>
    public static Dictionary<byte, OpcodeInfo> BuildOpcodeInfoTable(OpcodeTable opcodeTable)
    {
        ArgumentNullException.ThrowIfNull(opcodeTable);

        var opcodeInfoTable = new Dictionary<byte, OpcodeInfo>(256);

        for (int i = 0; i < 256; i++)
        {
            var opcode = (byte)i;
            opcodeInfoTable[opcode] = opcodeTable.GetMetadata(opcode);
        }

        return opcodeInfoTable;
    }

    /// <summary>
    /// Builds a complete opcode information table as an array from opcode metadata.
    /// </summary>
    /// <param name="opcodeTable">The opcode table to analyze.</param>
    /// <returns>
    /// An array of 256 <see cref="OpcodeInfo"/> structures where each index is an opcode (0x00-0xFF).
    /// Invalid or unimplemented opcodes will have <see cref="CpuInstructions.None"/> as the instruction.
    /// </returns>
    public static OpcodeInfo[] BuildOpcodeInfoArray(OpcodeTable opcodeTable)
    {
        ArgumentNullException.ThrowIfNull(opcodeTable);

        var opcodeInfoArray = new OpcodeInfo[256];

        for (int i = 0; i < 256; i++)
        {
            opcodeInfoArray[i] = opcodeTable.GetMetadata((byte)i);
        }

        return opcodeInfoArray;
    }
}