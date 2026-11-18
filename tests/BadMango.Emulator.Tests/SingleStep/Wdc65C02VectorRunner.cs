// <copyright file="Wdc65C02VectorRunner.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

using System.Globalization;
using System.Text;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Executes one <see cref="Cpu65C02"/> instruction per vector case and compares the
/// architectural result. Bus cycles inside <c>cycles</c> are counted, not traced.
/// </summary>
internal sealed class Wdc65C02VectorRunner
{
    private readonly PhysicalMemory memory;
    private readonly WriteRecordingRamTarget target;
    private readonly Cpu65C02 cpu;

    /// <summary>
    /// Initializes a new instance of the <see cref="Wdc65C02VectorRunner"/> class over 64KB of RAM.
    /// </summary>
    internal Wdc65C02VectorRunner()
    {
        var mainBus = new MainBus(16);
        memory = new PhysicalMemory(0x10000, "wdc65c02-vectors");
        target = new WriteRecordingRamTarget(memory.Slice(0, 0x10000));
        mainBus.MapPageRange(
            startPage: 0,
            pageCount: 16,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);
        var context = new EventContext(new Scheduler(), new SignalBus(), mainBus);
        cpu = new Cpu65C02(context);
    }

    /// <summary>Runs each committed case for <paramref name="opcode"/>.</summary>
    /// <param name="opcode">The opcode the cases belong to.</param>
    /// <param name="cases">The cases to run.</param>
    /// <returns>The case count and the first disagreement.</returns>
    internal Wdc65C02VectorRunResult ExecuteAll(byte opcode, ReadOnlySpan<Wdc65C02VectorCase> cases)
    {
        int failed = 0;
        string? example = null;
        for (int index = 0; index < cases.Length; index++)
        {
            string? mismatch = Execute(opcode, cases[index]);
            if (mismatch is not null)
            {
                failed++;
                example ??= mismatch;
            }
        }

        return new Wdc65C02VectorRunResult(cases.Length, failed, example);
    }

    /// <summary>Parses an upstream opcode file and runs each case.</summary>
    /// <param name="opcode">The opcode the file belongs to.</param>
    /// <param name="json">The upstream JSON array. An empty stream is an omitted opcode.</param>
    /// <returns>The case count and the first disagreement.</returns>
    internal Wdc65C02VectorRunResult ExecuteJson(byte opcode, Stream json)
    {
        if (json.Length == 0)
        {
            return new Wdc65C02VectorRunResult(0, 0, null);
        }

        int cases = 0;
        int failed = 0;
        string? example = null;
        using var document = JsonDocument.Parse(json);
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            Wdc65C02VectorCase test = ReadCase(element);
            cases++;
            string? mismatch = Execute(opcode, test);
            if (mismatch is not null)
            {
                failed++;
                example ??= mismatch;
            }
        }

