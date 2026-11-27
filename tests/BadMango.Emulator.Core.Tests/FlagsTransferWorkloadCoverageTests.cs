// <copyright file="FlagsTransferWorkloadCoverageTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Proves the flags-transfer workload actually executes every opcode it claims to.
/// </summary>
[TestFixture]
public sealed class FlagsTransferWorkloadCoverageTests
{
    /// <summary>
    /// Steps one loop on a bare 65C02 and requires every parity-test opcode to be fetched.
    /// </summary>
    [Test]
    public void OneLoop_ExecutesEveryListedOpcode()
    {
        byte[] expected =
        [
            0x18, 0x38, 0x58, 0x78, 0xB8, 0xD8, 0xF8,
            0xAA, 0x8A, 0xA8, 0x98, 0x9A, 0xBA,
        ];

        var fixture = new BareCpuFixture();
        FlagsTransferWorkload.Load(fixture.Memory);
        Cpu65C02 cpu = fixture.Cpu;
        FlagsTransferWorkload.Prepare(cpu);

        Assert.That(FlagsTransferWorkload.ExercisedOpcodes.ToArray(), Is.EqualTo(expected));

        Span<byte> memory = fixture.Memory;
        var executed = new HashSet<byte>();
        ulong startCycles = cpu.GetCycles();
        for (int i = 0; i < FlagsTransferWorkload.InstructionsPerLoop; i++)
        {
            executed.Add(memory[(int)cpu.GetPC()]);
            CpuStepResult result = cpu.Step();
            Assert.That(result.State, Is.EqualTo(CpuRunState.Running), $"step {i} state");
        }

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(FlagsTransferWorkload.EntryPoint), "PC");
            Assert.That(cpu.Halted, Is.False, "halted");
            Assert.That(cpu.GetCycles() - startCycles, Is.EqualTo((ulong)FlagsTransferWorkload.CyclesPerLoop), "cycles");
            Assert.That(executed, Does.Contain((byte)0x4C), "JMP");
            foreach (byte opcode in expected)
            {
                Assert.That(executed, Does.Contain(opcode), $"${opcode:X2}");
            }
        });
    }
}