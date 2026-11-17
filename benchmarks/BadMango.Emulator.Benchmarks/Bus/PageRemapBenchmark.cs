// <copyright file="PageRemapBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Bus;

using BadMango.Emulator.Bus;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Times <see cref="MainBus.SelectSwapVariant"/>, the remap that publishes fast-path fields.
/// </summary>
/// <remarks>
/// The CPU byte path does not read those fields yet. This measures the cost of rebuilding
/// them on a one-page bank switch. The two banks are slices of one array, which is the
/// language-card shape that changes <see cref="PageEntry.FastBase"/>.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("Bus", "Remap")]
public class PageRemapBenchmark
{
    private const int SwapsPerInvoke = 1000;

    private MainBus bus = null!;

    private uint groupId;

    /// <summary>
    /// Maps one page and registers two RAM banks for it.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        var memory = new PhysicalMemory(0x2000, "banks");
        var bank1 = new RamTarget(memory.Slice(0, 0x1000), "bank1");
        var bank2 = new RamTarget(memory.Slice(0x1000, 0x1000), "bank2");
        bus = new MainBus();
        bus.MapPage(0xD, new PageEntry(1, RegionTag.Ram, PagePerms.All, bank1.Capabilities, bank1, 0));
        groupId = bus.CreateSwapGroup("banks", 0xD000, 0x1000);
        bus.AddSwapVariant(groupId, "BANK1", bank1, 0, PagePerms.All);
        bus.AddSwapVariant(groupId, "BANK2", bank2, 0, PagePerms.All);
        bus.SelectSwapVariant(groupId, "BANK1");
    }

    /// <summary>
    /// Alternates the active bank. Each call stores a new page entry before it returns.
    /// </summary>
    [Benchmark(OperationsPerInvoke = SwapsPerInvoke)]
    public void SelectSwapVariant()
    {
        for (int i = 0; i < SwapsPerInvoke; i++)
        {
            bus.SelectSwapVariant(groupId, (i & 1) == 0 ? "BANK2" : "BANK1");
        }
    }
}