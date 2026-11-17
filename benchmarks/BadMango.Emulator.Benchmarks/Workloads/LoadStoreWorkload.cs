// <copyright file="LoadStoreWorkload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// A 65C02 loop dominated by the load and store opcodes moved onto direct dispatch.
/// </summary>
/// <remarks>
/// <para>
/// The body is every LDA, LDX, LDY, STA, STX, STY, and STZ opcode, including the
/// 65C02 <c>(zp)</c> mode, then <c>JMP</c> back to the entry point. X and Y are
/// loaded with zero before any indexed operand, and every later load of X or Y
/// also reads zero, so indexed addresses stay on the base page. One copy is 147
/// cycles. The jump is the only instruction that is not one of those opcodes.
/// </para>
/// <para>
/// The body is repeated four times. Stores write scratch locations that are not
/// code, pointers, or load sources, so the next copy sees the same data.
/// </para>
/// </remarks>
public static class LoadStoreWorkload
{
    /// <summary>The workload name.</summary>
    public const string Name = "load-store";

    /// <summary>The CPU family. The loop uses 65C02 <c>STZ</c> and <c>(zp)</c>.</summary>
    public const string CpuFamily = "65C02";

    /// <summary>The address the program is written at, which is also the loop entry.</summary>
    public const Addr LoadAddress = 0x0400;

    /// <summary>The address execution starts at.</summary>
    public const Addr EntryPoint = LoadAddress;

    /// <summary>How many times the load and store body is repeated before the jump.</summary>
    public const int RepeatCount = 4;

    /// <summary>How many load and store opcodes are in one copy of the body.</summary>
    public const int BodyLength = 37;

    /// <summary>Counted cycles in one copy of the body, with no page-cross penalty.</summary>
    public const int BodyCycles = 147;

    /// <summary>Instructions in one loop, including the jump back to <see cref="EntryPoint"/>.</summary>
    public const int InstructionsPerLoop = (BodyLength * RepeatCount) + 1;

    /// <summary>Counted cycles in one loop.</summary>
    public const int CyclesPerLoop = (BodyCycles * RepeatCount) + 3;

    /// <summary>Loops executed by one benchmark invocation.</summary>
    public const int LoopsPerMeasurement = 256;

    /// <summary>Instructions executed by one benchmark invocation.</summary>
    public const int StepsPerMeasurement = InstructionsPerLoop * LoopsPerMeasurement;

    private const byte JumpAbsolute = 0x4C;

    /// <summary>
    /// Gets one copy of the loop body, in memory order. Each opcode byte is followed by its operand bytes.
    /// The closing jump is not included. <see cref="OpcodeBytes"/> is the opcode bytes alone.
    /// </summary>
    public static ReadOnlySpan<byte> ExercisedOpcodes => Body;

    private static ReadOnlySpan<byte> Body =>
    [
        0xA2, 0x00,
        0xA0, 0x00,
        0xA9, 0x42,
        0xA5, 0x10,
        0xB5, 0x18,
        0xAD, 0x00, 0x30,
        0xBD, 0x00, 0x31,
        0xB9, 0x00, 0x32,
        0xA1, 0x80,
        0xB1, 0x82,
        0xB2, 0x84,
        0xA6, 0x11,
        0xB6, 0x1A,
        0xAE, 0x10, 0x30,
        0xBE, 0x10, 0x32,
        0xA4, 0x12,
        0xB4, 0x19,
        0xAC, 0x20, 0x30,
        0xBC, 0x10, 0x31,
        0x85, 0x90,
        0x95, 0x94,
        0x8D, 0x00, 0x33,
        0x9D, 0x00, 0x34,
        0x99, 0x00, 0x35,
        0x81, 0x86,
        0x91, 0x88,
        0x92, 0x8A,
        0x86, 0x91,
        0x96, 0x97,
        0x8E, 0x10, 0x33,
        0x84, 0x92,
        0x94, 0x95,
        0x8C, 0x20, 0x33,
        0x64, 0x93,
        0x74, 0x96,
        0x9C, 0x30, 0x33,
        0x9E, 0x00, 0x36,
    ];

    /// <summary>
    /// Writes the loop and its data into a 64 KB image.
    /// </summary>
    /// <param name="memory">The RAM image. Must be at least 64 KB.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="memory"/> is shorter than 64 KB.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the body does not contain <see cref="BodyLength"/> opcodes.</exception>
    public static void Load(Span<byte> memory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, 0x10000);
        ReadOnlySpan<byte> body = Body;
        if (CountOpcodes(body) != BodyLength)
        {
            throw new InvalidOperationException("Load and store body length drifted from BodyLength.");
        }

