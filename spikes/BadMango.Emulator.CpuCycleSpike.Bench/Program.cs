// <copyright file="Program.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.CpuCycleSpike.Bench;

using System.Diagnostics;
using System.Globalization;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// Release stopwatch comparison of the shipping 65C02 and the cycle-step spike
/// on the same instruction mix and the same RAM bus.
/// </summary>
internal static class Program
{
    private const int DefaultLoops = 100_000;
    private const int WarmupLoops = 20_000;
    private const int Trials = 7;

    /// <summary>
    /// Runs the benchmark and writes the same text to stdout and the artifact log.
    /// </summary>
    private static int Main(string[] args)
    {
        int loops = args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0 ? parsed : DefaultLoops;
        string report = Run(loops);
        Console.WriteLine(report);
        string artifactDirectory = "/opt/cursor/artifacts";
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(Path.Combine(artifactDirectory, "cpu-cycle-spike-bench.txt"), report);
        return 0;
    }

    private static string Run(int loops)
    {
        int instructions = checked(loops * SpikeInstructionMix.InstructionsPerLoop);
        int warmupInstructions = checked(WarmupLoops * SpikeInstructionMix.InstructionsPerLoop);
        var spikeRig = new SpikeMemoryRig();
        var currentRig = new SpikeMemoryRig();
        var spike = new CycleStepCpu(spikeRig.Bus, spikeRig.Scheduler, spikeRig.Signals);
        var current = new Cpu65C02(currentRig.Context);

        Prepare(spikeRig, spike);
        PrepareCurrent(currentRig, current);
        long spikeLoopCycles = 0;
        long currentLoopCycles = 0;
        for (int i = 0; i < SpikeInstructionMix.InstructionsPerLoop; i++)
        {
            spikeLoopCycles += spike.Step();
            currentLoopCycles += (long)current.Step().CyclesConsumed.Value;
        }

        _ = MeasureSpike(spikeRig, spike, warmupInstructions);
        _ = MeasureCurrent(currentRig, current, warmupInstructions);

        var spikeSamples = new double[Trials];
        var currentSamples = new double[Trials];
        var spikeCycles = new long[Trials];
        var currentCycles = new long[Trials];
        for (int trial = 0; trial < Trials; trial++)
        {
            bool spikeFirst = (trial & 1) == 0;
            if (spikeFirst)
            {
                (spikeSamples[trial], spikeCycles[trial]) = MeasureSpike(spikeRig, spike, instructions);
                (currentSamples[trial], currentCycles[trial]) = MeasureCurrent(currentRig, current, instructions);
            }
            else
            {
                (currentSamples[trial], currentCycles[trial]) = MeasureCurrent(currentRig, current, instructions);
                (spikeSamples[trial], spikeCycles[trial]) = MeasureSpike(spikeRig, spike, instructions);
            }
        }

        long spikeAllocated = AllocatedBy(() => MeasureSpike(spikeRig, spike, instructions));
        long currentAllocated = AllocatedBy(() => MeasureCurrent(currentRig, current, instructions));

        double spikeSeconds = Median(spikeSamples);
        double currentSeconds = Median(currentSamples);
        long spikeCycleCount = Median(spikeCycles);
        long currentCycleCount = Median(currentCycles);
        var culture = CultureInfo.InvariantCulture;
        return string.Join(
            Environment.NewLine,
            "cpu-cycle-step spike benchmark",
            FormattableString.Invariant($"runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}"),
            FormattableString.Invariant($"loops {loops} instructions {instructions} warmupLoops {WarmupLoops} trials {Trials}"),
            FormattableString.Invariant($"oneLoopCycles spike {spikeLoopCycles} current {currentLoopCycles}"),
            FormattableString.Invariant($"spike seconds median {spikeSeconds.ToString("F6", culture)} min {spikeSamples.Min().ToString("F6", culture)} max {spikeSamples.Max().ToString("F6", culture)}"),
            FormattableString.Invariant($"current seconds median {currentSeconds.ToString("F6", culture)} min {currentSamples.Min().ToString("F6", culture)} max {currentSamples.Max().ToString("F6", culture)}"),
            FormattableString.Invariant($"spike instructions/sec {instructions / spikeSeconds:F0}"),
            FormattableString.Invariant($"current instructions/sec {instructions / currentSeconds:F0}"),
            FormattableString.Invariant($"spike counted MHz {spikeCycleCount / spikeSeconds / 1_000_000d:F3} cycles {spikeCycleCount}"),
            FormattableString.Invariant($"current counted MHz {currentCycleCount / currentSeconds / 1_000_000d:F3} cycles {currentCycleCount}"),
            FormattableString.Invariant($"hardware-equivalent MHz uses one-loop spike cycles {spikeLoopCycles} times loops"),
            FormattableString.Invariant($"spike hardware MHz {(double)spikeLoopCycles * loops / spikeSeconds / 1_000_000d:F3}"),
            FormattableString.Invariant($"current hardware MHz {(double)spikeLoopCycles * loops / currentSeconds / 1_000_000d:F3}"),
            FormattableString.Invariant($"allocated bytes spike {spikeAllocated} current {currentAllocated}"),
            FormattableString.Invariant($"ratio current/spike instructions per sec {(instructions / currentSeconds) / (instructions / spikeSeconds):F3}"),
            string.Empty);
    }

    private static (double Seconds, long Cycles) MeasureSpike(SpikeMemoryRig rig, CycleStepCpu cpu, int instructions)
    {
        Prepare(rig, cpu);
        long start = Stopwatch.GetTimestamp();
        long cycles = 0;
        for (int i = 0; i < instructions; i++)
        {
            cycles += cpu.Step();
        }

        double seconds = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
        return (seconds, cycles);
    }

    private static (double Seconds, long Cycles) MeasureCurrent(SpikeMemoryRig rig, Cpu65C02 cpu, int instructions)
    {
        PrepareCurrent(rig, cpu);
        long start = Stopwatch.GetTimestamp();
        long cycles = 0;
        for (int i = 0; i < instructions; i++)
        {
            cycles += (long)cpu.Step().CyclesConsumed.Value;
        }

        double seconds = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
        return (seconds, cycles);
    }

    private static long AllocatedBy(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Prepare(SpikeMemoryRig rig, CycleStepCpu cpu)
    {
        rig.Clear();
        SpikeInstructionMix.Load(rig.Ram);
        cpu.Registers = new CycleStepRegisters(
            SpikeInstructionMix.Entry,
            0,
            SpikeInstructionMix.IndexX,
            SpikeInstructionMix.IndexY,
            SpikeInstructionMix.Stack,
            0);
    }

    private static void PrepareCurrent(SpikeMemoryRig rig, Cpu65C02 cpu)
    {
        rig.Clear();
        SpikeInstructionMix.Load(rig.Ram);
        ref Registers regs = ref cpu.Registers;
        regs.E = true;
        regs.CP = true;
        regs.DBR = 0;
        regs.PBR = 0;
        regs.D.SetWord(0);
        regs.A.SetByte(0);
        regs.X.SetByte(SpikeInstructionMix.IndexX);
        regs.Y.SetByte(SpikeInstructionMix.IndexY);
        regs.SP.SetByte(SpikeInstructionMix.Stack);
        regs.P = 0;
        regs.PC.SetWord(SpikeInstructionMix.Entry);
        cpu.HaltReason = HaltState.None;
    }

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static long Median(long[] values)
    {
        var sorted = (long[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}