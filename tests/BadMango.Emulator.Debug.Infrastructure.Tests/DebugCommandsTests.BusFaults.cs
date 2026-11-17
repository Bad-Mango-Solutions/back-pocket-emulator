// <copyright file="DebugCommandsTests.BusFaults.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Infrastructure.Commands;

/// <content>
/// Unit tests for bus fault surfacing in debug commands.
/// </content>
/// <remarks>
/// These tests verify that debug commands properly report bus faults when
/// accessing unmapped memory or memory with permission violations.
/// </remarks>
public partial class DebugCommandsTests
{
    /// <summary>
    /// Verifies that MemCommand shows faults for unmapped addresses.
    /// </summary>
    [Test]
    public void MemCommand_UnmappedAddress_ShowsFaultInOutput()
    {
        // Create a bus with only partial mapping (pages 0-7 mapped, 8-15 unmapped)
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new MemCommand();
        var result = command.Execute(context, ["$8000", "16"]); // Page 8 is unmapped

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("??"), "Should show ?? for unmapped bytes");
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report faults");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    /// <summary>
    /// Verifies that PeekCommand shows faults for unmapped addresses.
    /// </summary>
    [Test]
    public void PeekCommand_UnmappedAddress_ShowsFaultInOutput()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new PeekCommand();
        var result = command.Execute(context, ["$8000"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("??"), "Should show ?? for unmapped address");
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report fault");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    /// <summary>
    /// Verifies that PeekCommand shows faults for multiple unmapped addresses.
    /// </summary>
    [Test]
    public void PeekCommand_MultipleUnmappedAddresses_ShowsAllFaults()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new PeekCommand();
        var result = command.Execute(context, ["$8000", "4"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("?? ?? ?? ??"), "Should show ?? for all unmapped bytes");
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered (4)"), "Should report 4 faults");
        });
    }

    /// <summary>
    /// Verifies that ReadCommand shows faults for unmapped addresses.
    /// </summary>
    [Test]
    public void ReadCommand_UnmappedAddress_ShowsFaultInOutput()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new ReadCommand();
        var result = command.Execute(context, ["$8000", "2"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("??"), "Should show ?? for unmapped bytes");
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report faults");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    /// <summary>
    /// Verifies that WriteCommand shows faults for unmapped addresses.
    /// </summary>
    [Test]
    public void WriteCommand_UnmappedAddress_ShowsFaultInOutput()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new WriteCommand();
        var result = command.Execute(context, ["$8000", "AB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report fault");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    /// <summary>
    /// Verifies that PokeCommand shows faults for unmapped addresses.
    /// </summary>
    [Test]
    public void PokeCommand_UnmappedAddress_ShowsFaultInOutput()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new PokeCommand();
        var result = command.Execute(context, ["$8000", "AB", "CD"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report faults");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    /// <summary>
    /// Verifies that MemCommand shows no faults for fully mapped addresses.
    /// </summary>
    [Test]
    public void MemCommand_MappedAddress_ShowsNoFaults()
    {
        var command = new MemCommand();
        var result = command.Execute(debugContext, ["$0200", "16"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("??"), "Should not show ?? for mapped bytes");
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "Should not report faults");
        });
    }

    /// <summary>
    /// Verifies that PeekCommand shows no faults for mapped addresses.
    /// </summary>
    [Test]
    public void PeekCommand_MappedAddress_ShowsNoFaults()
    {
        var command = new PeekCommand();
        var result = command.Execute(debugContext, ["$0200", "4"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("??"), "Should not show ?? for mapped bytes");
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "Should not report faults");
        });
    }

    /// <summary>
    /// Verifies that WriteCommand shows no faults for mapped addresses.
    /// </summary>
    [Test]
    public void WriteCommand_MappedAddress_ShowsNoFaults()
    {
        var command = new WriteCommand();
        var result = command.Execute(debugContext, ["$0200", "AB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "Should not report faults");
        });
    }

    /// <summary>
    /// Verifies that PokeCommand shows no faults for mapped addresses.
    /// </summary>
    [Test]
    public void PokeCommand_MappedAddress_ShowsNoFaults()
    {
        var command = new PokeCommand();
        var result = command.Execute(debugContext, ["$0200", "AB"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "Should not report faults");
        });
    }

    /// <summary>
    /// Verifies that ReadCommand shows no faults for mapped addresses.
    /// </summary>
    [Test]
    public void ReadCommand_MappedAddress_ShowsNoFaults()
    {
        var command = new ReadCommand();
        var result = command.Execute(debugContext, ["$0200"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("??"), "Should not show ?? for mapped bytes");
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "Should not report faults");
        });
    }

    /// <summary>
    /// Verifies that MemCommand shows correct ASCII representation for faulted bytes.
    /// </summary>
    [Test]
    public void MemCommand_UnmappedAddress_ShowsQuestionMarkInAsciiColumn()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new MemCommand();
        var result = command.Execute(context, ["$8000", "16"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("|????????????????|"), "Should show ? in ASCII column for faulted bytes");
        });
    }

    /// <summary>
    /// Verifies that MemCommand reports the correct fault address.
    /// </summary>
    [Test]
    public void MemCommand_UnmappedAddress_ReportsFaultAddress()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new MemCommand();
        var result = command.Execute(context, ["$8000", "1"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("$8000:"), "Should report the fault address");
        });
    }

    /// <summary>
    /// A dump that starts in mapped RAM and crosses into a hole keeps the readable bytes and marks the rest.
    /// </summary>
    [Test]
    public void MemCommand_SpanMappedAndUnmapped_ShowsBoth()
    {
        var partialBus = PartialBus();
        WriteByte(partialBus, 0x7FFE, 0x41);
        WriteByte(partialBus, 0x7FFF, 0x42);
        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var result = new MemCommand().Execute(context, ["$7FFE", "4"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(outputWriter.ToString(), Does.Contain("41 42 ?? ??"));
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"));
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered (2)"));
        });
    }

    /// <summary>
    /// JSON for a spanning dump keeps readable bytes and a parallel fault mask.
    /// </summary>
    [Test]
    public void MemCommand_SpanMappedAndUnmapped_JsonMask()
    {
        var partialBus = PartialBus();
        WriteByte(partialBus, 0x7FFE, 0x41);
        WriteByte(partialBus, 0x7FFF, 0x42);
        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var result = new MemCommand(null, JsonRenderer()).Execute(context, ["$7FFE", "4"]);

        Assert.That(result.Success, Is.True, result.Message);
        using JsonDocument json = JsonDocument.Parse(outputWriter.ToString());
        JsonElement payload = json.RootElement.GetProperty("payload");
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(payload.GetProperty("data").GetString(), Is.EqualTo("41420000"));
            Assert.That(payload.GetProperty("faults").GetString(), Is.EqualTo("00000101"));
        });
    }

    /// <summary>
    /// Mixed known and unknown fault bits are preserved in the formatted output.
    /// </summary>
    [Test]
    public void FormatFault_PreservesKnownAndUnknownBits()
    {
        BusFault fault = new(
            Kind: (FaultKind)0x1002,
            Address: 0x1234,
            WidthBits: 8,
            Intent: AccessIntent.DataRead,
            Mode: BusAccessMode.Decomposed,
            SourceId: 0,
            DeviceId: 0,
            RegionTag: RegionTag.Ram,
            Cycle: 0);

        string rendered = DebugOutputRenderer.FormatFault(fault);

        Assert.Multiple(() =>
        {
            Assert.That(rendered, Does.Contain("Permission"));
            Assert.That(rendered, Does.Contain("unknown 0x1000"));
        });
    }

    /// <summary>
    /// Unknown-only masks are rendered without dropping the raw bits.
    /// </summary>
    [Test]
    public void FormatFault_UnknownOnlyMaskIsPreserved()
    {
        BusFault fault = new(
            Kind: (FaultKind)0x1000,
            Address: 0x1234,
            WidthBits: 8,
            Intent: AccessIntent.DataRead,
            Mode: BusAccessMode.Decomposed,
            SourceId: 0,
            DeviceId: 0,
            RegionTag: RegionTag.Ram,
            Cycle: 0);

        string rendered = DebugOutputRenderer.FormatFault(fault);

        Assert.That(rendered, Is.EqualTo("unknown 0x1000"));
    }

    /// <summary>
    /// High-byte flag names are rendered alongside low-byte names when present.
    /// </summary>
    [Test]
    public void FormatFault_RendersKnownHighByteBits()
    {
        BusFault fault = new(
            Kind: FaultKind.Permission | FaultKind.Write,
            Address: 0x1234,
            WidthBits: 8,
            Intent: AccessIntent.DataRead,
            Mode: BusAccessMode.Decomposed,
            SourceId: 0,
            DeviceId: 0,
            RegionTag: RegionTag.Ram,
            Cycle: 0);

        string rendered = DebugOutputRenderer.FormatFault(fault);

        Assert.That(rendered, Is.EqualTo("Permission|Write"));
    }

    /// <summary>
    /// Peek across the same boundary marks only the unmapped bytes.
    /// </summary>
    [Test]
    public void PeekCommand_SpanMappedAndUnmapped_ShowsBoth()
    {
        var partialBus = PartialBus();
        WriteByte(partialBus, 0x7FFF, 0xAB);
        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var result = new PeekCommand().Execute(context, ["$7FFF", "2"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(outputWriter.ToString(), Does.Contain("AB ??"));
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"));
        });
    }

    /// <summary>
    /// Verifies that WriteCommand reports permission faults for read-only regions.
    /// </summary>
    [Test]
    public void WriteCommand_ReadOnlyRegion_ShowsPermissionFault()
    {
        // Create a bus with a read-only region
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "test-memory");
        var ramTarget = new RamTarget(physical.Slice(0, 0x4000));
        var romTarget = new RomTarget(physical.Slice(0x4000, 0x4000));

        // Pages 0-3: RAM (read/write)
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 4,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: ramTarget.Capabilities,
            target: ramTarget,
            physicalBase: 0);

        // Pages 4-7: ROM (read-only)
        partialBus.MapPageRange(
            startPage: 4,
            pageCount: 4,
            deviceId: 1,
            regionTag: RegionTag.Rom,
            perms: PagePerms.ReadExecute,
            caps: romTarget.Capabilities,
            target: romTarget,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new WriteCommand();
        var result = command.Execute(context, ["$4000", "AB"]); // Write to ROM region

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("Bus faults encountered"), "Should report fault");
            Assert.That(outputWriter.ToString(), Does.Contain("Permission"), "Should identify as permission fault");
        });
    }

    /// <summary>
    /// Verifies that PeekCommand succeeds for write-only regions (debug reads bypass permission checks).
    /// </summary>
    [Test]
    public void PeekCommand_WriteOnlyRegion_SucceedsWithDebugRead()
    {
        // Create a bus with a write-only region
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "test-memory");
        var ramTarget = new RamTarget(physical.Slice(0, 0x4000));
        var writeOnlyTarget = new RamTarget(physical.Slice(0x4000, 0x4000));

        // Pages 0-3: RAM (read/write)
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 4,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: ramTarget.Capabilities,
            target: ramTarget,
            physicalBase: 0);

        // Pages 4-7: Write-only (unusual but possible)
        // Note: Write to the backing memory first so peek can read something
        physical.Slice(0x4000, 0x1000).Span[0] = 0xAB;
        partialBus.MapPageRange(
            startPage: 4,
            pageCount: 4,
            deviceId: 1,
            regionTag: RegionTag.Io,
            perms: PagePerms.Write,
            caps: writeOnlyTarget.Capabilities,
            target: writeOnlyTarget,
            physicalBase: 0);

        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler);

        var command = new PeekCommand();
        var result = command.Execute(context, ["$4000"]);

        Assert.Multiple(() =>
        {
            // Debug reads (peek) bypass permission checks, so this should succeed
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Not.Contain("??"), "Debug reads bypass permission checks");
            Assert.That(outputWriter.ToString(), Does.Not.Contain("Bus faults"), "No faults for debug reads");
            Assert.That(outputWriter.ToString(), Does.Contain("AB"), "Should read the value from memory");
        });
    }

    /// <summary>
    /// Verifies that PokeCommand in interactive mode reports faults immediately.
    /// </summary>
    [Test]
    public void PokeCommand_InteractiveMode_ReportsFaultsImmediately()
    {
        // Create a bus with only partial mapping
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        // Set up input for interactive mode
        var inputText = "AB CD\n\n";
        using var inputReader = new StringReader(inputText);
        var context = new DebugContext(dispatcher, outputWriter, errorWriter, cpu, partialBus, disassembler, machineInfo: null, input: inputReader);

        var command = new PokeCommand();
        var result = command.Execute(context, ["$8000", "-i"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(outputWriter.ToString(), Does.Contain("Fault at $8000"), "Should report fault at first address");
            Assert.That(outputWriter.ToString(), Does.Contain("Fault at $8001"), "Should report fault at second address");
            Assert.That(outputWriter.ToString(), Does.Contain("Unmapped"), "Should identify fault kind");
        });
    }

    private static MainBus PartialBus()
    {
        var partialBus = new MainBus(16);
        var physical = new PhysicalMemory(0x8000, "partial-ram");
        var target = new RamTarget(physical.Slice(0, 0x8000));
        partialBus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);
        return partialBus;
    }
}