        int address = (int)EntryPoint;
        for (int repeat = 0; repeat < RepeatCount; repeat++)
        {
            body.CopyTo(memory.Slice(address, body.Length));
            address += body.Length;
        }

        memory[address++] = JumpAbsolute;
        memory[address++] = (byte)(EntryPoint & 0xFF);
        memory[address] = (byte)((EntryPoint >> 8) & 0xFF);

        memory[0x10] = 0x42;
        memory[0x18] = 0x42;
        memory[0x3000] = 0x42;
        memory[0x3100] = 0x42;
        memory[0x3200] = 0x42;
        memory[0x2100] = 0x42;
        memory[0x2200] = 0x42;
        memory[0x2300] = 0x42;
        WritePointer(memory, 0x80, 0x2100);
        WritePointer(memory, 0x82, 0x2200);
        WritePointer(memory, 0x84, 0x2300);
        WritePointer(memory, 0x86, 0x2400);
        WritePointer(memory, 0x88, 0x2500);
        WritePointer(memory, 0x8A, 0x2600);
    }

    /// <summary>
    /// Clears a halt and points the CPU at <see cref="EntryPoint"/>.
    /// The first two instructions force X and Y to zero.
    /// </summary>
    /// <param name="cpu">The CPU that will run the loop.</param>
    public static void Prepare(Cpu65C02 cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.Registers.P = ProcessorStatusFlags.R | ProcessorStatusFlags.I;
        cpu.SetPC(EntryPoint);
    }

    /// <summary>
    /// Builds one benchmark parameter per opcode returned by <see cref="OpcodeBytes"/>.
    /// </summary>
    /// <returns>The opcodes, in body order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a mnemonic is missing for an opcode.</exception>
    public static DispatchOpcode[] CreateOpcodes()
    {
        string[] names =
        [
            "LDX #", "LDY #", "LDA #", "LDA zp", "LDA zp,X", "LDA abs", "LDA abs,X", "LDA abs,Y",
            "LDA (zp,X)", "LDA (zp),Y", "LDA (zp)",
            "LDX zp", "LDX zp,Y", "LDX abs", "LDX abs,Y",
            "LDY zp", "LDY zp,X", "LDY abs", "LDY abs,X",
            "STA zp", "STA zp,X", "STA abs", "STA abs,X", "STA abs,Y", "STA (zp,X)", "STA (zp),Y", "STA (zp)",
            "STX zp", "STX zp,Y", "STX abs",
            "STY zp", "STY zp,X", "STY abs",
            "STZ zp", "STZ zp,X", "STZ abs", "STZ abs,X",
        ];
        byte[] opcodes = OpcodeBytes();
        if (names.Length != opcodes.Length)
        {
            throw new InvalidOperationException("Load and store opcode names do not match the loop body.");
        }

        var parameters = new DispatchOpcode[opcodes.Length];
        for (int i = 0; i < opcodes.Length; i++)
        {
            parameters[i] = new DispatchOpcode(opcodes[i], names[i]);
        }

        return parameters;
    }

    /// <summary>
    /// Copies the opcode bytes out of <see cref="ExercisedOpcodes"/>, skipping operands.
    /// </summary>
    /// <returns>The 37 opcode bytes, in body order.</returns>
    public static byte[] OpcodeBytes()
    {
        ReadOnlySpan<byte> body = Body;
        var opcodes = new byte[BodyLength];
        int opcodeIndex = 0;
        int cursor = 0;
        while (cursor < body.Length)
        {
            opcodes[opcodeIndex++] = body[cursor];
            cursor += LengthOf(body[cursor]);
        }

        return opcodes;
    }

    private static void WritePointer(Span<byte> memory, int address, ushort target)
    {
        memory[address] = (byte)(target & 0xFF);
        memory[address + 1] = (byte)((target >> 8) & 0xFF);
    }

    private static int CountOpcodes(ReadOnlySpan<byte> body)
    {
        int count = 0;
        int cursor = 0;
        while (cursor < body.Length)
        {
            cursor += LengthOf(body[cursor]);
            count++;
        }

        return count;
    }

    private static int LengthOf(byte opcode)
    {
        return opcode switch
        {
            0xAD or 0xBD or 0xB9 or 0xAE or 0xBE or 0xAC or 0xBC or 0x8D or 0x9D or 0x99 or 0x8E or 0x8C or 0x9C or 0x9E => 3,
            _ => 2,
        };
    }
}