// <copyright file="WorkloadLoop.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Harness;

using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Checks that a workload loop returns to its entry point in the documented cycle count.
/// </summary>
internal static class WorkloadLoop
{
    /// <summary>
    /// Steps one loop and throws when the program counter, cycle count, or halt state is wrong.
    /// </summary>
    /// <param name="cpu">The CPU, already prepared at <paramref name="entry"/>.</param>
    /// <param name="entry">The address the loop must return to.</param>
    /// <param name="instructions">The number of instructions in one loop.</param>
    /// <param name="cycles">The number of counted cycles in one loop.</param>
    /// <exception cref="InvalidOperationException">Thrown when the loop does not match the documented shape.</exception>
    public static void RequireClosed(Cpu65C02 cpu, Addr entry, int instructions, int cycles)
    {
        cpu.SetPC(entry);
        ulong start = cpu.GetCycles();
        for (int step = 0; step < instructions; step++)
        {
            cpu.Step();
        }

        ulong elapsed = cpu.GetCycles() - start;
        Addr pc = cpu.GetPC();
        if (pc != entry || elapsed != (ulong)cycles || cpu.Halted)
        {
            throw new InvalidOperationException(
                $"Loop at ${entry:X4} ended at ${pc:X4} after {elapsed} cycles in {instructions} instructions (expected {cycles} cycles). Halted={cpu.Halted}.");
        }
    }
}