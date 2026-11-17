// <copyright file="FlagsTransferWorkload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// A 6502 loop dominated by the flag and transfer opcodes PR #436 moved onto direct dispatch.
/// </summary>
/// <remarks>
/// <para>
/// The body is CLC, SEC, CLI, SEI, CLV, CLD, SED, TAX, TXA, TAY, TYA, TXS, and TSX,
/// repeated eight times, then <c>JMP</c> back to the entry point. Those thirteen opcodes
/// are the complete set in <c>FlagsTransferDispatchParityTests</c>. That suite does not
/// cover any further 65C02 flag or transfer opcode.
/// </para>
/// <para>
/// Each implied opcode is 2 cycles and the absolute jump is 3, so one loop is 211 cycles.
/// The jump is the only instruction in the loop that is not one of the thirteen.
/// </para>
/// </remarks>
public static class FlagsTransferWorkload
{
    /// <summary>The workload name.</summary>
    public const string Name = "flags-transfer";

    /// <summary>The CPU family the program is written for. It runs on the 65C02 core.</summary>
    public const string CpuFamily = "6502";

    /// <summary>The address the program is written at, which is also the loop entry.</summary>
    public const Addr LoadAddress = 0x0200;

    /// <summary>The address execution starts at.</summary>
    public const Addr EntryPoint = LoadAddress;

    /// <summary>How many times the thirteen opcodes are repeated before the jump.</summary>
    public const int RepeatCount = 8;

    /// <summary>How many flag and transfer opcodes are in one copy of the body.</summary>
    public const int BodyLength = 13;

    /// <summary>Instructions in one loop, including the jump back to <see cref="EntryPoint"/>.</summary>
    public const int InstructionsPerLoop = (BodyLength * RepeatCount) + 1;

    /// <summary>Counted cycles in one loop.</summary>
    public const int CyclesPerLoop = (BodyLength * RepeatCount * 2) + 3;

    /// <summary>Loops executed by one benchmark invocation.</summary>
    public const int LoopsPerMeasurement = 512;

    /// <summary>Instructions executed by one benchmark invocation.</summary>
    public const int StepsPerMeasurement = InstructionsPerLoop * LoopsPerMeasurement;

    /// <summary>Register seed. Transfers copy this value and do not walk the stack.</summary>
    public const byte Seed = 0x42;

    private const byte JumpAbsolute = 0x4C;

    /// <summary>
    /// Gets the opcodes the loop must execute, in body order. The closing jump is not included.
    /// </summary>
    public static ReadOnlySpan<byte> ExercisedOpcodes => Body;

    private static ReadOnlySpan<byte> Body =>
    [
        0x18, 0x38, 0x58, 0x78, 0xB8, 0xD8, 0xF8,
        0xAA, 0x8A, 0xA8, 0x98, 0x9A, 0xBA,
    ];

    /// <summary>
    /// Writes the loop into a 64 KB image.
    /// </summary>
    /// <param name="memory">The RAM image. Must be at least 64 KB.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="memory"/> is shorter than 64 KB.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the body length does not match <see cref="BodyLength"/>.</exception>
    public static void Load(Span<byte> memory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, 0x10000);
        ReadOnlySpan<byte> body = Body;
        if (body.Length != BodyLength)
        {
            throw new InvalidOperationException("Flag and transfer body length drifted from BodyLength.");
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
    }

    /// <summary>
    /// Sets A, X, Y, and SP to <see cref="Seed"/> and points the CPU at <see cref="EntryPoint"/>.
    /// </summary>
    /// <param name="cpu">The CPU that will run the loop.</param>
    public static void Prepare(Cpu65C02 cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.Registers.A.SetByte(Seed);
        cpu.Registers.X.SetByte(Seed);
        cpu.Registers.Y.SetByte(Seed);
        cpu.Registers.SP.SetByte(Seed);
        cpu.Registers.P = ProcessorStatusFlags.R | ProcessorStatusFlags.I;
        cpu.SetPC(EntryPoint);
    }

    /// <summary>
    /// Builds one benchmark parameter per opcode in <see cref="ExercisedOpcodes"/>.
    /// </summary>
    /// <returns>The opcodes, in body order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a mnemonic is missing for an opcode.</exception>
    public static DispatchOpcode[] CreateOpcodes()
    {
        string[] names =
        [
            "CLC", "SEC", "CLI", "SEI", "CLV", "CLD", "SED",
            "TAX", "TXA", "TAY", "TYA", "TXS", "TSX",
        ];
        ReadOnlySpan<byte> body = Body;
        if (names.Length != body.Length)
        {
            throw new InvalidOperationException("Flag and transfer opcode names do not match the loop body.");
        }

        var opcodes = new DispatchOpcode[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            opcodes[i] = new DispatchOpcode(body[i], names[i]);
        }

        return opcodes;
    }
}