// <copyright file="DevicePageLoopBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Bus;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Emulation.Cpu;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Effective-megahertz run of the device-page loop on a Pocket2e with a language card and
/// an 80-column card.
/// </summary>
/// <remarks>
/// The measured region is the loop. Setup builds the machine and plants test bytes outside
/// the timed invocation. Counted megahertz is
/// <c>cycles per instruction / nanoseconds per instruction * 1000</c>.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Bus", "DevicePage")]
public class DevicePageLoopBenchmark
{
    private DevicePageFixture fixture = null!;

    private Cpu65C02 cpu = null!;

    /// <summary>
    /// Builds the machine, loads the loop, and checks that one iteration returns to the entry point.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        fixture = new DevicePageFixture();
        DevicePageWorkload.Load(fixture);
        DevicePageWorkload.Prepare(fixture.Cpu);
        WorkloadLoop.RequireClosed(
            fixture.Cpu,
            DevicePageWorkload.EntryPoint,
            DevicePageWorkload.InstructionsPerLoop,
            DevicePageWorkload.CyclesPerLoop);
        cpu = fixture.Cpu;
    }

    /// <summary>
    /// Steps the device-page loop.
    /// </summary>
    [Benchmark(OperationsPerInvoke = DevicePageWorkload.StepsPerMeasurement)]
    public void Loop()
    {
        for (int i = 0; i < DevicePageWorkload.StepsPerMeasurement; i++)
        {
            cpu.Step();
        }
    }
}