        return new Wdc65C02VectorRunResult(cases, failed, example);
    }

    private static void Apply(Span<byte> ram, (ushort Address, byte Value)[] bytes)
    {
        foreach ((ushort address, byte value) in bytes)
        {
            ram[address] = value;
        }
    }

    private static void Clear(Span<byte> ram, (ushort Address, byte Value)[] bytes)
    {
        foreach ((ushort address, byte _) in bytes)
        {
            ram[address] = 0;
        }
    }

    private static bool TryByteAt((ushort Address, byte Value)[] bytes, ushort address, out byte value)
    {
        foreach ((ushort entryAddress, byte entryValue) in bytes)
        {
            if (entryAddress == address)
            {
                value = entryValue;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static Wdc65C02VectorCase ReadCase(JsonElement test)
    {
        JsonElement initial = test.GetProperty("initial");
        JsonElement final = test.GetProperty("final");
        string name = test.GetProperty("name").GetString() ?? string.Empty;
        return new Wdc65C02VectorCase(
            name,
            ReadUInt16(initial, "pc"),
            ReadByte(initial, "a"),
            ReadByte(initial, "x"),
            ReadByte(initial, "y"),
            ReadByte(initial, "s"),
            ReadByte(initial, "p"),
            ReadUInt16(final, "pc"),
            ReadByte(final, "a"),
            ReadByte(final, "x"),
            ReadByte(final, "y"),
            ReadByte(final, "s"),
            ReadByte(final, "p"),
            test.GetProperty("cycles").GetArrayLength(),
            ReadRam(initial),
            ReadRam(final));
    }

    private static (ushort Address, byte Value)[] ReadRam(JsonElement state)
    {
        JsonElement ram = state.GetProperty("ram");
        var bytes = new (ushort Address, byte Value)[ram.GetArrayLength()];
        int index = 0;
        foreach (JsonElement entry in ram.EnumerateArray())
        {
            int address = entry[0].GetInt32();
            int value = entry[1].GetInt32();
            if ((uint)address > 0xFFFF || (uint)value > 0xFF)
            {
                throw new InvalidDataException($"RAM entry [{address}, {value}] is out of range.");
            }

            bytes[index++] = ((ushort)address, (byte)value);
        }

        return bytes;
    }

    private static byte ReadByte(JsonElement state, string name)
    {
        int value = state.GetProperty(name).GetInt32();
        if ((uint)value > 0xFF)
        {
            throw new InvalidDataException($"{name} value {value} is outside a byte.");
        }

        return (byte)value;
    }

    private static ushort ReadUInt16(JsonElement state, string name)
    {
        int value = state.GetProperty(name).GetInt32();
        if ((uint)value > 0xFFFF)
        {
            throw new InvalidDataException($"{name} value {value} is outside a word.");
        }

        return (ushort)value;
    }

    private string? Execute(byte opcode, Wdc65C02VectorCase test)
    {
        Span<byte> ram = memory.AsSpan();
        if (!TryByteAt(test.InitialRam, test.Pc, out byte atPc) || atPc != opcode)
        {
            throw new InvalidDataException(
                $"Case {test.Name} does not place opcode ${opcode:X2} at ${test.Pc:X4}.");
        }

        Apply(ram, test.InitialRam);
        Load(test);
        target.ResetWrites();
        CpuStepResult result = cpu.Step();
        string? mismatch = Compare(test, ram, (int)result.CyclesConsumed.Value);
        Clear(ram, test.InitialRam);
        Clear(ram, test.FinalRam);
        foreach (ushort address in target.Writes)
        {
            ram[address] = 0;
        }

        return mismatch;
    }

    private void Load(Wdc65C02VectorCase test)
    {
        ref Registers regs = ref cpu.Registers;
        regs.E = true;
        regs.CP = true;
        regs.DBR = 0;
        regs.PBR = 0;
        regs.D.SetAddr(0);
        regs.A.SetDWord(test.A);
        regs.X.SetDWord(test.X);
        regs.Y.SetDWord(test.Y);
        regs.SP.SetDWord(test.S);
        regs.P = (ProcessorStatusFlags)test.P;
        regs.PC.SetAddr(test.Pc);
        cpu.HaltReason = HaltState.None;
    }

    private string? Compare(Wdc65C02VectorCase test, ReadOnlySpan<byte> ram, int cycles)
    {
        ref Registers regs = ref cpu.Registers;
        ushort pc = regs.PC.GetWord();
        byte a = regs.A.GetByte();
        byte x = regs.X.GetByte();
        byte y = regs.Y.GetByte();
        byte s = regs.SP.GetByte();
        byte p = (byte)regs.P;
        bool registersMatch = pc == test.FinalPc
            && a == test.FinalA
            && x == test.FinalX
            && y == test.FinalY
            && s == test.FinalS
            && p == test.FinalP;
        int ramMismatches = 0;
        ushort ramAddress = 0;
        byte ramActual = 0;
        byte ramExpected = 0;
        foreach ((ushort address, byte value) in test.FinalRam)
        {
            if (ram[address] != value)
            {
                if (ramMismatches == 0)
                {
                    ramAddress = address;
                    ramActual = ram[address];
                    ramExpected = value;
                }

                ramMismatches++;
            }
        }

        int strayWrites = 0;
        ushort strayAddress = 0;
        foreach (ushort address in target.Writes)
        {
            if (!TryByteAt(test.FinalRam, address, out _))
            {
                if (strayWrites == 0)
                {
                    strayAddress = address;
                }

                strayWrites++;
            }
        }

        if (registersMatch && ramMismatches == 0 && strayWrites == 0 && cycles == test.Cycles)
        {
            return null;
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{test.Name}:");
        if (cycles != test.Cycles)
        {
            text.Append(CultureInfo.InvariantCulture, $" cycles {cycles} != {test.Cycles};");
        }

        if (!registersMatch)
        {
            text.Append(CultureInfo.InvariantCulture, $" PC ${pc:X4}/${test.FinalPc:X4} A ${a:X2}/${test.FinalA:X2} X ${x:X2}/${test.FinalX:X2} Y ${y:X2}/${test.FinalY:X2} S ${s:X2}/${test.FinalS:X2} P ${p:X2}/${test.FinalP:X2};");
        }

        if (ramMismatches > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" ram ${ramAddress:X4}=${ramActual:X2} expected ${ramExpected:X2} ({ramMismatches} byte(s));");
        }

        if (strayWrites > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" unexpected write to ${strayAddress:X4} ({strayWrites} write(s) outside final ram);");
        }

        return text.ToString();
    }
}