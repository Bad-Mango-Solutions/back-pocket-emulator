// <copyright file="RamWorkload.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// The RAM loop used beside the instruction mix: <c>JMP $0200</c> at <c>$0200</c>.
/// </summary>
/// <remarks>
/// The jump is one instruction and 3 counted cycles. It hammers opcode fetch and the
/// absolute jump path without touching the flag or transfer opcodes.
/// </remarks>
public static class RamWorkload
{
    /// <summary>The workload name.</summary>
    public const string Name = "ram";

    /// <summary>The CPU family. <c>JMP</c> absolute is a 6502 instruction.</summary>
    public const string CpuFamily = "6502";

    /// <summary>The address of the jump, which is also its target.</summary>
    public const Addr EntryPoint = 0x0200;

    /// <summary>Instructions in one loop.</summary>
    public const int InstructionsPerLoop = 1;

    /// <summary>Counted cycles in one loop.</summary>
    public const int CyclesPerLoop = 3;

    /// <summary>Jumps executed by one benchmark invocation.</summary>
    public const int LoopsPerMeasurement = 200_000;

    /// <summary>Instructions executed by one benchmark invocation.</summary>
    public const int StepsPerMeasurement = LoopsPerMeasurement;

    /// <summary>
    /// Writes <c>JMP $0200</c> into a 64 KB image.
    /// </summary>
    /// <param name="memory">The RAM image. Must be at least 64 KB.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="memory"/> is shorter than 64 KB.</exception>
    public static void Load(Span<byte> memory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(memory.Length, 0x10000);
        int address = (int)EntryPoint;
        memory[address] = 0x4C;
        memory[address + 1] = (byte)(EntryPoint & 0xFF);
        memory[address + 2] = (byte)((EntryPoint >> 8) & 0xFF);
    }

    /// <summary>
    /// Clears a halt and points the CPU at the jump.
    /// </summary>
    /// <param name="cpu">The CPU that will run the loop.</param>
    public static void Prepare(Cpu65C02 cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.SetPC(EntryPoint);
    }
}