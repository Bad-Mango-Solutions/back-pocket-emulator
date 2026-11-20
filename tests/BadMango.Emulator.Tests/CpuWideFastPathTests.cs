// <copyright file="CpuWideFastPathTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

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
/// Wide CPU reads and writes index a published <see cref="MainBus"/> page, and a 4 KB
/// crossing takes the fast path on an eligible byte and the checked path on the other.
/// </summary>
[TestFixture]
public class CpuWideFastPathTests
{
    private const ushort LcBank2Split = 0xC081;

    /// <summary>
    /// A same-page RAM <see cref="CpuBase.Read16"/> matches two <see cref="CpuBase.Read8"/> results
    /// and does not enter <see cref="MainBus.TryRead8"/>.
    /// </summary>
    [Test]
    public void SamePage_Read16_MatchesTwoRead8s_AndDoesNotCallTryRead8()
    {
        var raw = new byte[0x1000];
        raw[0x0200] = 0x34;
        raw[0x0201] = 0x12;
        var bus = MapRam(raw);
        var cpu = CreateCpu(bus);
        cpu.Registers.TCU = new Cycle(7);

        Word wide = cpu.Read16(0x0200);
        byte low = cpu.Read8(0x0200);
        byte high = cpu.Read8(0x0201);

        Assert.Multiple(() =>
        {
            Assert.That(wide, Is.EqualTo((Word)(low | (high << 8))));
            Assert.That(wide, Is.EqualTo((Word)0x1234));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
            Assert.That(cpu.Registers.TCU, Is.EqualTo(new Cycle(7)));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// The absolute operand of <c>JMP $0200</c> is a same-page <see cref="CpuBase.Read16"/>.
    /// Fetch and that operand stay off <see cref="MainBus.TryRead8"/>.
    /// </summary>
    [Test]
    public void JmpAbsolute_RamOperand_DoesNotCallTryRead8()
    {
        var raw = new byte[0x10000];
        raw[0x0200] = 0x4C;
        raw[0x0201] = 0x00;
        raw[0x0202] = 0x02;
        var bus = MapRam(raw);
        var cpu = CreateCpu(bus);
        cpu.SetPC(0x0200);
        int reads = bus.TryRead8CallCount;
        ulong cycles = cpu.GetCycles();

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo((Addr)0x0200));
            Assert.That(cpu.GetCycles(), Is.EqualTo(cycles + 3));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
        });
    }

