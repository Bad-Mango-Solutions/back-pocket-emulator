// <copyright file="BareCpuFixture.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Harness;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// A 65C02 on a flat 64 KB RAM bus, with no devices attached.
/// </summary>
/// <remarks>
/// The scheduler and signal bus exist only because the CPU's public constructor
/// requires an event context. Nothing is registered on either one, so instruction
/// timing is the CPU and the RAM target alone. The CPU is reset once so it runs
/// in 65C02 emulation mode; callers then load a program and set the program counter.
/// </remarks>
public sealed class BareCpuFixture
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BareCpuFixture"/> class.
    /// </summary>
    public BareCpuFixture()
    {
        var memory = new PhysicalMemory(0x10000, "bare-ram");
        var target = new RamTarget(memory.Slice(0, 0x10000), "bare-ram");
        var bus = new MainBus(16);
        bus.MapPageRange(
            startPage: 0,
            pageCount: 16,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        var context = new EventContext(new Scheduler(), new SignalBus(), bus);
        Ram = memory;
        Cpu = new Cpu65C02(context);

        // A constructed Registers value is not in 65C02 mode until reset. Reset
        // sets the emulation and compatibility flags so operand reads stay 8-bit.
        Cpu.Reset();
    }

    /// <summary>
    /// Gets the CPU under test.
    /// </summary>
    public Cpu65C02 Cpu { get; }

    /// <summary>
    /// Gets the flat RAM image. Writes are visible to the CPU on the next step.
    /// </summary>
    public Span<byte> Memory => Ram.AsSpan();

    /// <summary>
    /// Gets the physical RAM backing the bus.
    /// </summary>
    private PhysicalMemory Ram { get; }

    /// <summary>
    /// Fills a straight run of the same opcode and plants an absolute jump back to
    /// <paramref name="address"/> immediately after it.
    /// </summary>
    /// <param name="address">The address of the first opcode byte.</param>
    /// <param name="opcode">The opcode byte to repeat.</param>
    /// <param name="count">How many copies to write. Must leave room for the jump.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the run does not fit in the 64 KB image.</exception>
    public void FillOpcodeRun(Addr address, byte opcode, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Span<byte> memory = Memory;
        int start = checked((int)address);
        int end = checked(start + count + 3);
        if (end > memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Opcode run does not fit in the 64 KB image.");
        }

        memory.Slice(start, count).Fill(opcode);
        memory[start + count] = 0x4C;
        memory[start + count + 1] = (byte)address;
        memory[start + count + 2] = (byte)(address >> 8);
    }
}