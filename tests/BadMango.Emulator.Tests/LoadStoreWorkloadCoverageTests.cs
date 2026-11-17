// <copyright file="LoadStoreWorkloadCoverageTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Benchmarks.Workloads;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Proves the load-store workload actually executes every opcode it claims to.
/// </summary>
[TestFixture]
public sealed class LoadStoreWorkloadCoverageTests
{
    /// <summary>
    /// Steps one loop on a bare 65C02 and requires every parity-test opcode to be fetched.
    /// </summary>
    [Test]
    public void OneLoop_ExecutesEveryListedOpcode()
    {
        byte[] expected = LoadStoreWorkload.OpcodeBytes();
        Assert.That(expected, Has.Length.EqualTo(LoadStoreWorkload.BodyLength));
        Assert.That(LoadStoreWorkload.ExercisedOpcodes.Length, Is.GreaterThan(expected.Length));

        var fixture = new BareCpuFixture();
        LoadStoreWorkload.Load(fixture.Memory);
        Cpu65C02 cpu = fixture.Cpu;
        LoadStoreWorkload.Prepare(cpu);

        Span<byte> memory = fixture.Memory;
        var executed = new HashSet<byte>();
        ulong startCycles = cpu.GetCycles();
        for (int i = 0; i < LoadStoreWorkload.InstructionsPerLoop; i++)
        {
            executed.Add(memory[(int)cpu.GetPC()]);
            CpuStepResult result = cpu.Step();
            Assert.That(result.State, Is.EqualTo(CpuRunState.Running), $"step {i} state");
        }

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo(LoadStoreWorkload.EntryPoint), "PC");
            Assert.That(cpu.Halted, Is.False, "halted");
            Assert.That(cpu.GetCycles() - startCycles, Is.EqualTo((ulong)LoadStoreWorkload.CyclesPerLoop), "cycles");
            Assert.That(executed, Does.Contain((byte)0x4C), "JMP");
            foreach (byte opcode in expected)
            {
                Assert.That(executed, Does.Contain(opcode), $"${opcode:X2}");
            }
        });
    }

    /// <summary>
    /// A straight fill of each converted opcode, the shape the per-opcode benchmark uses,
    /// steps without halting and without a store corrupting the next opcode byte.
    /// </summary>
    [Test]
    public void StraightOpcodeRun_StepsThePlantedOpcode()
    {
        foreach (DispatchOpcode opcode in LoadStoreWorkload.CreateOpcodes())
        {
            var fixture = new BareCpuFixture();
            fixture.FillOpcodeRun(0x0800, opcode.Code, 64);
            Cpu65C02 cpu = fixture.Cpu;
            cpu.HaltReason = HaltState.None;
            cpu.Registers.A.SetByte(0x42);
            cpu.Registers.X.SetByte(0x00);
            cpu.Registers.Y.SetByte(0x00);
            cpu.SetPC(0x0800);

            for (int step = 0; step < 8; step++)
            {
                byte fetched = fixture.Memory[(int)cpu.GetPC()];
                Assert.That(fetched, Is.EqualTo(opcode.Code), $"{opcode.Mnemonic} step {step}");
                CpuStepResult result = cpu.Step();
                Assert.That(result.State, Is.EqualTo(CpuRunState.Running), $"{opcode.Mnemonic} step {step} state");
            }
        }
    }
}