// <copyright file="SpikeMemoryRig.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Core.Signaling;

/// <summary>
/// A 64KB RAM machine wired through <see cref="MainBus"/>, shared by the spike
/// and the shipping core so the comparison uses the same bus plumbing.
/// </summary>
public sealed class SpikeMemoryRig
{
    private readonly PhysicalMemory memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpikeMemoryRig"/> class.
    /// </summary>
    public SpikeMemoryRig()
    {
        memory = new PhysicalMemory(0x10000, "spike-ram");
        var target = new RamTarget(memory.Slice(0, 0x10000));
        Bus = new MainBus(16);
        Bus.MapPageRange(
            startPage: 0,
            pageCount: 16,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);
        Scheduler = new Scheduler();
        Signals = new SignalBus();
        Context = new EventContext(Scheduler, Signals, Bus);
    }

    /// <summary>
    /// Gets the memory bus.
    /// </summary>
    public MainBus Bus { get; }

    /// <summary>
    /// Gets the scheduler.
    /// </summary>
    public Scheduler Scheduler { get; }

    /// <summary>
    /// Gets the signal bus.
    /// </summary>
    public SignalBus Signals { get; }

    /// <summary>
    /// Gets the event context used to construct a <see cref="Cpu65C02"/>.
    /// </summary>
    public EventContext Context { get; }

    /// <summary>
    /// Gets the RAM image. Writes through the bus are visible here.
    /// </summary>
    public Span<byte> Ram => memory.AsSpan();

    /// <summary>
    /// Sets every RAM byte to zero.
    /// </summary>
    public void Clear() => memory.Clear();
}