// <copyright file="MainBusWideFastPathTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using Interfaces;

/// <summary>
/// Non-<c>Try</c> wide reads and writes index an eligible page instead of calling the target's
/// wide method, and a 4 KB crossing chooses per byte.
/// </summary>
[TestFixture]
public class MainBusWideFastPathTests
{
    private const int PageSize = 4096;

    /// <summary>
    /// A same-page RAM <see cref="MainBus.Read16"/>, <see cref="MainBus.Write16"/>,
    /// <see cref="MainBus.Read32"/>, and <see cref="MainBus.Write32"/> match the bytes and do not
    /// call <see cref="RamTarget.Read16"/> or the other wide target methods.
    /// </summary>
    [Test]
    public void SamePageRam_Indexes_AndDoesNotCallTargetWide()
    {
        var memory = new PhysicalMemory(PageSize, "TestRAM");
        memory.AsSpan()[0x100] = 0x78;
        memory.AsSpan()[0x101] = 0x56;
        memory.AsSpan()[0x102] = 0x34;
        memory.AsSpan()[0x103] = 0x12;
        var target = new RamTarget(memory.Slice(0, PageSize));
        var bus = new MainBus();
        bus.MapPage(0, new(1, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsWide, target, 0));

        Word read16 = bus.Read16(Access(0x0100, AccessIntent.DataRead, BusAccessMode.Atomic, 16));
        DWord read32 = bus.Read32(Access(0x0100, AccessIntent.DataRead, BusAccessMode.Atomic, 32));
        bus.Write16(Access(0x0200, AccessIntent.DataWrite, BusAccessMode.Atomic, 16), 0xCAFE);
        bus.Write32(Access(0x0300, AccessIntent.DataWrite, BusAccessMode.Atomic, 32), 0xDEADBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(read16, Is.EqualTo((Word)0x5678));
            Assert.That(read32, Is.EqualTo(0x12345678u));
            Assert.That(memory.AsSpan()[0x200], Is.EqualTo(0xFE));
            Assert.That(memory.AsSpan()[0x201], Is.EqualTo(0xCA));
            Assert.That(memory.AsSpan()[0x300], Is.EqualTo(0xEF));
            Assert.That(memory.AsSpan()[0x301], Is.EqualTo(0xBE));
            Assert.That(memory.AsSpan()[0x302], Is.EqualTo(0xAD));
            Assert.That(memory.AsSpan()[0x303], Is.EqualTo(0xDE));
            Assert.That(bus.TargetWideCallCount, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A target that is not eligible still receives the wide call.
    /// </summary>
    [Test]
    public void SamePageIneligible_StillCallsTargetRead16()
    {
        var slow = new CountingWideTarget();
        slow.Bytes[0x100] = 0x34;
        slow.Bytes[0x101] = 0x12;
        var bus = new MainBus();
        bus.MapPage(0, new(1, RegionTag.Io, PagePerms.ReadWrite, TargetCaps.SupportsWide, slow, 0));

        Word value = bus.Read16(Access(0x0100, AccessIntent.DataRead, BusAccessMode.Atomic, 16));

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo((Word)0x1234));
            Assert.That(slow.Read16Count, Is.EqualTo(1));
            Assert.That(bus.TargetWideCallCount, Is.EqualTo(1));
            Assert.That(bus.GetPageEntryByIndex(0).FastOps, Is.EqualTo(FastPathOps.None));
        });
    }

    /// <summary>
    /// A <see cref="MainBus.Read16"/> that crosses 4 KB indexes the RAM side and reads the other
    /// side one byte at a time.
    /// </summary>
    [Test]
    public void CrossPage_IndexesEligibleSide_AndReadsTheOtherByByte()
    {
        var memory = new PhysicalMemory(PageSize, "TestRAM");
        memory.AsSpan()[0xFFF] = 0xCD;
        var ram = new RamTarget(memory.Slice(0, PageSize));
        var slow = new CountingWideTarget();
        slow.Bytes[0] = 0xAB;
        var bus = new MainBus();
        bus.MapPage(0, new(1, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsWide, ram, 0));
        bus.MapPage(1, new(2, RegionTag.Io, PagePerms.ReadWrite, TargetCaps.SupportsWide, slow, 0));

        Word value = bus.Read16(Access(0x0FFF, AccessIntent.DataRead, BusAccessMode.Atomic, 16));

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo((Word)0xABCD));
            Assert.That(slow.Read8Count, Is.EqualTo(1));
            Assert.That(slow.Read16Count, Is.EqualTo(0));
            Assert.That(bus.TargetWideCallCount, Is.EqualTo(0));
        });
    }

    private static BusAccess Access(Addr address, AccessIntent intent, BusAccessMode mode, byte widthBits)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: mode,
            EmulationFlag: mode == BusAccessMode.Decomposed,
            Intent: intent,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
    }

    /// <summary>
    /// A wide-capable target that is not a <see cref="RamTarget"/>, so it stays off the fast path.
    /// </summary>
    private sealed class CountingWideTarget : IBusTarget
    {
        public byte[] Bytes { get; } = new byte[PageSize];

        public int Read8Count { get; private set; }

        public int Read16Count { get; private set; }

        public string Name => "counting-wide";

        public TargetCaps Capabilities => TargetCaps.SupportsWide;

        public byte Read8(Addr offset, in BusAccess access)
        {
            Read8Count++;
            return Bytes[(int)offset];
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            Bytes[(int)offset] = value;
        }

        public Word Read16(Addr offset, in BusAccess access)
        {
            Read16Count++;
            return (Word)(Bytes[(int)offset] | (Bytes[(int)offset + 1] << 8));
        }

        public void Clear()
        {
        }
    }
}