// <copyright file="CpuByteFastPathTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Benchmarks.Harness;
using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Signaling;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Systems;

using Moq;

/// <summary>
/// CPU byte reads, writes, and fetches index a published <see cref="MainBus"/> page
/// and leave every other access on <see cref="MainBus.TryRead8"/> / <see cref="MainBus.TryWrite8"/>.
/// </summary>
[TestFixture]
public class CpuByteFastPathTests
{
    private const ushort LcBank2ReadOnlyRam = 0xC080;
    private const ushort LcBank2Split = 0xC081;
    private const ushort LcBank2ReadWriteRam = 0xC083;

    /// <summary>
    /// A bare-CPU RAM read, write, peek, poke, and instruction fetch do not enter the checked path.
    /// </summary>
    [Test]
    public void BareCpu_RamReadWritePeekPokeAndFetch_DoNotEnterTry()
    {
        var fixture = new BareCpuFixture();
        var cpu = fixture.Cpu;
        var bus = BusOf(cpu);
        fixture.Memory[0x0200] = 0xA9;
        fixture.Memory[0x0201] = 0x42;
        fixture.Memory[0x0202] = 0x85;
        fixture.Memory[0x0203] = 0x50;
        cpu.SetPC(0x0200);
        cpu.Registers.TCU = new Cycle(4);

        int reads = bus.TryRead8CallCount;
        int writes = bus.TryWrite8CallCount;
        byte peeked = cpu.Peek8(0x0201);
        byte read = cpu.Read8(0x0201);
        cpu.Write8(0x0080, 0x17);
        cpu.Poke8(0x0081, 0x18);

        Assert.Multiple(() =>
        {
            Assert.That(peeked, Is.EqualTo(0x42));
            Assert.That(read, Is.EqualTo(peeked));
            Assert.That(fixture.Memory[0x0080], Is.EqualTo(0x17));
            Assert.That(fixture.Memory[0x0081], Is.EqualTo(0x18));
            Assert.That(cpu.Registers.TCU, Is.EqualTo(new Cycle(4)));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes));
        });

        cpu.Registers.TCU = Cycle.Zero;
        ulong cycles = cpu.GetCycles();
        cpu.Step();
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x42));
            Assert.That(fixture.Memory[0x0050], Is.EqualTo(0x42));
            Assert.That(cpu.GetPC(), Is.EqualTo((Addr)0x0204));
            Assert.That(cpu.GetCycles(), Is.EqualTo(cycles + 5));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes));
        });
    }

    /// <summary>
    /// A 65C02 fetch of a read-only RAM page uses <see cref="FastPathOps.Read"/> and ignores a missing fetch flag.
    /// </summary>
    [Test]
    public void BareCpu_FetchOfReadOnlyRam_IgnoresMissingFetchFlag()
    {
        var memory = new PhysicalMemory(0x10000, "rom-like-ram");
        memory.AsSpan()[0x0200] = 0xEA;
        memory.AsSpan()[0xFFFC] = 0x00;
        memory.AsSpan()[0xFFFD] = 0x02;
        var target = new RamTarget(memory.Slice(0, 0x10000), "rom-like-ram");
        var bus = new MainBus(16);
        bus.MapPageRange(0, 16, 0, RegionTag.Ram, PagePerms.Read, target.Capabilities, target, 0);
        Assert.That(bus.GetPageEntryByIndex(2).FastOps, Is.EqualTo(FastPathOps.Read));

        var cpu = new Cpu65C02(new EventContext(new Scheduler(), new SignalBus(), bus));
        cpu.Reset();
        int reads = bus.TryRead8CallCount;
        int writes = bus.TryWrite8CallCount;

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo((Addr)0x0201));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes));
        });
    }

    /// <summary>
    /// The index is <c>FastBase + PhysicalBase + (A &amp; 0xFFF)</c>, read from the live slot.
    /// </summary>
    [Test]
    public void ReadWrite_UsesFastBaseAndPhysicalBase()
    {
        var raw = new byte[0x3000];
        raw[0x0123] = 0x11;
        raw[0x1123] = 0x22;
        raw[0x2123] = 0xAB;
        var target = new RamTarget(new Memory<byte>(raw, 0x1000, 0x2000), "sliced");
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(target, PagePerms.All, 0));
        bus.MapPage(1, Entry(target, PagePerms.All, 0x1000));
        var cpu = CreateCpu(bus);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0x0123), Is.EqualTo(0x22));
            Assert.That(cpu.Read8(0x1123), Is.EqualTo(0xAB));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
        });

        cpu.Write8(0x1123, 0xCD);

        Assert.Multiple(() =>
        {
            Assert.That(raw[0x2123], Is.EqualTo(0xCD));
            Assert.That(raw[0x1123], Is.EqualTo(0x22));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A ROM write and an unmapped read stay on the checked path and keep today's <see cref="HaltState.Stp"/> result.
    /// A later fast read does not clear that halt.
    /// </summary>
    [Test]
    public void RomWriteAndUnmappedRead_UseTry_AndFastReadLeavesStpSet()
    {
        var pool = new PhysicalMemory(0x2000, "mem");
        pool.AsSpan()[0x0100] = 0xEA;
        pool.AsSpan()[0x1100] = 0xEA;
        var ram = new RamTarget(pool.Slice(0, 0x1000), "ram");
        var rom = new RomTarget(pool.Slice(0x1000, 0x1000), "rom");
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(ram, PagePerms.All, 0));
        bus.MapPage(1, Entry(rom, PagePerms.ReadExecute, 0));
        var cpu = CreateCpu(bus);

        int writes = bus.TryWrite8CallCount;
        cpu.Write8(0x1100, 0x11);

        Assert.Multiple(() =>
        {
            Assert.That(pool.AsSpan()[0x1100], Is.EqualTo(0xEA));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
            Assert.That(bus.GetPageEntryByIndex(1).FastOps.HasFlag(FastPathOps.Write), Is.False);
        });

        int reads = bus.TryRead8CallCount;
        byte unmapped = cpu.Read8(0x2000);

        Assert.Multiple(() =>
        {
            Assert.That(unmapped, Is.EqualTo(0xFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1));
        });

        byte outOfRange = cpu.Read8(0x10000);
        byte ramByte = cpu.Read8(0x0100);

        Assert.Multiple(() =>
        {
            Assert.That(outOfRange, Is.EqualTo(0xFF));
            Assert.That(ramByte, Is.EqualTo(0xEA));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 2));
        });
    }

    /// <summary>
    /// A page that keeps its array but publishes no operation for the access stays on the checked path.
    /// </summary>
    [Test]
    public void NoGrantedOperation_KeepsBackingAndUsesTry()
    {
        var pool = new PhysicalMemory(0x1000, "ram");
        pool.AsSpan()[0x10] = 0x5A;
        var target = new RamTarget(pool.Slice(0, 0x1000), "ram");
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(target, PagePerms.None, 0));
        bus.MapPage(1, Entry(target, PagePerms.Write, 0));
        var cpu = CreateCpu(bus);
        PageEntry none = bus.GetPageEntryByIndex(0);
        PageEntry writeOnly = bus.GetPageEntryByIndex(1);

        Assert.Multiple(() =>
        {
            Assert.That(none.FastBacking, Is.Not.Null);
            Assert.That(none.FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(writeOnly.FastOps, Is.EqualTo(FastPathOps.Write));
        });

        byte denied = cpu.Read8(0x0010);
        cpu.Write8(0x0010, 0x01);

        Assert.Multiple(() =>
        {
            Assert.That(denied, Is.EqualTo(0xFF));
            Assert.That(pool.AsSpan()[0x10], Is.EqualTo(0x5A));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(1));
        });

        cpu.Write8(0x1010, 0x77);
        byte writeOnlyRead = cpu.Read8(0x1010);

        Assert.Multiple(() =>
        {
            Assert.That(pool.AsSpan()[0x10], Is.EqualTo(0x77));
            Assert.That(writeOnlyRead, Is.EqualTo(0xFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(1));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// A debug write to a writable <see cref="RomTarget"/> still goes through <see cref="RomTarget.Write8"/>.
    /// A read-only ROM poke does not change the byte. A RAM poke with write permission does not enter <c>TryWrite8</c>.
    /// </summary>
    [Test]
    public void Poke8_WritableRomUsesTry_ReadOnlyRomIsIgnored_RamPokeIsIndexed()
    {
        var pool = new PhysicalMemory(0x3000, "mem");
        pool.AsSpan()[0x0004] = 0x11;
        pool.AsSpan()[0x1004] = 0x22;
        pool.AsSpan()[0x2004] = 0x33;
        var ram = new RamTarget(pool.Slice(0, 0x1000), "ram");
        var writableRom = new RomTarget(pool.Slice(0x1000, 0x1000), "rom-writable");
        var readOnlyRom = new RomTarget(pool.Memory.Slice(0x2000, 0x1000), "rom-readonly");
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(ram, PagePerms.All, 0));
        bus.MapPage(1, Entry(writableRom, PagePerms.ReadExecute, 0));
        bus.MapPage(2, Entry(readOnlyRom, PagePerms.ReadExecute, 0));
        var cpu = CreateCpu(bus);

        int writes = bus.TryWrite8CallCount;
        cpu.Poke8(0x1004, 0x60);
        cpu.Poke8(0x2004, 0x60);
        cpu.Poke8(0x0004, 0x60);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Peek8(0x1004), Is.EqualTo(0x60));
            Assert.That(cpu.Read8(0x1004), Is.EqualTo(0x60));
            Assert.That(pool.AsSpan()[0x2004], Is.EqualTo(0x33));
            Assert.That(pool.AsSpan()[0x0004], Is.EqualTo(0x60));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 2));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A fast hit does not call <see cref="CpuBase.GetEmulationFlag"/> or read <see cref="IEventContext.Now"/>.
    /// A native-style fetch without <see cref="FastPathOps.Fetch"/> falls through, where those calls still happen.
    /// </summary>
    [Test]
    public void FastPath_DoesNotCallEmulationFlagOrNow_SlowPathStillDoes()
    {
        var pool = new PhysicalMemory(0x2000, "ram");
        pool.AsSpan()[0x10] = 0x3C;
        pool.AsSpan()[0x1010] = 0x4D;
        var readOnly = new RamTarget(pool.Slice(0, 0x1000), "read-only");
        var writable = new RamTarget(pool.Slice(0x1000, 0x1000), "writable");
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(readOnly, PagePerms.Read, 0));
        bus.MapPage(1, Entry(writable, PagePerms.All, 0));
        var context = new CountingContext(bus);
        var cpu = new ObservingCpu(context);

        byte read = cpu.Read8(0x0010);
        byte peek = cpu.Peek8(0x0010);
        byte fetched = cpu.Fetch(0x0010);
        cpu.Write8(0x1010, 0x66);
        cpu.Poke8(0x1011, 0x67);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(0x3C));
            Assert.That(peek, Is.EqualTo(0x3C));
            Assert.That(fetched, Is.EqualTo(0x3C));
            Assert.That(pool.AsSpan()[0x1010], Is.EqualTo(0x66));
            Assert.That(pool.AsSpan()[0x1011], Is.EqualTo(0x67));
            Assert.That(cpu.EmulationFlagReads, Is.EqualTo(0));
            Assert.That(context.NowReads, Is.EqualTo(0));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
        });

        byte nativeFetch = cpu.FetchRequiringExecute(0x0010);

        Assert.Multiple(() =>
        {
            Assert.That(nativeFetch, Is.EqualTo(0x3C));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(cpu.EmulationFlagReads, Is.EqualTo(1));
            Assert.That(context.NowReads, Is.EqualTo(1));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
        });

        byte unmapped = cpu.Read8(0x2000);

        Assert.Multiple(() =>
        {
            Assert.That(unmapped, Is.EqualTo(0xFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.EmulationFlagReads, Is.EqualTo(2));
            Assert.That(context.NowReads, Is.EqualTo(2));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// A bus that is not <see cref="MainBus"/> stays on <c>Try*</c>.
    /// </summary>
    [Test]
    public void NonMainBus_StaysOnTry()
    {
        int reads = 0;
        int writes = 0;
        var bus = new Mock<IMemoryBus>();
        bus.Setup(mock => mock.TryRead8(It.Ref<BusAccess>.IsAny))
            .Returns(() =>
            {
                reads++;
                return BusResult<byte>.Success(0x3C);
            });
        bus.Setup(mock => mock.TryWrite8(It.Ref<BusAccess>.IsAny, It.IsAny<byte>()))
            .Returns(() =>
            {
                writes++;
                return BusResult.Success();
            });

        var context = new CountingContext(bus.Object);
        var cpu = new Cpu65C02(context);
        cpu.Read8(0x0200);
        cpu.Peek8(0x0200);
        cpu.Write8(0x0200, 0x01);
        cpu.Poke8(0x0200, 0x02);

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(writes, Is.EqualTo(2));
            Assert.That(context.NowReads, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// Language-card split mode stays checked. The byte after the switch to bank RAM is the new bank,
    /// and that read is indexed.
    /// </summary>
    [Test]
    public void LanguageCard_NextByteAfterSwitch_ReadsTheNewBank()
    {
        var machine = new MachineBuilder().AsPocket2e().WithStubRom().Build();
        if (machine.State == MachineState.Off)
        {
            machine.PowerOnAsync().GetAwaiter().GetResult();
        }

        machine.Reset();
        var bus = (MainBus)machine.Bus;
        var cpu = machine.Cpu;

        int reads = bus.TryRead8CallCount;
        Assert.That(cpu.Read8(0xE000), Is.EqualTo(0xEA), "Power-on $E000 is ROM and is indexed");
        Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));

        int writes = bus.TryWrite8CallCount;
        cpu.Write8(0xE000, 0x11);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0xEA));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
        });

        cpu.Read8(LcBank2Split);
        cpu.Read8(LcBank2Split);
        Assert.That(bus.GetPageEntry(0xE000).FastOps, Is.EqualTo(FastPathOps.None));

        reads = bus.TryRead8CallCount;
        writes = bus.TryWrite8CallCount;
        Assert.That(cpu.Read8(0xE000), Is.EqualTo(0xEA));
        cpu.Write8(0xE000, 0xDE);
        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
        });

        cpu.Read8(LcBank2ReadWriteRam);
        cpu.Read8(LcBank2ReadWriteRam);
        reads = bus.TryRead8CallCount;
        Assert.That(cpu.Read8(0xE000), Is.EqualTo(0xDE));
        Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads), "The bank published by the switch is indexed");

        cpu.Read8(LcBank2ReadOnlyRam);
        writes = bus.TryWrite8CallCount;
        reads = bus.TryRead8CallCount;
        cpu.Write8(0xE000, 0x00);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0xE000), Is.EqualTo(0xDE));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// RAMRD, RAMWRT, and the page-0 target stay on the checked path, and the byte after the switch
    /// is the bank that switch selected.
    /// </summary>
    [Test]
    public void RamRdRamWrtAndPage0_StayOnTry_AndSeeTheSelectedBank()
    {
        var mainRam = new PhysicalMemory(0xC000, "main-ram-48k");
        var builder = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(context => new Cpu65C02(context));
        builder.AddPhysicalMemory("main-ram-48k", mainRam);
        builder.ConfigureMemory((bus, registry) =>
        {
            var ram = builder.GetPhysicalMemory("main-ram-48k")!;
            var target = new RamTarget(ram.Slice(0, 0xC000), "main");
            int deviceId = registry.GenerateId();
            registry.Register(deviceId, "RAM", "Main RAM", "Memory/MainRAM");
            bus.MapRegion(0x0000, 0xC000, deviceId, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        });
        builder.WithSlotManager().WithPocket2eIOPage().WithExtended80ColumnDevice();

        var machine = builder.Build();
        var aux = machine.GetComponent<Extended80ColumnDevice>()
            ?? throw new AssertionException("Extended80ColumnDevice should be registered.");
        var bus = (MainBus)machine.Bus;
        var cpu = machine.Cpu;
        aux.WriteAuxRam(0x0200, 0x3C);
        aux.WriteAuxRam(0x2100, 0xA6);

        int reads = bus.TryRead8CallCount;
        int writes = bus.TryWrite8CallCount;
        cpu.Write8(0x2000, 0x11);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0x2000), Is.EqualTo(0x11));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes));
        });

        cpu.Write8(0x0200, 0x22);
        Assert.That(bus.TryWrite8CallCount, Is.GreaterThan(writes), "Page 0 is a split target");
        reads = bus.TryRead8CallCount;
        Assert.That(cpu.Read8(0x0200), Is.EqualTo(0x22));
        Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1));

        cpu.Write8(0xC005, 0);
        writes = bus.TryWrite8CallCount;
        reads = bus.TryRead8CallCount;
        cpu.Write8(0x2000, 0x73);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0x2000), Is.EqualTo(0x11));
            Assert.That(aux.ReadAuxRam(0x2000), Is.EqualTo(0x73));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1));
        });

        cpu.Write8(0xC004, 0);
        reads = bus.TryRead8CallCount;
        Assert.That(cpu.Read8(0x2000), Is.EqualTo(0x11));
        Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads), "Deactivating RAMWRT publishes main RAM again");

        cpu.Write8(0xC003, 0);
        reads = bus.TryRead8CallCount;
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Read8(0x2100), Is.EqualTo(0xA6));
            Assert.That(cpu.Read8(0x2000), Is.EqualTo(0x73));
            Assert.That(cpu.Read8(0x0200), Is.EqualTo(0x3C));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 3));
            Assert.That(bus.GetPageEntry(0x0000).FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(bus.GetPageEntry(0x2000).FastOps, Is.EqualTo(FastPathOps.None));
        });
    }

    private static MainBus BusOf(Cpu65C02 cpu) => (MainBus)cpu.EventContext.Bus;

    private static Cpu65C02 CreateCpu(MainBus bus) =>
        new(new EventContext(new Scheduler(), new SignalBus(), bus));

    private static PageEntry Entry(IBusTarget target, PagePerms perms, Addr physicalBase) =>
        new(0, target is RomTarget ? RegionTag.Rom : RegionTag.Ram, perms, target.Capabilities, target, physicalBase);

    /// <summary>
    /// Counts <see cref="IEventContext.Now"/> reads. The fast path must not touch the getter.
    /// </summary>
    private sealed class CountingContext : IEventContext
    {
        public CountingContext(IMemoryBus bus)
        {
            Bus = bus;
            Scheduler = new Scheduler();
            Signals = new SignalBus();
        }

        public int NowReads { get; private set; }

        public Cycle Now
        {
            get
            {
                NowReads++;
                return Scheduler.Now;
            }
        }

        public IScheduler Scheduler { get; }

        public ISignalBus Signals { get; }

        public IMemoryBus Bus { get; }

        public T? GetComponent<T>()
            where T : class => null;

        public IEnumerable<T> GetComponents<T>()
            where T : class => [];

        public bool HasComponent<T>()
            where T : class => false;

        public void AddComponent<T>(T component)
            where T : class => ArgumentNullException.ThrowIfNull(component);
    }

    /// <summary>
    /// Observes <see cref="CpuBase.GetEmulationFlag"/> and exposes the shared instruction-fetch helper.
    /// </summary>
    private sealed class ObservingCpu : CpuBase
    {
        public ObservingCpu(IEventContext context)
            : base(context)
        {
        }

        public int EmulationFlagReads { get; private set; }

        public override CpuCapabilities Capabilities => CpuCapabilities.Base6502;

        public byte Fetch(Addr address) => ReadInstructionByte(address, FastPathOps.Read);

        public byte FetchRequiringExecute(Addr address) => ReadInstructionByte(address, FastPathOps.Fetch);

        public override CpuStepResult Step() => throw new NotSupportedException();

        public override void Reset()
        {
        }

        protected override bool GetEmulationFlag()
        {
            EmulationFlagReads++;
            return true;
        }
    }
}