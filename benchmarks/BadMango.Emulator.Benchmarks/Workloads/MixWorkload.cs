// <copyright file="MixWorkload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// The instruction mix used to measure the shipping core since the cycle-step spike.
/// </summary>
/// <remarks>
/// One loop is the 25-instruction program from that harness: immediate, zero-page,
/// absolute, and indexed loads and stores, a page-crossing and a non-crossing form of
/// the indexed instructions, <c>STZ</c>, <c>INC</c>, <c>ASL</c>, <c>LDA (zp)</c>,
/// <c>LDA (zp),Y</c>, <c>PHA</c>/<c>PLA</c>, <c>JSR</c>/<c>RTS</c>, a taken branch,
/// a not-taken branch, a page-crossing branch, and an indirect jump back to the entry
/// point. After the absolute-indexed timing fix the shipping core counts 113 cycles.
/// </remarks>
public static class MixWorkload
{
    /// <summary>The workload name.</summary>
    public const string Name = "mix";

    /// <summary>The CPU family. The loop uses 65C02 <c>STZ</c> and <c>LDA (zp)</c>.</summary>
    public const string CpuFamily = "65C02";

    /// <summary>The address of the first instruction.</summary>
    public const Addr EntryPoint = 0x07C8;

    /// <summary>X value required by the indexed operands.</summary>
    public const byte IndexX = 0x05;

    /// <summary>Y value required by the indirect-indexed operand.</summary>
    public const byte IndexY = 0x20;

    /// <summary>Stack pointer at loop entry. <c>PHA</c>/<c>PLA</c> and <c>JSR</c>/<c>RTS</c> restore it.</summary>
    public const byte Stack = 0xF0;

    /// <summary>Instructions in one loop, including <c>RTS</c> and the indirect jump.</summary>
    public const int InstructionsPerLoop = 25;

    /// <summary>Counted cycles in one loop on the shipping core.</summary>
    public const int CyclesPerLoop = 113;

    /// <summary>Loops executed by one benchmark invocation.</summary>
    public const int LoopsPerMeasurement = 2000;

    /// <summary>Instructions executed by one benchmark invocation.</summary>
    public const int StepsPerMeasurement = InstructionsPerLoop * LoopsPerMeasurement;

    /// <summary>
    /// Writes the loop and its data into a zeroed 64 KB image.
    /// </summary>
    /// <param name="memory">The RAM image. Must be at least 64 KB.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="memory"/> is shorter than 64 KB.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the page-crossing branch is not at <c>$07FD</c>.</exception>
    public static void Load(Span<byte> memory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, 0x10000);

        ushort address = (ushort)EntryPoint;
        Emit(memory, ref address, 0xA9, 0x00);
        Emit(memory, ref address, 0xF0, 0x00);
        Emit(memory, ref address, 0xA9, 0x01);
        Emit(memory, ref address, 0xF0, 0x00);
        Emit(memory, ref address, 0xA5, 0x10);
        Emit(memory, ref address, 0xB5, 0x20);
        Emit(memory, ref address, 0xAD, 0x00, 0x50);
        Emit(memory, ref address, 0xBD, 0x00, 0x50);
        Emit(memory, ref address, 0xBD, 0xFE, 0x50);
        Emit(memory, ref address, 0x9D, 0x00, 0x60);
        Emit(memory, ref address, 0x9E, 0x00, 0x61);
        Emit(memory, ref address, 0x9E, 0xFE, 0x61);
        Emit(memory, ref address, 0xFE, 0x00, 0x70);
        Emit(memory, ref address, 0xFE, 0xFE, 0x70);
        Emit(memory, ref address, 0x1E, 0x00, 0x80);
        Emit(memory, ref address, 0x1E, 0xFE, 0x80);
        Emit(memory, ref address, 0xB2, 0x30);
        Emit(memory, ref address, 0xB1, 0x50);
        Emit(memory, ref address, 0x48);
        Emit(memory, ref address, 0x68);
        Emit(memory, ref address, 0x20, 0x10, 0x07);
        Emit(memory, ref address, 0xA9, 0x00);
        if (address != 0x07FD)
        {
            throw new InvalidOperationException($"Mix layout drifted: page-cross branch is at ${address:X4}, expected $07FD.");
        }

        Emit(memory, ref address, 0xF0, 0x01);
        memory[0x0710] = 0x60;
        memory[0x0800] = 0x6C;
        memory[0x0801] = 0x00;
        memory[0x0802] = 0x06;
        memory[0x0600] = 0xC8;
        memory[0x0601] = 0x07;
        memory[0x0010] = 0x42;
        memory[0x0020] = 0x99;
        memory[0x0025] = 0x77;
        memory[0x0030] = 0x00;
        memory[0x0031] = 0x40;
        memory[0x4000] = 0x11;
        memory[0x0050] = 0xF0;
        memory[0x0051] = 0x42;
        memory[0x4310] = 0x22;
        memory[0x5000] = 0x33;
        memory[0x5005] = 0x44;
        memory[0x5103] = 0x55;
        memory[0x7005] = 0x10;
        memory[0x7103] = 0x20;
        memory[0x8005] = 0x40;
        memory[0x8103] = 0x81;
    }

    /// <summary>
    /// Sets X, Y, and the stack pointer to the values the loop's operands require.
    /// </summary>
    /// <param name="cpu">The CPU that will run the loop.</param>
    public static void Prepare(Cpu65C02 cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.Registers.X.SetByte(IndexX);
        cpu.Registers.Y.SetByte(IndexY);
        cpu.Registers.SP.SetByte(Stack);
        cpu.SetPC(EntryPoint);
    }

    private static void Emit(Span<byte> memory, ref ushort address, params ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            memory[address++] = bytes[i];
        }
    }
}