    /// <summary>
    /// A <see cref="CpuBase.Read16"/> that crosses a 4 KB boundary indexes the eligible side
    /// and calls <see cref="MainBus.TryRead8"/> only for the other side.
    /// </summary>
    [Test]
    public void Read16_Crossing4K_UsesFastOnEligibleSideAndSlowOnTheOther()
    {
        var raw = new byte[0x1000];
        raw[0x0FFF] = 0x11;
        var slow = new CountingTarget(0x1000);
        slow.Bytes[0] = 0x22;
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All, 0));
        bus.MapPage(1, Entry(slow, PagePerms.All, 0));
        var cpu = CreateCpu(bus);

        Word lowFast = cpu.Read16(0x0FFF);

        Assert.Multiple(() =>
        {
            Assert.That(lowFast, Is.EqualTo((Word)0x2211));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
            Assert.That(slow.Read8Count, Is.EqualTo(1));
            Assert.That(slow.Read16Count, Is.EqualTo(0));
            Assert.That(slow.LastReadAddress, Is.EqualTo((Addr)0));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });

        raw[0x0000] = 0x44;
        slow.Bytes[0x0FFF] = 0x33;
        slow.Read8Count = 0;
        var highFastBus = new MainBus(16);
        highFastBus.MapPage(0, Entry(slow, PagePerms.All, 0));
        highFastBus.MapPage(1, Entry(new RamTarget(raw, "ram"), PagePerms.All, 0));
        var highFastCpu = CreateCpu(highFastBus);

        Word highFast = highFastCpu.Read16(0x0FFF);

        Assert.Multiple(() =>
        {
            Assert.That(highFast, Is.EqualTo((Word)0x4433));
            Assert.That(highFastBus.TryRead8CallCount, Is.EqualTo(1));
            Assert.That(slow.Read8Count, Is.EqualTo(1));
            Assert.That(slow.Read16Count, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A <see cref="CpuBase.Write16"/> whose second page is unmapped stores the first byte,
    /// latches that second address, and the wide <c>Try*</c> result is still that byte's unmapped fault.
    /// </summary>
    [Test]
    public void Write16_SecondPageUnmapped_StoresFirstByte_AndTryWriteReturnsUnmapped()
    {
        var raw = new byte[0x1000];
        var ring = new BusFaultRing(4);
        var bus = new MainBus(16, faultRecorder: ring);
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All, 0));
        var cpu = CreateCpu(bus);
        cpu.Registers.TCU = new Cycle(3);

        int writes = bus.TryWrite8CallCount;
        cpu.Write16(0x0FFF, 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(raw[0x0FFF], Is.EqualTo(0xEF), "The first byte stays stored");
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1), "Only the unmapped byte is checked");
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(cpu.Registers.TCU, Is.EqualTo(new Cycle(3)));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo((Addr)0x1000));
            Assert.That(ring.Count, Is.EqualTo(0), "Fault recording is off. The latch is the status register.");
        });

        var access = new BusAccess(
            Address: 0x0FFF,
            Value: 0,
            WidthBits: 16,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataWrite,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
        BusResult result = bus.TryWrite16(access, 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Kind, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(result.Fault.Address, Is.EqualTo((Addr)0x1000));
            Assert.That(raw[0x0FFF], Is.EqualTo(0xEF));
            Assert.That(ring.Count, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// <see cref="CpuBase.Read16"/> at <c>$FFFF</c> on a 16-bit bus faults the high byte as unmapped
    /// at <c>$10000</c>. It does not wrap that byte to <c>$0000</c>.
    /// </summary>
    [Test]
    public void Read16_AtFFFF_FaultsHighByteAsUnmappedNotZero()
    {
        var raw = new byte[0x10000];
        raw[0x0000] = 0x34;
        raw[0xFFFF] = 0x12;
        var ring = new BusFaultRing(4);
        var bus = new MainBus(16, faultRecorder: ring);
        bus.FaultRecordingEnabled = true;
        bus.MapPage(0, Entry(new RamTarget(new Memory<byte>(raw, 0, 0x1000), "page0"), PagePerms.All, 0));
        bus.MapPage(0xF, Entry(new RamTarget(new Memory<byte>(raw, 0xF000, 0x1000), "pageF"), PagePerms.All, 0));
        var cpu = CreateCpu(bus);

        Word value = cpu.Read16(0xFFFF);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo((Word)0xFFFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
            Assert.That(raw[0x0000], Is.EqualTo(0x34));
            Assert.That(ring.Count, Is.EqualTo(1));
            Assert.That(ring.Last.HasValue, Is.True);
            Assert.That(ring.Last.GetValueOrDefault().Kind, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(ring.Last.GetValueOrDefault().Address, Is.EqualTo((Addr)0x10000));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo((Addr)0x10000));
        });
    }

    /// <summary>
    /// A fast <see cref="CpuBase.Read8"/> does not change <see cref="Registers.TCU"/>.
    /// <c>FetchByte</c> still adds one, so a NOP is two cycles.
    /// </summary>
    [Test]
    public void FastRead8_DoesNotChangeTcu_FetchByteStillAddsOne()
    {
        var raw = new byte[0x1000];
        raw[0x0200] = 0xEA;
        var bus = MapRam(raw);
        var cpu = CreateCpu(bus);
        cpu.SetPC(0x0200);
        cpu.Registers.TCU = new Cycle(4);

        _ = cpu.Read8(0x0200);

        Assert.That(cpu.Registers.TCU, Is.EqualTo(new Cycle(4)));

        cpu.Registers.TCU = Cycle.Zero;
        ulong cycles = cpu.GetCycles();
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.GetPC(), Is.EqualTo((Addr)0x0201));
            Assert.That(cpu.GetCycles(), Is.EqualTo(cycles + 2), "FetchByte adds 1 and NOP adds 1");
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Same-page <see cref="CpuBase.Read32"/> and <see cref="CpuBase.Write32"/> of RAM do not enter
    /// <c>Try*</c>. A crossing <see cref="CpuBase.Read32"/> stops at the first faulting byte.
    /// </summary>
    [Test]
    public void Read32AndWrite32_SamePageIndexes_CrossingStopsAtFirstFault()
    {
        var raw = new byte[0x1000];
        raw[0x0100] = 0x78;
        raw[0x0101] = 0x56;
        raw[0x0102] = 0x34;
        raw[0x0103] = 0x12;
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(new RamTarget(raw, "low"), PagePerms.All, 0));
        var cpu = new WideCpu(new EventContext(new Scheduler(), new SignalBus(), bus));

        DWord read = cpu.ReadDWord(0x0100);
        cpu.WriteDWord(0x0104, 0xDEADBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(0x12345678u));
            Assert.That(raw[0x0104], Is.EqualTo(0xEF));
            Assert.That(raw[0x0105], Is.EqualTo(0xBE));
            Assert.That(raw[0x0106], Is.EqualTo(0xAD));
            Assert.That(raw[0x0107], Is.EqualTo(0xDE));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
        });

        int reads = bus.TryRead8CallCount;
        DWord faulted = cpu.ReadDWord(0x0FFE);

        Assert.Multiple(() =>
        {
            Assert.That(faulted, Is.EqualTo(0xFFFFFFFFu));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 1), "The byte past the fault is not issued");
        });
    }

    /// <summary>
    /// A page that keeps its array but does not publish the operation stays on <c>Try*</c>.
    /// A write-only page stores through the index and does not grant the read.
    /// </summary>
    [Test]
    public void FastOps_NotNullBacking_SelectsTheWidePath()
    {
        var raw = new byte[0x1000];
        raw[0x0010] = 0x5A;
        raw[0x0011] = 0x5A;
        var bus = new MainBus(16);
        var target = new RamTarget(raw, "ram");
        bus.MapPage(0, Entry(target, PagePerms.None, 0));
        bus.MapPage(1, Entry(target, PagePerms.Write, 0));
        var cpu = CreateCpu(bus);

        Assert.Multiple(() =>
        {
            Assert.That(bus.GetPageEntryByIndex(0).FastBacking, Is.Not.Null);
            Assert.That(bus.GetPageEntryByIndex(0).FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(bus.GetPageEntryByIndex(1).FastOps, Is.EqualTo(FastPathOps.Write));
        });

        Word denied = cpu.Read16(0x0010);
        cpu.Write16(0x1010, 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(denied, Is.EqualTo((Word)0xFFFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0), "Same-page permission is the wide TryRead16 fault");
            Assert.That(raw[0x0010], Is.EqualTo(0xEF));
            Assert.That(raw[0x0011], Is.EqualTo(0xBE));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
        });
    }

    /// <summary>
    /// A ROM <see cref="CpuBase.Read16"/> is indexed. A ROM <see cref="CpuBase.Write16"/> stays on
    /// <c>Try*</c> because a <see cref="RomTarget"/> never publishes write.
    /// </summary>
    [Test]
    public void RomRead16_IsIndexed_RomWrite16_StaysOnTry()
    {
        var raw = new byte[0x1000];
        raw[0x0100] = 0x11;
        raw[0x0101] = 0x22;
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(new RomTarget(new ReadOnlyMemory<byte>(raw), "rom"), PagePerms.ReadExecute, 0));
        var cpu = CreateCpu(bus);

        Assert.That(bus.GetPageEntryByIndex(0).FastOps.HasFlag(FastPathOps.Write), Is.False);

        Word read = cpu.Read16(0x0100);
        int writes = bus.TryWrite8CallCount;
        cpu.Write16(0x0100, 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo((Word)0x2211));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
            Assert.That(raw[0x0100], Is.EqualTo(0x11));
            Assert.That(raw[0x0101], Is.EqualTo(0x22));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes), "Same-page ROM write is the wide TryWrite16 fault");
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// A fast wide read does not call <see cref="CpuBase.GetEmulationFlag"/> or read
    /// <see cref="IEventContext.Now"/>. The slow byte of a crossing still does.
    /// </summary>
    [Test]
    public void FastRead16_DoesNotCallEmulationFlagOrNow_SlowByteStillDoes()
    {
        var raw = new byte[0x1000];
        raw[0x0010] = 0x34;
        raw[0x0011] = 0x12;
        raw[0x0FFF] = 0x01;
        var bus = MapRam(raw);
        var context = new CountingContext(bus);
        var cpu = new ObservingCpu(context);

        Word samePage = cpu.Read16(0x0010);

        Assert.Multiple(() =>
        {
            Assert.That(samePage, Is.EqualTo((Word)0x1234));
            Assert.That(cpu.EmulationFlagReads, Is.EqualTo(0));
            Assert.That(context.NowReads, Is.EqualTo(0));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
        });

        Word crossing = cpu.Read16(0x0FFF);

        Assert.Multiple(() =>
        {
            Assert.That(crossing, Is.EqualTo((Word)0xFFFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(cpu.EmulationFlagReads, Is.EqualTo(1));
            Assert.That(context.NowReads, Is.EqualTo(1));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A bus that is not <see cref="MainBus"/> keeps today's single <c>TryRead16</c> / <c>TryWrite16</c> call.
    /// </summary>
    [Test]
    public void NonMainBus_StaysOnTryWide()
    {
        int reads = 0;
        int writes = 0;
        var bus = new Mock<IMemoryBus>();
        bus.Setup(mock => mock.TryRead16(It.Ref<BusAccess>.IsAny))
            .Returns(() =>
            {
                reads++;
                return BusResult<Word>.Success(0x1234);
            });
        bus.Setup(mock => mock.TryWrite16(It.Ref<BusAccess>.IsAny, It.IsAny<ushort>()))
            .Returns(() =>
            {
                writes++;
                return BusResult.Success();
            });
        bus.Setup(mock => mock.TryRead32(It.Ref<BusAccess>.IsAny))
            .Returns(BusResult<DWord>.Success(0x12345678));
        bus.Setup(mock => mock.TryWrite32(It.Ref<BusAccess>.IsAny, It.IsAny<uint>()))
            .Returns(BusResult.Success());

        var cpu = new WideCpu(new CountingContext(bus.Object));
        Word read = cpu.Read16(0x0200);
        cpu.Write16(0x0200, 0x0001);
        _ = cpu.ReadDWord(0x0300);
        cpu.WriteDWord(0x0300, 0x0002);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo((Word)0x1234));
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(writes, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Language-card split mode and page 0 stay on <c>Try*</c> for a wide read.
    /// Ordinary ROM at <c>$E000</c> is indexed.
    /// </summary>
    [Test]
    public void LanguageCardSplitAndPage0_Read16_StayOnTry_RomIsIndexed()
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
        byte low = cpu.Read8(0xE000);
        byte high = cpu.Read8(0xE001);
        Word rom = cpu.Read16(0xE000);

        Assert.Multiple(() =>
        {
            Assert.That(rom, Is.EqualTo((Word)(low | (high << 8))));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.GetPageEntry(0xE000).FastOps.HasFlag(FastPathOps.Read), Is.True);
        });

        cpu.Read8(LcBank2Split);
        cpu.Read8(LcBank2Split);
        Assert.That(bus.GetPageEntry(0xE000).FastOps, Is.EqualTo(FastPathOps.None));

        reads = bus.TryRead8CallCount;
        _ = cpu.Read16(0xE000);

        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 2));
            Assert.That(bus.GetPageEntry(0xE000).FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    /// <summary>
    /// Page 0 and a RAMRD page stay on <c>Try*</c> for <see cref="CpuBase.Read16"/>.
    /// Main RAM outside those targets is indexed.
    /// </summary>
    [Test]
    public void Page0AndRamRd_Read16_StayOnTry_MainRamIsIndexed()
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
        var bus = (MainBus)machine.Bus;
        var cpu = machine.Cpu;

        int reads = bus.TryRead8CallCount;
        _ = cpu.Read16(0x2100);

        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.GetPageEntry(0x2000).FastOps.HasFlag(FastPathOps.Read), Is.True);
        });

        reads = bus.TryRead8CallCount;
        _ = cpu.Read16(0x0010);

        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 2));
            Assert.That(bus.GetPageEntry(0x0000).FastOps, Is.EqualTo(FastPathOps.None));
        });

        cpu.Write8(0xC003, 0);
        reads = bus.TryRead8CallCount;
        _ = cpu.Read16(0x2100);

        Assert.Multiple(() =>
        {
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads + 2));
            Assert.That(bus.GetPageEntry(0x2000).FastOps, Is.EqualTo(FastPathOps.None));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
        });
    }

    private static MainBus MapRam(byte[] raw)
    {
        var bus = new MainBus(16);
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All, 0));
        return bus;
    }

    private static Cpu65C02 CreateCpu(MainBus bus) =>
        new(new EventContext(new Scheduler(), new SignalBus(), bus));

    private static PageEntry Entry(IBusTarget target, PagePerms perms, Addr physicalBase) =>
        new(0, target is RomTarget ? RegionTag.Rom : RegionTag.Ram, perms, target.Capabilities, target, physicalBase);

    /// <summary>
    /// Exposes the protected 32-bit helpers.
    /// </summary>
    private sealed class WideCpu : CpuBase
    {
        public WideCpu(IEventContext context)
            : base(context)
        {
        }

        public override CpuCapabilities Capabilities => CpuCapabilities.Base6502;

        public DWord ReadDWord(Addr address) => Read32(address);

        public void WriteDWord(Addr address, DWord value) => Write32(address, value);

        public override CpuStepResult Step() => throw new NotSupportedException();

        public override void Reset()
        {
        }
    }

    /// <summary>
    /// A non-RAM target. Wide and byte calls are counted so a test can see which path ran.
    /// </summary>
    private sealed class CountingTarget : IBusTarget
    {
        public CountingTarget(int size)
        {
            Bytes = new byte[size];
        }

        public byte[] Bytes { get; }

        public int Read8Count { get; set; }

        public int Read16Count { get; set; }

        public Addr LastReadAddress { get; private set; }

        public string Name => "counting";

        public TargetCaps Capabilities => TargetCaps.SupportsWide;

        public byte Read8(Addr offset, in BusAccess access)
        {
            Read8Count++;
            LastReadAddress = offset;
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

    /// <summary>
    /// Counts <see cref="IEventContext.Now"/> reads.
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
    }

    /// <summary>
    /// Observes <see cref="CpuBase.GetEmulationFlag"/>.
    /// </summary>
    private sealed class ObservingCpu : CpuBase
    {
        public ObservingCpu(IEventContext context)
            : base(context)
        {
        }

        public int EmulationFlagReads { get; private set; }

        public override CpuCapabilities Capabilities => CpuCapabilities.Base6502;

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