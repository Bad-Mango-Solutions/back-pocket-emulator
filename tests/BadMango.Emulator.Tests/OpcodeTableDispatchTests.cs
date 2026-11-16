// <copyright file="OpcodeTableDispatchTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;

using TestHelpers;

/// <summary>
/// Dispatch tests for the hybrid <see cref="OpcodeTable"/> entry and trampoline.
/// </summary>
[TestFixture]
public class OpcodeTableDispatchTests : CpuTestBase
{
    /// <summary>
    /// Every opcode slot invokes its own legacy handler through <see cref="OpcodeTable.Execute"/>.
    /// </summary>
    [Test]
    public void Execute_RoutesEachOpcodeToItsOwnLegacyHandler()
    {
        var hits = new int[256];
        var handlers = new OpcodeHandler[256];
        var metadata = new OpcodeInfo[256];
        for (int opcode = 0; opcode < 256; opcode++)
        {
            int captured = opcode;
            handlers[opcode] = _ => hits[captured]++;
            metadata[opcode] = new(CpuInstructions.NOP, CpuAddressingModes.Implied, 0, (byte)opcode);
        }

        var table = new OpcodeTable(handlers, metadata);
        for (int opcode = 0; opcode < 256; opcode++)
        {
            table.Execute((byte)opcode, Cpu);
        }

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                Assert.That(hits[opcode], Is.EqualTo(1), $"${opcode:X2} ran {hits[opcode]} times");
                Assert.That(table.GetHandler((byte)opcode), Is.SameAs(handlers[opcode]), $"${opcode:X2} handler");
                Assert.That(table.GetMetadata((byte)opcode).BaseCycles, Is.EqualTo((byte)opcode), $"${opcode:X2} metadata");
            }
        });
    }

    /// <summary>
    /// Two tables on one thread, including a handler that re-enters the other table,
    /// each run only their own legacy handler.
    /// </summary>
    [Test]
    public void Execute_InterleavedTablesOnOneThread_DoNotCrossTalk()
    {
        var order = new List<string>();
        var metadata = new OpcodeInfo[256];
        var handlersA = new OpcodeHandler[256];
        var handlersB = new OpcodeHandler[256];
        OpcodeTable? tableB = null;

        handlersA[0x10] = cpu =>
        {
            order.Add("A10-enter");
            tableB!.Execute(0x20, cpu);
            order.Add("A10-exit");
        };
        handlersA[0x11] = _ => order.Add("A11");
        handlersB[0x20] = _ => order.Add("B20");
        handlersB[0x21] = _ => order.Add("B21");

        var tableA = new OpcodeTable(handlersA, metadata);
        tableB = new OpcodeTable(handlersB, metadata);

        tableA.Execute(0x10, Cpu);
        tableB.Execute(0x21, Cpu);
        tableA.Execute(0x11, Cpu);
        tableB.Execute(0x20, Cpu);

        Assert.That(order, Is.EqualTo(new[] { "A10-enter", "B20", "A10-exit", "B21", "A11", "B20" }));
    }

    /// <summary>
    /// Two CPUs on one thread, stepped alternately, keep their own registers and memory.
    /// </summary>
    [Test]
    public void Execute_InterleavedMachinesOnOneThread_DoNotCrossTalk()
    {
        var cpuA = CreateCpu();
        var cpuB = CreateCpu();

        // NOP; NOP; JMP $0200. A stays 0 if this machine's handlers run.
        LoadProgram(cpuA, 0xEA, 0xEA, 0x4C, 0x00, 0x02);

        // LDA #$42; JMP $0200.
        LoadProgram(cpuB, 0xA9, 0x42, 0x4C, 0x00, 0x02);

        for (int step = 0; step < 6; step++)
        {
            cpuA.Step();
            cpuB.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(cpuA.Registers.A.GetByte(), Is.EqualTo((byte)0));
            Assert.That(cpuA.Registers.PC.GetWord(), Is.EqualTo((Word)0x0200));
            Assert.That(cpuA.Read8(0x0201), Is.EqualTo((byte)0xEA));
            Assert.That(cpuB.Registers.A.GetByte(), Is.EqualTo((byte)0x42));
            Assert.That(cpuB.Registers.PC.GetWord(), Is.EqualTo((Word)0x0200));
            Assert.That(cpuB.Read8(0x0201), Is.EqualTo((byte)0x42));
        });
    }

    /// <summary>
    /// A null legacy handler still throws <see cref="NullReferenceException"/> from <see cref="OpcodeTable.Execute"/>.
    /// </summary>
    [Test]
    public void Execute_NullHandler_ThrowsNullReferenceException()
    {
        var handlers = new OpcodeHandler[256];
        bool neighborRan = false;
        handlers[0x01] = _ => neighborRan = true;
        var table = new OpcodeTable(handlers, new OpcodeInfo[256]);

        Assert.Throws<NullReferenceException>(() => table.Execute(0x00, Cpu));
        table.Execute(0x01, Cpu);
        Assert.Throws<NullReferenceException>(() => table.Execute(0x00, Cpu));

        Assert.Multiple(() =>
        {
            Assert.That(neighborRan, Is.True);
            Assert.That(table.GetHandler(0x00), Is.Null);
            Assert.That(table.GetHandler(0x01), Is.SameAs(handlers[0x01]));
        });
    }

    /// <summary>
    /// Flags and transfer opcodes use direct dispatch and do not require legacy delegates.
    /// </summary>
    [Test]
    public void Build_FlagsAndTransferOpcodes_UseDirectDispatchWithNullLegacyHandlers()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();

        byte[] convertedOpcodes =
        [
            0x18, 0x38, 0x58, 0x78, 0xB8, 0xD8, 0xF8,
            0xAA, 0xA8, 0x8A, 0x98, 0x9A, 0xBA,
        ];

        foreach (byte opcode in convertedOpcodes)
        {
            Assert.That(table.GetHandler(opcode), Is.Null, $"${opcode:X2} expected null legacy handler");
        }

        var cpu = CreateCpu();
        cpu.Registers.P |= ProcessorStatusFlags.C;
        cpu.Registers.PC.SetWord(0x0200);
        cpu.Write8(0x0200, 0x18); // CLC
        cpu.Step();

        Assert.That((cpu.Registers.P & ProcessorStatusFlags.C) == ProcessorStatusFlags.C, Is.False);
    }

    /// <summary>
    /// Builds a 64KB RAM machine with its own opcode table.
    /// </summary>
    /// <returns>A resettable 65C02 wired only to RAM.</returns>
    private static Cpu65C02 CreateCpu()
    {
        var bus = new MainBus(16);
        var memory = new PhysicalMemory(0x10000, "dispatch-ram");
        var target = new RamTarget(memory.Slice(0, 0x10000));
        bus.MapPageRange(
            startPage: 0,
            pageCount: 16,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);
        return new Cpu65C02(new EventContext(new Scheduler(), new SignalBus(), bus));
    }

    /// <summary>
    /// Writes <paramref name="program"/> at <c>$0200</c> and resets through a vector that points there.
    /// </summary>
    /// <param name="cpu">The CPU whose RAM receives the program.</param>
    /// <param name="program">The bytes to place at <c>$0200</c>.</param>
    private static void LoadProgram(Cpu65C02 cpu, params byte[] program)
    {
        cpu.Write8(0xFFFC, 0x00);
        cpu.Write8(0xFFFD, 0x02);
        for (int i = 0; i < program.Length; i++)
        {
            cpu.Write8((Addr)(0x0200 + i), program[i]);
        }

        cpu.Reset();
    }
}