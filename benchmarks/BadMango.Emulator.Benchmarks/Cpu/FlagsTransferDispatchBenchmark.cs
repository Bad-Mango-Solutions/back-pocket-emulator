// <copyright file="FlagsTransferDispatchBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Cpu;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Times one flag or transfer opcode through <see cref="Cpu65C02.Step"/>.
/// </summary>
/// <remarks>
/// The opcode is written into RAM and fetched by the CPU. The benchmark does not call
/// dispatch tables or handler delegates, so the same source runs before and after a
/// dispatch change.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Cpu", "Dispatch")]
public class FlagsTransferDispatchBenchmark
{
    /// <summary>How many times <see cref="Execute"/> runs the selected opcode.</summary>
    public const int OperationsPerInvoke = 2048;

    private const Addr RunAddress = 0x0800;

    private Cpu65C02 cpu = null!;

    /// <summary>
    /// Gets or sets the opcode measured by this invocation.
    /// </summary>
    [ParamsSource(nameof(Opcodes))]
    public DispatchOpcode Opcode { get; set; } = null!;

    /// <summary>
    /// Gets the flag and transfer opcodes covered by the parity tests.
    /// </summary>
    public IEnumerable<DispatchOpcode> Opcodes => FlagsTransferWorkload.CreateOpcodes();

    /// <summary>
    /// Builds a bare CPU and fills a straight run of <see cref="Opcode"/>.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = new BareCpuFixture();
        fixture.FillOpcodeRun(RunAddress, Opcode.Code, OperationsPerInvoke);
        cpu = fixture.Cpu;
        cpu.HaltReason = HaltState.None;
        cpu.ClearStopRequest();
        cpu.Registers.A.SetByte(FlagsTransferWorkload.Seed);
        cpu.Registers.X.SetByte(FlagsTransferWorkload.Seed);
        cpu.Registers.Y.SetByte(FlagsTransferWorkload.Seed);
        cpu.Registers.SP.SetByte(FlagsTransferWorkload.Seed);
        cpu.Registers.P = ProcessorStatusFlags.R | ProcessorStatusFlags.I | ProcessorStatusFlags.V;
    }

    /// <summary>
    /// Steps the selected opcode <see cref="OperationsPerInvoke"/> times.
    /// </summary>
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public void Execute()
    {
        // BenchmarkDotNet calls this method more than once per iteration.
        // IterationSetup does not run between those calls, so the program counter
        // has to be rewound here or the next call walks off the opcode run.
        Cpu65C02 cpu = this.cpu;
        cpu.SetPC(RunAddress);
        for (int i = 0; i < OperationsPerInvoke; i++)
        {
            cpu.Step();
        }
    }
}