// <copyright file="DebugCommandsTests.Json.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Text.Json;

/// <content>
/// Golden JSON envelopes for the session-backed commands.
/// </content>
public partial class DebugCommandsTests
{
    private static readonly DateTimeOffset GoldenTimestamp = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// <c>regs --json</c> is the register envelope.
    /// </summary>
    [Test]
    public void RegsCommand_Json_MatchesGoldenEnvelope()
    {
        AssertGolden(new RegsCommand(null, JsonRenderer()), [], """
{
  "schema": "emudbg/v1",
  "payload": {
    "cpuType": "Cpu65C02",
    "programCounter": 4096,
    "accumulator": 0,
    "xRegister": 0,
    "yRegister": 0,
    "stackPointer": 255,
    "statusRegister": 52,
    "directPage": 0,
    "dataBank": 0,
    "extraBank": 0
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>mem</c> JSON is a memory-block envelope.
    /// </summary>
    [Test]
    public void MemCommand_Json_MatchesGoldenEnvelope()
    {
        WriteByte(bus, 0x0200, 0x41);
        WriteByte(bus, 0x0201, 0x42);
        AssertGolden(new MemCommand(null, JsonRenderer()), ["$0200", "2"], """
{
  "schema": "emudbg/v1",
  "payload": {
    "address": 512,
    "length": 2,
    "data": "4142"
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>peek</c> JSON is a memory-block envelope.
    /// </summary>
    [Test]
    public void PeekCommand_Json_MatchesGoldenEnvelope()
    {
        WriteByte(bus, 0x1234, 0xAB);
        AssertGolden(new PeekCommand(null, JsonRenderer()), ["$1234"], """
{
  "schema": "emudbg/v1",
  "payload": {
    "address": 4660,
    "length": 1,
    "data": "AB"
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>dasm</c> JSON is a disassembly envelope.
    /// </summary>
    [Test]
    public void DasmCommand_Json_MatchesGoldenEnvelope()
    {
        WriteByte(bus, 0x1000, 0xEA);
        AssertGolden(new DasmCommand(null, JsonRenderer()), ["$1000", "--instructions=1"], """
{
  "schema": "emudbg/v1",
  "payload": [
    {
      "address": 4096,
      "mnemonic": "NOP",
      "operand": "",
      "size": 1,
      "bytes": "EA"
    }
  ],
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>step</c> JSON is a step receipt.
    /// </summary>
    [Test]
    public void StepCommand_Json_MatchesGoldenEnvelope()
    {
        WriteByte(bus, 0x1000, 0xEA);
        cpu.Reset();
        AssertGolden(new StepCommand(null, JsonRenderer()), [], """
{
  "schema": "emudbg/v1",
  "payload": {
    "instructionsExecuted": 1,
    "programCounter": 4097
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>bp add</c> JSON is one breakpoint envelope.
    /// </summary>
    [Test]
    public void BreakCommand_Json_MatchesGoldenEnvelope()
    {
        AssertGolden(new BreakCommand(null, JsonRenderer()), ["add", "$1000", "entry"], """
{
  "schema": "emudbg/v1",
  "payload": {
    "address": 4096,
    "label": "entry",
    "enabled": true,
    "hitCount": 0
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>watch add</c> JSON is one watchpoint envelope.
    /// </summary>
    [Test]
    public void WatchCommand_Json_MatchesGoldenEnvelope()
    {
        AssertGolden(new WatchCommand(null, JsonRenderer()), ["add", "$2000", "r"], """
{
  "schema": "emudbg/v1",
  "payload": {
    "address": 8192,
    "access": "Read",
    "stopOnHit": false,
    "enabled": true,
    "hitCount": 0
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>reset</c> JSON is a reset receipt.
    /// </summary>
    [Test]
    public void ResetCommand_Json_MatchesGoldenEnvelope()
    {
        cpu.SetPC(0x5000);
        AssertGolden(new ResetCommand(null, JsonRenderer()), [], """
{
  "schema": "emudbg/v1",
  "payload": {
    "kind": "Warm",
    "programCounter": 4096
  },
  "timestamp": "2020-01-01T00:00:00+00:00"
}
""");
    }

    /// <summary>
    /// <c>run-until</c> JSON uses the run-result envelope. Elapsed time is not stable, so the
    /// golden check covers the schema and the stop fields.
    /// </summary>
    [Test]
    public void RunUntilCommand_Json_UsesRunResultEnvelope()
    {
        WriteByte(bus, 0x1000, 0xEA);
        WriteByte(bus, 0x1001, 0xDB);
        cpu.Reset();

        var machine = new BadMango.Emulator.Bus.MachineBuilder()
            .WithAddressSpace(16)
            .WithCpu(BadMango.Emulator.Core.Cpu.CpuFamily.Cpu65C02)
            .WithCpuFactory(ctx => new BadMango.Emulator.Emulation.Cpu.Cpu65C02(ctx))
            .MapRegion(0x0000, 64 * 1024, new BadMango.Emulator.Bus.RamTarget(new BadMango.Emulator.Bus.PhysicalMemory(64 * 1024, "json-ram").Slice(0, 64 * 1024)), BadMango.Emulator.Bus.RegionTag.Ram, BadMango.Emulator.Bus.PagePerms.All)
            .Build();
        WriteByte(machine.Bus, 0x1000, 0xEA);
        WriteByte(machine.Bus, 0x1001, 0xDB);
        WriteWord(machine.Bus, 0xFFFC, 0x1000);
        machine.Cpu.Reset();
        debugContext.AttachMachine(machine);

        var result = new RunUntilCommand(null, JsonRenderer()).Execute(debugContext, ["--until-cycles=1", "--max-cycles=100"]);
        Assert.That(result.Success, Is.True, result.Message);
        using JsonDocument json = JsonDocument.Parse(outputWriter.ToString());
        JsonElement root = json.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(root.GetProperty("payload").GetProperty("stopReason").GetString(), Is.EqualTo("CycleCount"));
            Assert.That(root.GetProperty("payload").GetProperty("instructionCount").GetUInt64(), Is.EqualTo(1UL));
        });
    }

    private static DebugOutputRenderer JsonRenderer() =>
        new(new EmudbgOptions(JsonOutput: true), () => GoldenTimestamp);

    private void AssertGolden(BadMango.Emulator.Debug.Infrastructure.Commands.ICommandHandler command, string[] args, string expected)
    {
        var result = command.Execute(debugContext, args);
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(outputWriter.ToString().Trim(), Is.EqualTo(expected.Trim()));
    }
}