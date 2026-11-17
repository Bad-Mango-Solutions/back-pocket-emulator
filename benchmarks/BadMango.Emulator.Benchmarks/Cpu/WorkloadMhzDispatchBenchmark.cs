// <copyright file="WorkloadMhzDispatchBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Cpu;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Emulation.Cpu;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Effective-megahertz runs of the mix, RAM, and flags-transfer loops.
/// </summary>
/// <remarks>
/// Each method steps a fixed instruction count on a bare 65C02. Counted megahertz is
/// <c>cycles per instruction / nanoseconds per instruction * 1000</c>. The reported
/// time is per instruction because <see cref="BenchmarkAttribute.OperationsPerInvoke"/>
/// is the step count.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Cpu", "Dispatch")]
public class WorkloadMhzDispatchBenchmark
{
    private Cpu65C02 mixCpu = null!;
    private Cpu65C02 ramCpu = null!;
    private Cpu65C02 flagsCpu = null!;

    /// <summary>
    /// Loads each workload onto its own bare CPU and checks one loop.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        mixCpu = StartMix();
        ramCpu = StartRam();
        flagsCpu = StartFlagsTransfer();
    }

    /// <summary>
    /// Steps the general instruction mix.
    /// </summary>
    [Benchmark(OperationsPerInvoke = MixWorkload.StepsPerMeasurement)]
    public void Mix()
    {
        Step(mixCpu, MixWorkload.StepsPerMeasurement);
    }

    /// <summary>
    /// Steps the absolute-jump RAM loop.
    /// </summary>
    [Benchmark(OperationsPerInvoke = RamWorkload.StepsPerMeasurement)]
    public void Ram()
    {
        Step(ramCpu, RamWorkload.StepsPerMeasurement);
    }

    /// <summary>
    /// Steps the flag and transfer loop.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FlagsTransferWorkload.StepsPerMeasurement)]
    public void FlagsTransfer()
    {
        Step(flagsCpu, FlagsTransferWorkload.StepsPerMeasurement);
    }

    private static Cpu65C02 StartMix()
    {
        var fixture = new BareCpuFixture();
        MixWorkload.Load(fixture.Memory);
        MixWorkload.Prepare(fixture.Cpu);
        WorkloadLoop.RequireClosed(fixture.Cpu, MixWorkload.EntryPoint, MixWorkload.InstructionsPerLoop, MixWorkload.CyclesPerLoop);
        return fixture.Cpu;
    }

    private static Cpu65C02 StartRam()
    {
        var fixture = new BareCpuFixture();
        RamWorkload.Load(fixture.Memory);
        RamWorkload.Prepare(fixture.Cpu);
        WorkloadLoop.RequireClosed(fixture.Cpu, RamWorkload.EntryPoint, RamWorkload.InstructionsPerLoop, RamWorkload.CyclesPerLoop);
        return fixture.Cpu;
    }

    private static Cpu65C02 StartFlagsTransfer()
    {
        var fixture = new BareCpuFixture();
        FlagsTransferWorkload.Load(fixture.Memory);
        FlagsTransferWorkload.Prepare(fixture.Cpu);
        WorkloadLoop.RequireClosed(
            fixture.Cpu,
            FlagsTransferWorkload.EntryPoint,
            FlagsTransferWorkload.InstructionsPerLoop,
            FlagsTransferWorkload.CyclesPerLoop);
        return fixture.Cpu;
    }

    private static void Step(Cpu65C02 cpu, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            cpu.Step();
        }
    }
}