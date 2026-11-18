// <copyright file="CycleStepVectorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.CpuCycleSpike.Tests;

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// Checks the cycle-step spike against a trimmed SingleStepTests 65C02 slice,
/// and records where the shipping core disagrees on final state or cycle count.
/// </summary>
[TestFixture]
public sealed class CycleStepVectorTests
{
    private const string VectorResourceName = "wdc65c02-slice.json";

    private static readonly string[] CoveredOpcodes =
    [
        "a9", "a5", "b5", "ad", "bd", "9d", "9e", "fe", "1e", "b1", "b2", "6c", "f0", "20", "60", "48", "68",
    ];

    private SpikeMemoryRig rig = null!;
    private CycleStepCpu spike = null!;
    private Cpu65C02 current = null!;
    private List<VectorCase> cases = null!;

    /// <summary>
    /// Builds one RAM machine and loads the committed vector slice.
    /// </summary>
    [OneTimeSetUp]
    public void LoadVectors()
    {
        rig = new SpikeMemoryRig();
        spike = new CycleStepCpu(rig.Bus, rig.Scheduler, rig.Signals);
        current = new Cpu65C02(rig.Context);
        cases = LoadSlice();
    }

    /// <summary>
    /// The committed slice covers every opcode the spike claims to implement.
    /// </summary>
    [Test]
    public void Slice_CoversTheSpikeOpcodes()
    {
        var present = cases.Select(item => item.Opcode).Distinct().OrderBy(item => item).ToArray();
        Assert.That(present, Is.EqualTo(CoveredOpcodes.OrderBy(item => item).ToArray()));
        Assert.That(cases, Has.Count.GreaterThan(100));
        Assert.That(CycleStepCpu.Covers(0xEA), Is.False);
        Assert.That(CycleStepCpu.Covers(0xA9), Is.True);
    }

