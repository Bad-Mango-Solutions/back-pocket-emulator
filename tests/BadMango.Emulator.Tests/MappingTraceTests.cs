// <copyright file="MappingTraceTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Mapping trace records remaps and slow-path bytes, and a range forces those pages slow.
/// </summary>
[TestFixture]
public class MappingTraceTests
{
    private const FastPathOps RamAll = FastPathOps.Read | FastPathOps.Write | FastPathOps.Fetch;

    private const FastPathOps ReadFetch = FastPathOps.Read | FastPathOps.Fetch;

    /// <summary>
    /// With trace clear, a fast RAM read does not enter the checked path and does not append.
    /// The checked path is the only place that loads the trace flag.
    /// </summary>
    [Test]
    public void TraceClear_FastRamLda_DoesNotEnterTryOrAppend()
    {
        (Cpu65C02 cpu, MainBus bus, _) = PlantLda();
        cpu.SetPC(0x1000);
        int reads = bus.TryRead8CallCount;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(bus.MappingTraceEnabled, Is.False);
            Assert.That(bus.HasMappingTraceRange, Is.False);
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x5A));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.MappingTrace.Count, Is.Zero);
        });
    }

    /// <summary>
    /// Trace on with no range records a language-card remap and one $C000 read, and a fast LDA adds nothing.
    /// </summary>
    [Test]
    public void TraceOn_NoRange_RecordsLanguageCardRemap_C000Read_AndSkipsFastLda()
    {
        (MainBus bus, LanguageCardDevice card, IOPageDispatcher dispatcher, Cpu65C02 cpu) = PlantLanguageCard();
        bus.SetSoftSwitchSource(() => new ISoftSwitchProvider[] { card });
        bus.MappingTraceEnabled = true;
        bus.MappingTrace.Clear();

        ReadSwitch(dispatcher, 0x80);

        MappingTraceRecord[] remaps = Remaps(bus);
        MappingTraceRecord d000 = remaps.Last(record => record.IncludesPage(0xD));
        Assert.Multiple(() =>
        {
            Assert.That(d000.TargetName, Is.EqualTo("LC_Bank2"));
            Assert.That(d000.PhysicalBase, Is.EqualTo(0u));
            Assert.That(d000.NaturalFastOps, Is.EqualTo(ReadFetch));
            Assert.That(d000.SoftSwitches, Is.Not.Null);
            Assert.That(
                d000.SoftSwitches!.Any(state => state.Address == 0xC080 && state.Value),
                Is.True,
                "snapshot includes the language-card state after the switch");
        });

        bus.MappingTrace.Clear();
        var access = DataRead(0xC000);
        BusResult<byte> read = bus.TryRead8(access);
        MappingTraceRecord[] lines = bus.MappingTrace.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(read.Ok, Is.True);
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0].Kind, Is.EqualTo(MappingTraceKind.Access));
            Assert.That(lines[0].Address, Is.EqualTo(0xC000u));
            Assert.That(lines[0].TargetName, Is.EqualTo("$C000"));
        });

        int accessCount = AccessCount(bus);
        int tryCount = bus.TryRead8CallCount;
        cpu.SetPC(0x2000);
        cpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(AccessCount(bus), Is.EqualTo(accessCount));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(tryCount));
        });
    }

    /// <summary>
    /// <c>maptrace range $0800 $08FF</c> slows one page. The next page stays fast.
    /// <c>range clear</c> restores the page and the next LDA is not recorded.
    /// </summary>
    [Test]
    public void MapTraceRange_ForcesOnePage_ClearRestoresIt()
    {
        (Cpu65C02 cpu, MainBus bus, DebugContext context) = PlantLda();
        var command = new MapTraceCommand(null, DebugOutputRenderer.PlainText);
        CommandResult ranged = command.Execute(context, ["range", "$0800", "$08FF"]);

        Assert.Multiple(() =>
        {
            Assert.That(ranged.Success, Is.True, ranged.Message);
            Assert.That(bus.MappingTraceEnabled, Is.True);
            Assert.That(bus.HasMappingTraceRange, Is.True);
            Assert.That(bus.MappingTraceRangeStart, Is.EqualTo(0x0800u));
            Assert.That(bus.MappingTraceRangeEnd, Is.EqualTo(0x08FFu));
            Assert.That(bus.GetPageEntryByIndex(0).FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(bus.GetPageEntryByIndex(0).FastBacking, Is.Not.Null);
            Assert.That(bus.GetPageEntryByIndex(1).FastOps, Is.EqualTo(RamAll));
        });

        bus.MappingTrace.Clear();
        cpu.SetPC(0x1000);
        cpu.Step();
        MappingTraceRecord[] afterSlow = bus.MappingTrace.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(afterSlow, Has.Length.EqualTo(1));
            Assert.That(afterSlow[0].Kind, Is.EqualTo(MappingTraceKind.Access));
            Assert.That(afterSlow[0].Address, Is.EqualTo(0x0800u));
            Assert.That(afterSlow[0].TargetName, Is.EqualTo("MainRAM"));
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x5A));
        });

        int accessCount = AccessCount(bus);
        int tryCount = bus.TryRead8CallCount;
        cpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(AccessCount(bus), Is.EqualTo(accessCount), "LDA in the next page is not recorded");
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(tryCount));
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x11));
        });

        CommandResult cleared = command.Execute(context, ["range", "clear"]);
        Assert.Multiple(() =>
        {
            Assert.That(cleared.Success, Is.True, cleared.Message);
            Assert.That(bus.MappingTraceEnabled, Is.True);
            Assert.That(bus.HasMappingTraceRange, Is.False);
            Assert.That(bus.GetPageEntryByIndex(0).FastOps, Is.EqualTo(RamAll));
        });

        accessCount = AccessCount(bus);
        tryCount = bus.TryRead8CallCount;
        cpu.SetPC(0x1006);
        cpu.Step();
        Assert.Multiple(() =>
        {
            Assert.That(AccessCount(bus), Is.EqualTo(accessCount));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(tryCount));
        });
    }

    /// <summary>
    /// A language-card remap of a page inside the range leaves that page slow.
    /// <c>maptrace off</c> clears the range and restores it.
    /// </summary>
    [Test]
    public void LanguageCardRemap_InsideRange_StaysSlow_OffRestores()
    {
        (MainBus bus, _, IOPageDispatcher dispatcher, Cpu65C02 cpu) = PlantLanguageCard();
        byte[]? romBacking = bus.GetPageEntryByIndex(0xD).FastBacking;
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter(), cpu, bus, null);
        var command = new MapTraceCommand(null, DebugOutputRenderer.PlainText);

        CommandResult ranged = command.Execute(context, ["range", "$D000", "$D0FF"]);
        Assert.That(ranged.Success, Is.True, ranged.Message);
        Assert.That(bus.GetPageEntryByIndex(0xD).FastOps, Is.EqualTo(FastPathOps.None));

        ReadSwitch(dispatcher, 0x80);
        PageEntry bank = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(bank.FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(bank.FastBacking, Is.Not.Null);
            Assert.That(bank.FastBacking, Is.Not.SameAs(romBacking));
            Assert.That(bank.Target, Is.InstanceOf<RamTarget>());
            Assert.That(bank.Target!.Name, Is.EqualTo("LC_Bank2"));
            Assert.That(bank.PhysicalBase, Is.EqualTo(0u));
        });

        int reads = bus.TryRead8CallCount;
        _ = cpu.Read8(0xD010);
        Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1));

        CommandResult off = command.Execute(context, ["off"]);
        PageEntry restored = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(off.Success, Is.True, off.Message);
            Assert.That(bus.MappingTraceEnabled, Is.False);
            Assert.That(bus.HasMappingTraceRange, Is.False);
            Assert.That(restored.FastOps, Is.EqualTo(ReadFetch));
            Assert.That(restored.FastBacking, Is.SameAs(bank.FastBacking));
        });

        int afterOff = bus.TryRead8CallCount;
        int recorded = bus.MappingTrace.Count;
        _ = cpu.Read8(0xD010);
        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(afterOff));
            Assert.That(bus.MappingTrace.Count, Is.EqualTo(recorded));
        });
    }

    /// <summary>
    /// A rebuild while trace is off does not ask providers for soft-switch state, and one rebuild
    /// takes a single snapshot for every page it stores.
    /// </summary>
    [Test]
    public void Remap_SnapshotsOnlyWhileTraceIsOn_OncePerRebuild()
    {
        var memory = new PhysicalMemory(0x3000, "ram");
        var target = new RamTarget(memory.Slice(0, 0x3000), "ram");
        var bus = new MainBus(16);
        var provider = new CountingProvider();
        bus.SetSoftSwitchSource(() => new ISoftSwitchProvider[] { provider });
        var entry = new PageEntry(1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);

        bus.MapPageRange(0, 3, 1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        Assert.That(provider.Calls, Is.Zero);

        bus.MappingTraceEnabled = true;
        bus.MapPageRange(0, 3, 1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        MappingTraceRecord[] remaps = Remaps(bus);
        Assert.Multiple(() =>
        {
            Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.That(remaps, Has.Length.EqualTo(3));
            Assert.That(remaps[0].SoftSwitches, Is.SameAs(remaps[1].SoftSwitches));
            Assert.That(remaps[1].SoftSwitches, Is.SameAs(remaps[2].SoftSwitches));
            Assert.That(remaps[0].SoftSwitches!.Any(state => state.Address == 0xC080 && state.Value), Is.True);
        });

        bus.MapPage(0, entry);
        Assert.That(provider.Calls, Is.EqualTo(2));
    }

    /// <summary>
    /// The machine builder points the bus at the components it already has.
    /// </summary>
    [Test]
    public void MachineBuilder_SnapshotsLanguageCardOnTheMachine()
    {
        var card = new LanguageCardDevice();
        IMachine machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(static ctx => new Cpu65C02(ctx))
            .AddComponent(card)
            .Build();
        var bus = (MainBus)machine.Bus;
        var memory = new PhysicalMemory(0x1000, "page");
        var target = new RamTarget(memory.Slice(0, 0x1000), "page");
        bus.MappingTraceEnabled = true;
        bus.MapPage(0, new PageEntry(1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0));

        MappingTraceRecord remap = Remaps(bus).Single(record => record.IncludesPage(0));
        Assert.That(remap.SoftSwitches!.Any(state => state.Address == 0xC08A && state.Value), Is.True);
    }

    /// <summary>
    /// Appending access records, including past the ring's capacity, allocates nothing.
    /// </summary>
    [Test]
    public void AccessRecords_DoNotAllocate()
    {
        var memory = new PhysicalMemory(0x1000, "ram");
        var target = new RamTarget(memory.Slice(0, 0x1000), "MainRAM");
        var bus = new MainBus(16);
        bus.MapPage(0, new PageEntry(1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0));
        bus.SetMappingTraceRange(0x0000, 0x00FF);
        BusAccess access = DataRead(0x0010);
        for (int i = 0; i < 32; i++)
        {
            _ = bus.TryRead8(access);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = bus.TryRead8(access);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0));
        Assert.That(bus.MappingTrace.Count, Is.EqualTo(bus.MappingTrace.Capacity));
    }

    /// <summary>
    /// A remap with trace off allocates nothing and does not read the provider.
    /// </summary>
    [Test]
    public void TraceOff_Remap_DoesNotAllocateOrSnapshot()
    {
        var memory = new PhysicalMemory(0x1000, "ram");
        var target = new RamTarget(memory.Slice(0, 0x1000), "ram");
        var bus = new MainBus(16);
        var provider = new CountingProvider();
        bus.SetSoftSwitchSource(() => new ISoftSwitchProvider[] { provider });
        bus.MapPage(0, new PageEntry(1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0));
        for (int i = 0; i < 200; i++)
        {
            bus.RemapPage(0, target, 0);
        }

        _ = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            bus.RemapPage(0, target, 0);
        }

        long oneBefore = GC.GetAllocatedBytesForCurrentThread();
        bus.RemapPage(0, target, 0);
        long one = GC.GetAllocatedBytesForCurrentThread() - oneBefore;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            bus.RemapPage(0, target, 0);
        }

        long many = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(0), $"one={one} many={many}");
            Assert.That(many, Is.EqualTo(0), $"one={one} many={many}");
            Assert.That(provider.Calls, Is.Zero);
            Assert.That(bus.MappingTrace.Count, Is.Zero);
        });
    }

    /// <summary>
    /// <c>--json</c> on <c>maptrace</c> is one <c>emudbg/v1</c> envelope.
    /// </summary>
    [Test]
    public void MapTrace_Json_IsOneEnvelope()
    {
        (Cpu65C02 cpu, MainBus bus, _) = PlantLda();
        var output = new StringWriter();
        var context = new DebugContext(new CommandDispatcher(), output, new StringWriter(), cpu, bus, null);
        var renderer = new DebugOutputRenderer(
            new EmudbgOptions(JsonOutput: true),
            () => new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        CommandResult result = new MapTraceCommand(null, renderer).Execute(context, []);

        Assert.That(result.Success, Is.True, result.Message);
        string json = output.ToString();
        Assert.That(json.Split("emudbg/v1").Length - 1, Is.EqualTo(1));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(root.GetProperty("payload").GetProperty("enabled").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("payload").GetProperty("records").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("payload").TryGetProperty("rangeStart", out _), Is.False);
            Assert.That(root.GetProperty("timestamp").GetString(), Is.EqualTo("2020-01-01T00:00:00+00:00"));
        });
    }

    private static (Cpu65C02 Cpu, MainBus Bus, DebugContext Context) PlantLda()
    {
        var memory = new PhysicalMemory(0x10000, "ram");
        memory.AsSpan()[0x0800] = 0x5A;
        memory.AsSpan()[0x1100] = 0x11;
        memory.AsSpan()[0x1000] = 0xAD;
        memory.AsSpan()[0x1001] = 0x00;
        memory.AsSpan()[0x1002] = 0x08;
        memory.AsSpan()[0x1003] = 0xAD;
        memory.AsSpan()[0x1004] = 0x00;
        memory.AsSpan()[0x1005] = 0x11;
        memory.AsSpan()[0x1006] = 0xAD;
        memory.AsSpan()[0x1007] = 0x00;
        memory.AsSpan()[0x1008] = 0x08;
        memory.AsSpan()[0x2000] = 0xAD;
        memory.AsSpan()[0x2001] = 0x00;
        memory.AsSpan()[0x2002] = 0x02;
        memory.AsSpan()[0xFFFC] = 0x00;
        memory.AsSpan()[0xFFFD] = 0x10;

        var target = new RamTarget(memory.Slice(0, 0x10000), "MainRAM");
        var bus = new MainBus(16);
        bus.MapPageRange(0, 16, 1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        var cpu = new Cpu65C02(new EventContext(new Scheduler(), new SignalBus(), bus));
        cpu.Reset();
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter(), cpu, bus, null);
        return (cpu, bus, context);
    }

    private static (MainBus Bus, LanguageCardDevice Card, IOPageDispatcher Dispatcher, Cpu65C02 Cpu) PlantLanguageCard()
    {
        var romPool = new PhysicalMemory(0x3000, "SYSROM");
        romPool.AsSpan()[0x10] = 0xD1;
        var rom = new RomTarget(romPool.Slice(0, 0x3000), "SYSROM");
        var io = new DefaultCompositeTarget("$C000");
        var bus = new MainBus(16);
        bus.MapRegion(0xD000, 0x3000, 1, RegionTag.Rom, PagePerms.ReadExecute, rom.Capabilities, rom, 0);
        bus.MapRegion(0xC000, 0x1000, 2, RegionTag.Composite, PagePerms.ReadWrite, io.Capabilities, io, 0);
        var ram = new PhysicalMemory(0x10000, "low");
        var ramTarget = new RamTarget(ram.Slice(0, 0x10000), "MainRAM");
        bus.MapPageRange(0, 0xC, 3, RegionTag.Ram, PagePerms.All, ramTarget.Capabilities, ramTarget, 0);
        ram.AsSpan()[0x2000] = 0xAD;
        ram.AsSpan()[0x2001] = 0x00;
        ram.AsSpan()[0x2002] = 0x02;

        var card = new LanguageCardDevice();
        card.ConfigureMemory(bus, new DeviceRegistry());
        card.Initialize(new EventContext(new Scheduler(), new SignalBus(), bus));
        bus.SetSoftSwitchSource(() => new ISoftSwitchProvider[] { card });
        var dispatcher = new IOPageDispatcher();
        card.RegisterHandlers(dispatcher);
        var cpu = new Cpu65C02(new EventContext(new Scheduler(), new SignalBus(), bus));
        return (bus, card, dispatcher, cpu);
    }

    private static void ReadSwitch(IOPageDispatcher dispatcher, byte offset)
    {
        var access = DataRead((uint)(0xC000 + offset));
        dispatcher.Read(offset, in access);
    }

    private static BusAccess DataRead(uint address)
    {
        return new BusAccess(
            address,
            0,
            8,
            BusAccessMode.Decomposed,
            true,
            AccessIntent.DataRead,
            0,
            0,
            AccessFlags.None);
    }

    private static int AccessCount(MainBus bus)
    {
        return bus.MappingTrace.Snapshot().Count(record => record.Kind == MappingTraceKind.Access);
    }

    private static MappingTraceRecord[] Remaps(MainBus bus)
    {
        return bus.MappingTrace.Snapshot().Where(record => record.Kind == MappingTraceKind.Remap).ToArray();
    }

    /// <summary>
    /// A provider that counts <see cref="ISoftSwitchProvider.GetSoftSwitchStates"/> calls.
    /// </summary>
    private sealed class CountingProvider : ISoftSwitchProvider
    {
        /// <summary>
        /// Gets how many times the snapshot was requested.
        /// </summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public string ProviderName => "count";

        /// <inheritdoc />
        public IReadOnlyList<SoftSwitchState> GetSoftSwitchStates()
        {
            Calls++;
            return new[] { new SoftSwitchState("X", 0xC080, true, null) };
        }
    }
}