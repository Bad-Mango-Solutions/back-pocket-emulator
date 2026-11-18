// <copyright file="OpcodeTableDispatchTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

using TestHelpers;

/// <summary>
/// Dispatch tests for <see cref="OpcodeTable"/> entries.
/// </summary>
[TestFixture]
public class OpcodeTableDispatchTests : CpuTestBase
{
    private static readonly OpcodeDispatch UnusedDispatch = static (ICpu cpu, in OpcodeEntry entry) => { };

    /// <summary>
    /// Every opcode slot invokes its own dispatch delegate through <see cref="OpcodeTable.Execute"/>.
    /// </summary>
    [Test]
    public void Execute_RoutesEachOpcodeToItsOwnDispatch()
    {
        var hits = new int[256];
        var dispatch = new OpcodeDispatch[256];
        var metadata = new OpcodeInfo[256];
        for (int opcode = 0; opcode < 256; opcode++)
        {
            int captured = opcode;
            dispatch[opcode] = (ICpu cpu, in OpcodeEntry entry) => hits[captured]++;
            metadata[opcode] = new(CpuInstructions.NOP, CpuAddressingModes.Implied, 0, (byte)opcode);
        }

        var table = new OpcodeTable(dispatch, metadata);
        for (int opcode = 0; opcode < 256; opcode++)
        {
            table.Execute((byte)opcode, Cpu);
        }

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                Assert.That(hits[opcode], Is.EqualTo(1), $"${opcode:X2} ran {hits[opcode]} times");
                Assert.That(table.GetDispatch((byte)opcode), Is.SameAs(dispatch[opcode]), $"${opcode:X2} dispatch");
                Assert.That(table.GetMetadata((byte)opcode).BaseCycles, Is.EqualTo((byte)opcode), $"${opcode:X2} metadata");
            }
        });
    }

    /// <summary>
    /// Two tables on one thread, including a handler that re-enters the other table,
    /// each run only their own dispatch delegate.
    /// </summary>
    [Test]
    public void Execute_InterleavedTablesOnOneThread_DoNotCrossTalk()
    {
        var order = new List<string>();
        var metadata = new OpcodeInfo[256];
        var handlersA = FilledDispatch();
        var handlersB = FilledDispatch();
        OpcodeTable? tableB = null;

        handlersA[0x10] = (ICpu cpu, in OpcodeEntry entry) =>
        {
            order.Add("A10-enter");
            tableB!.Execute(0x20, cpu);
            order.Add("A10-exit");
        };
        handlersA[0x11] = (ICpu cpu, in OpcodeEntry entry) => order.Add("A11");
        handlersB[0x20] = (ICpu cpu, in OpcodeEntry entry) => order.Add("B20");
        handlersB[0x21] = (ICpu cpu, in OpcodeEntry entry) => order.Add("B21");

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
    /// A null dispatch slot is rejected when the table is built.
    /// </summary>
    [Test]
    public void Constructor_NullDispatchSlot_ThrowsArgumentException()
    {
        var dispatch = FilledDispatch();
        dispatch[0x00] = null!;

        var error = Assert.Throws<ArgumentException>(() => new OpcodeTable(dispatch, new OpcodeInfo[256]));

        Assert.That(error!.ParamName, Is.EqualTo("dispatchTargets"));
    }

    /// <summary>
    /// Flags and transfer opcodes each have a dispatch delegate.
    /// </summary>
    [Test]
    public void Build_FlagsAndTransferOpcodes_UseDirectDispatch()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();

        byte[] convertedOpcodes =
        [
            0x18, 0x38, 0x58, 0x78, 0xB8, 0xD8, 0xF8,
            0xAA, 0xA8, 0x8A, 0x98, 0x9A, 0xBA,
        ];

        foreach (byte opcode in convertedOpcodes)
        {
            Assert.That(table.GetDispatch(opcode), Is.Not.Null, $"${opcode:X2} dispatch");
        }

        var cpu = CreateCpu();
        cpu.Registers.E = true;
        cpu.Registers.CP = true;
        cpu.Registers.P |= ProcessorStatusFlags.C;
        cpu.Registers.A.SetByte(0x80);
        cpu.Registers.X.SetByte(0x00);
        cpu.Registers.PC.SetWord(0x0200);
        cpu.Write8(0x0200, 0x18); // CLC
        cpu.Write8(0x0201, 0xAA); // TAX
        cpu.Step();
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That((cpu.Registers.P & ProcessorStatusFlags.C) == ProcessorStatusFlags.C, Is.False);
            Assert.That(cpu.Registers.X.GetByte(), Is.EqualTo((byte)0x80));
            Assert.That((cpu.Registers.P & ProcessorStatusFlags.N) == ProcessorStatusFlags.N, Is.True);
            Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo((Word)0x0202));
        });
    }

    /// <summary>
    /// Logical, arithmetic, and compare opcodes each have a dispatch delegate.
    /// </summary>
    [Test]
    public void Build_LogicalArithmeticAndCompareOpcodes_UseDirectDispatch()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        int converted = 0;

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                CpuInstructions instruction = table.GetMetadata((byte)opcode).Instruction;
                if (!IsLogicalArithmeticOrCompare(instruction))
                {
                    continue;
                }

                converted++;
                Assert.That(
                    table.GetDispatch((byte)opcode),
                    Is.Not.Null,
                    $"${opcode:X2} {instruction} dispatch");
            }

            Assert.That(converted, Is.EqualTo(79), "converted opcode count");
        });
    }

    /// <summary>
    /// Shift and rotate opcodes each have a dispatch delegate.
    /// </summary>
    [Test]
    public void Build_ShiftOpcodes_UseDirectDispatch()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        int converted = 0;

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                CpuInstructions instruction = table.GetMetadata((byte)opcode).Instruction;
                if (!IsShift(instruction))
                {
                    continue;
                }

                converted++;
                Assert.That(
                    table.GetDispatch((byte)opcode),
                    Is.Not.Null,
                    $"${opcode:X2} {instruction} dispatch");
            }

            Assert.That(converted, Is.EqualTo(20), "converted opcode count");
        });
    }

    /// <summary>
    /// Branch, jump, and stack opcodes each have a dispatch delegate.
    /// </summary>
    [Test]
    public void Build_BranchJumpAndStackOpcodes_UseDirectDispatch()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        int converted = 0;

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                CpuInstructions instruction = table.GetMetadata((byte)opcode).Instruction;
                if (!IsBranchJumpOrStack(instruction))
                {
                    continue;
                }

                converted++;
                Assert.That(
                    table.GetDispatch((byte)opcode),
                    Is.Not.Null,
                    $"${opcode:X2} {instruction} dispatch");
            }

            Assert.That(converted, Is.EqualTo(23), "converted opcode count");
        });
    }

    /// <summary>
    /// Bit and remaining 65C02-only opcodes each have a dispatch delegate.
    /// </summary>
    [Test]
    public void Build_BitAnd65C02Opcodes_UseDirectDispatch()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        int converted = 0;

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                CpuInstructions instruction = table.GetMetadata((byte)opcode).Instruction;
                if (!IsBitOr65C02Only(instruction))
                {
                    continue;
                }

                converted++;
                Assert.That(
                    table.GetDispatch((byte)opcode),
                    Is.Not.Null,
                    $"${opcode:X2} {instruction} dispatch");
            }

            Assert.That(converted, Is.EqualTo(38), "converted opcode count");
        });
    }

    /// <summary>
    /// Every 65C02 slot has a dispatch delegate, and none remain on the illegal default.
    /// Opcode $42 uses its own handler, separate from the other immediate NOPs.
    /// </summary>
    [Test]
    public void Build_EveryOpcode_HasDirectDispatch_AndOpcode42IsSeparate()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        OpcodeDispatch wdm = table.GetDispatch(0x42);

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                OpcodeDispatch dispatch = table.GetDispatch((byte)opcode);
                Assert.That(dispatch, Is.Not.Null, $"${opcode:X2} dispatch");
                Assert.That(dispatch.Method.Name, Is.Not.EqualTo("DispatchIllegalOpcode"), $"${opcode:X2} illegal default");
            }

            Assert.That(table.GetDispatch(0x00).Method.Name, Is.EqualTo("DispatchBRKImplied"));
            Assert.That(table.GetDispatch(0xEA).Method.Name, Is.EqualTo("DispatchNopImplied"));
            Assert.That(wdm.Method.Name, Is.EqualTo("DispatchWdm"));
            Assert.That(table.GetMetadata(0x42).Instruction, Is.EqualTo(CpuInstructions.NOP));
            Assert.That(table.GetMetadata(0x42).AddressingMode, Is.EqualTo(CpuAddressingModes.Immediate));
            Assert.That(table.GetMetadata(0x42).BaseCycles, Is.EqualTo((byte)2));
            Assert.That(table.GetMetadata(0x42).OperandLength, Is.EqualTo((byte)1));

            foreach (byte opcode in new byte[] { 0x02, 0x22, 0x62, 0x82, 0xC2, 0xE2, 0xEA })
            {
                Assert.That(wdm, Is.Not.SameAs(table.GetDispatch(opcode)), $"$42 shares the ${opcode:X2} delegate");
                Assert.That(wdm.Method, Is.Not.EqualTo(table.GetDispatch(opcode).Method), $"$42 shares the ${opcode:X2} method");
            }
        });
    }

    /// <summary>
    /// Reports whether an instruction belongs to the logical, arithmetic, or compare families.
    /// </summary>
    /// <param name="instruction">The opcode mnemonic.</param>
    /// <returns><see langword="true"/> when the mnemonic is AND, ORA, EOR, BIT, ADC, SBC, INC, DEC, INA, DEA, INX, INY, DEX, DEY, CMP, CPX, or CPY.</returns>
    private static bool IsLogicalArithmeticOrCompare(CpuInstructions instruction)
    {
        return instruction is CpuInstructions.AND
            or CpuInstructions.ORA
            or CpuInstructions.EOR
            or CpuInstructions.BIT
            or CpuInstructions.ADC
            or CpuInstructions.SBC
            or CpuInstructions.INC
            or CpuInstructions.DEC
            or CpuInstructions.INA
            or CpuInstructions.DEA
            or CpuInstructions.INX
            or CpuInstructions.INY
            or CpuInstructions.DEX
            or CpuInstructions.DEY
            or CpuInstructions.CMP
            or CpuInstructions.CPX
            or CpuInstructions.CPY;
    }

    /// <summary>
    /// Reports whether an instruction belongs to the shift and rotate family.
    /// </summary>
    /// <param name="instruction">The opcode mnemonic.</param>
    /// <returns><see langword="true"/> when the mnemonic is ASL, LSR, ROL, or ROR.</returns>
    private static bool IsShift(CpuInstructions instruction)
    {
        return instruction is CpuInstructions.ASL
            or CpuInstructions.LSR
            or CpuInstructions.ROL
            or CpuInstructions.ROR;
    }

    /// <summary>
    /// Reports whether an instruction belongs to the branch, jump, or stack families.
    /// </summary>
    /// <param name="instruction">The opcode mnemonic.</param>
    /// <returns><see langword="true"/> when the mnemonic is a relative branch, JMP, JSR, RTS, RTI, or a push or pull.</returns>
    private static bool IsBranchJumpOrStack(CpuInstructions instruction)
    {
        return instruction is CpuInstructions.BCC
            or CpuInstructions.BCS
            or CpuInstructions.BEQ
            or CpuInstructions.BNE
            or CpuInstructions.BMI
            or CpuInstructions.BPL
            or CpuInstructions.BVC
            or CpuInstructions.BVS
            or CpuInstructions.BRA
            or CpuInstructions.JMP
            or CpuInstructions.JSR
            or CpuInstructions.RTS
            or CpuInstructions.RTI
            or CpuInstructions.PHA
            or CpuInstructions.PHP
            or CpuInstructions.PLA
            or CpuInstructions.PLP
            or CpuInstructions.PHX
            or CpuInstructions.PLX
            or CpuInstructions.PHY
            or CpuInstructions.PLY;
    }

    /// <summary>
    /// Reports whether an instruction belongs to the bit or remaining 65C02-only families.
    /// </summary>
    /// <param name="instruction">The opcode mnemonic.</param>
    /// <returns><see langword="true"/> when the mnemonic is TSB, TRB, WAI, STP, RMB, SMB, BBR, or BBS.</returns>
    private static bool IsBitOr65C02Only(CpuInstructions instruction)
    {
        return instruction is CpuInstructions.TSB
            or CpuInstructions.TRB
            or CpuInstructions.WAI
            or CpuInstructions.STP
            or CpuInstructions.RMB
            or CpuInstructions.SMB
            or CpuInstructions.BBR
            or CpuInstructions.BBS;
    }

    /// <summary>
    /// Fills a 256-slot dispatch array with a delegate that does nothing.
    /// </summary>
    /// <returns>An array the table constructor will accept.</returns>
    private static OpcodeDispatch[] FilledDispatch()
    {
        var dispatch = new OpcodeDispatch[256];
        for (int opcode = 0; opcode < dispatch.Length; opcode++)
        {
            dispatch[opcode] = UnusedDispatch;
        }

        return dispatch;
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