    /// <summary>
    /// Each trimmed vector matches the spike bus trace and the final architectural state.
    /// </summary>
    [Test]
    public void Spike_MatchesCycleAndFinalState()
    {
        var failures = new List<string>();
        foreach (var vector in cases)
        {
            string? failure = RunSpike(vector);
            if (failure is not null)
            {
                failures.Add(failure);
                if (failures.Count == 12)
                {
                    break;
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Runs the same vectors on the shipping core and prints final-state and cycle mismatches.
    /// Mismatches do not fail the test; the spike records them and does not change the core.
    /// </summary>
    [Test]
    public void CurrentCore_ReportsFinalStateAndCycleMismatches()
    {
        var summary = SummarizeCurrent(cases);
        TestContext.Out.WriteLine(summary.Text);
        Assert.That(summary.Cases, Is.EqualTo(cases.Count));
        Assert.That(summary.Text, Does.Contain("opcode"));
    }

    /// <summary>
    /// One trip around the shared instruction mix leaves both cores in the same architectural state.
    /// </summary>
    [Test]
    public void InstructionMix_AgreesOnRegistersAndMemory()
    {
        var spikeRig = new SpikeMemoryRig();
        var currentRig = new SpikeMemoryRig();
        SpikeInstructionMix.Load(spikeRig.Ram);
        SpikeInstructionMix.Load(currentRig.Ram);
        var spikeCpu = new CycleStepCpu(spikeRig.Bus, spikeRig.Scheduler, spikeRig.Signals);
        var currentCpu = new Cpu65C02(currentRig.Context);
        LoadSpike(spikeCpu);
        LoadCurrent(currentCpu);

        long spikeCycles = 0;
        long currentCycles = 0;
        for (int i = 0; i < SpikeInstructionMix.InstructionsPerLoop; i++)
        {
            spikeCycles += spikeCpu.Step();
            currentCycles += (long)currentCpu.Step().CyclesConsumed.Value;
        }

        CycleStepRegisters spikeState = spikeCpu.Registers;
        ref Registers regs = ref currentCpu.Registers;
        ushort currentPc = regs.PC.GetWord();
        byte currentA = regs.A.GetByte();
        byte currentX = regs.X.GetByte();
        byte currentY = regs.Y.GetByte();
        byte currentS = regs.SP.GetByte();
        byte currentP = (byte)regs.P;
        byte[] currentRam = currentRig.Ram.ToArray();
        byte[] spikeRam = spikeRig.Ram.ToArray();
        TestContext.Out.WriteLine($"mix cycles spike={spikeCycles} current={currentCycles}");
        Assert.Multiple(() =>
        {
            Assert.That(spikeState.PC, Is.EqualTo(SpikeInstructionMix.Entry));
            Assert.That(currentPc, Is.EqualTo(SpikeInstructionMix.Entry));
            Assert.That(currentA, Is.EqualTo(spikeState.A));
            Assert.That(currentX, Is.EqualTo(spikeState.X));
            Assert.That(currentY, Is.EqualTo(spikeState.Y));
            Assert.That(currentS, Is.EqualTo(spikeState.S));
            Assert.That(currentP, Is.EqualTo(spikeState.P));
            Assert.That(currentRam, Is.EqualTo(spikeRam));
        });
    }

    /// <summary>
    /// Runs the full upstream JSON files when <c>CYCLE_SPIKE_FULL_DIR</c> points at them.
    /// </summary>
    [Test]
    [Explicit("Local full SingleStepTests run. Set CYCLE_SPIKE_FULL_DIR to the opcode JSON directory.")]
    public void FullSet_WhenLocalVectorsPresent()
    {
        string? directory = Environment.GetEnvironmentVariable("CYCLE_SPIKE_FULL_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Ignore("CYCLE_SPIKE_FULL_DIR is not set.");
        }

        var full = new List<VectorCase>();
        foreach (string opcode in CoveredOpcodes)
        {
            string path = Path.Combine(directory, opcode + ".json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var test in document.RootElement.EnumerateArray())
            {
                full.Add(ParseCase(opcode, test));
            }
        }

        var spikeFailures = new List<string>();
        foreach (var vector in full)
        {
            string? failure = RunSpike(vector);
            if (failure is not null)
            {
                spikeFailures.Add(failure);
                if (spikeFailures.Count == 12)
                {
                    break;
                }
            }
        }

        var summary = SummarizeCurrent(full);
        TestContext.Out.WriteLine(summary.Text);
        TestContext.Out.WriteLine($"full cases={full.Count} spikeFailures={spikeFailures.Count}");
        Assert.That(spikeFailures, Is.Empty, string.Join(Environment.NewLine, spikeFailures));
    }

    private string? RunSpike(VectorCase vector)
    {
        LoadRam(vector.InitialRam);
        spike.Registers = new CycleStepRegisters(vector.Pc, vector.A, vector.X, vector.Y, vector.S, vector.P);
        var trace = new ObservedCycle[16];
        int count;
        try
        {
            count = spike.Execute(trace);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return $"{vector.Opcode} {vector.Name}: threw {ex.GetType().Name}: {ex.Message}";
        }

        CycleStepRegisters state = spike.Registers;
        if (count != vector.Cycles.Length)
        {
            return $"{vector.Opcode} {vector.Name}: cycles {count} != {vector.Cycles.Length}";
        }

        for (int i = 0; i < count; i++)
        {
            var expected = vector.Cycles[i];
            ObservedCycle actual = trace[i];
            if (actual.Address != expected.Address || actual.Data != expected.Data || actual.IsWrite != expected.IsWrite)
            {
                return $"{vector.Opcode} {vector.Name}: cycle {i} got {(actual.IsWrite ? "write" : "read")} ${actual.Address:X4}=${actual.Data:X2} expected {(expected.IsWrite ? "write" : "read")} ${expected.Address:X4}=${expected.Data:X2}";
            }
        }

        if (state.PC != vector.FinalPc || state.A != vector.FinalA || state.X != vector.FinalX || state.Y != vector.FinalY || state.S != vector.FinalS || state.P != vector.FinalP)
        {
            return $"{vector.Opcode} {vector.Name}: state PC=${state.PC:X4} A=${state.A:X2} X=${state.X:X2} Y=${state.Y:X2} S=${state.S:X2} P=${state.P:X2}";
        }

        var ram = rig.Ram;
        foreach (var (address, value) in vector.FinalRam)
        {
            if (ram[address] != value)
            {
                return $"{vector.Opcode} {vector.Name}: ram ${address:X4} = ${ram[address]:X2} expected ${value:X2}";
            }
        }

        return null;
    }

    private MismatchSummary SummarizeCurrent(List<VectorCase> vectors)
    {
        var byOpcode = new Dictionary<string, OpcodeTally>(StringComparer.Ordinal);
        foreach (string opcode in CoveredOpcodes)
        {
            byOpcode[opcode] = new OpcodeTally();
        }

        int executed = 0;
        foreach (var vector in vectors)
        {
            executed++;
            LoadRam(vector.InitialRam);
            LoadCurrent(vector);
            var result = current.Step();
            int cycles = (int)result.CyclesConsumed.Value;
            ref Registers regs = ref current.Registers;
            bool stateMismatch = regs.PC.GetWord() != vector.FinalPc
                || regs.A.GetByte() != vector.FinalA
                || regs.X.GetByte() != vector.FinalX
                || regs.Y.GetByte() != vector.FinalY
                || regs.SP.GetByte() != vector.FinalS
                || (byte)regs.P != vector.FinalP;
            if (!stateMismatch)
            {
                var ram = rig.Ram;
                foreach (var (address, value) in vector.FinalRam)
                {
                    if (ram[address] != value)
                    {
                        stateMismatch = true;
                        break;
                    }
                }
            }

            bool cycleMismatch = cycles != vector.Cycles.Length;
            OpcodeTally tally = byOpcode[vector.Opcode];
            tally.Cases++;
            if (stateMismatch)
            {
                tally.StateMismatches++;
            }

            if (cycleMismatch)
            {
                tally.CycleMismatches++;
                if (tally.Example is null)
                {
                    tally.Example = $"{vector.Name}: counted {cycles}, vector {vector.Cycles.Length}";
                }
            }
        }

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"current-core cases={executed}");
        foreach (string opcode in CoveredOpcodes)
        {
            OpcodeTally tally = byOpcode[opcode];
            text.AppendLine(CultureInfo.InvariantCulture, $"opcode {opcode} cases={tally.Cases} state={tally.StateMismatches} cycles={tally.CycleMismatches} {tally.Example}");
        }

        return new MismatchSummary(executed, text.ToString());
    }

    private void LoadRam((ushort Address, byte Value)[] bytes)
    {
        rig.Clear();
        var ram = rig.Ram;
        foreach (var (address, value) in bytes)
        {
            ram[address] = value;
        }
    }

    private void LoadCurrent(VectorCase vector)
    {
        ref Registers regs = ref current.Registers;
        regs.E = true;
        regs.CP = true;
        regs.DBR = 0;
        regs.PBR = 0;
        regs.D.SetWord(0);
        regs.A.SetByte(vector.A);
        regs.X.SetByte(vector.X);
        regs.Y.SetByte(vector.Y);
        regs.SP.SetByte(vector.S);
        regs.P = (ProcessorStatusFlags)vector.P;
        regs.PC.SetWord(vector.Pc);
        current.HaltReason = HaltState.None;
    }

    private void LoadSpike(CycleStepCpu cpu)
    {
        cpu.Registers = new CycleStepRegisters(SpikeInstructionMix.Entry, 0, SpikeInstructionMix.IndexX, SpikeInstructionMix.IndexY, SpikeInstructionMix.Stack, 0);
    }

    private void LoadCurrent(Cpu65C02 cpu)
    {
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

    private List<VectorCase> LoadSlice()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(VectorResourceName);
        Assert.That(stream, Is.Not.Null);
        using var document = JsonDocument.Parse(stream!);
        var list = new List<VectorCase>();
        foreach (var opcode in document.RootElement.GetProperty("opcodes").EnumerateObject())
        {
            foreach (var test in opcode.Value.EnumerateArray())
            {
                list.Add(ParseCase(opcode.Name, test));
            }
        }

        return list;
    }

    private VectorCase ParseCase(string opcode, JsonElement test)
    {
        var initial = test.GetProperty("initial");
        var final = test.GetProperty("final");
        var cycles = test.GetProperty("cycles");
        var parsedCycles = new (ushort Address, byte Data, bool IsWrite)[cycles.GetArrayLength()];
        int index = 0;
        foreach (var cycle in cycles.EnumerateArray())
        {
            parsedCycles[index++] = (
                (ushort)cycle[0].GetInt32(),
                (byte)cycle[1].GetInt32(),
                cycle[2].GetString() == "write");
        }

        return new VectorCase(
            opcode,
            test.GetProperty("name").GetString() ?? opcode,
            (ushort)initial.GetProperty("pc").GetInt32(),
            (byte)initial.GetProperty("a").GetInt32(),
            (byte)initial.GetProperty("x").GetInt32(),
            (byte)initial.GetProperty("y").GetInt32(),
            (byte)initial.GetProperty("s").GetInt32(),
            (byte)initial.GetProperty("p").GetInt32(),
            ReadRam(initial.GetProperty("ram")),
            (ushort)final.GetProperty("pc").GetInt32(),
            (byte)final.GetProperty("a").GetInt32(),
            (byte)final.GetProperty("x").GetInt32(),
            (byte)final.GetProperty("y").GetInt32(),
            (byte)final.GetProperty("s").GetInt32(),
            (byte)final.GetProperty("p").GetInt32(),
            ReadRam(final.GetProperty("ram")),
            parsedCycles);
    }

    private (ushort Address, byte Value)[] ReadRam(JsonElement ram)
    {
        var bytes = new (ushort Address, byte Value)[ram.GetArrayLength()];
        int index = 0;
        foreach (var pair in ram.EnumerateArray())
        {
            bytes[index++] = ((ushort)pair[0].GetInt32(), (byte)pair[1].GetInt32());
        }

        return bytes;
    }

    private sealed record VectorCase(
        string Opcode,
        string Name,
        ushort Pc,
        byte A,
        byte X,
        byte Y,
        byte S,
        byte P,
        (ushort Address, byte Value)[] InitialRam,
        ushort FinalPc,
        byte FinalA,
        byte FinalX,
        byte FinalY,
        byte FinalS,
        byte FinalP,
        (ushort Address, byte Value)[] FinalRam,
        (ushort Address, byte Data, bool IsWrite)[] Cycles);

    private sealed class MismatchSummary
    {
        public MismatchSummary(int cases, string text)
        {
            Cases = cases;
            Text = text;
        }

        public int Cases { get; }

        public string Text { get; }
    }

    private sealed class OpcodeTally
    {
        public int Cases { get; set; }

        public int StateMismatches { get; set; }

        public int CycleMismatches { get; set; }

        public string? Example { get; set; }
    }
}