// <copyright file="MainBusFaultRegisterTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Moq;

/// <summary>
/// Fault status and fault address on <see cref="MainBus"/>.
/// </summary>
/// <remarks>
/// The registers are read from the bus. No test executes a 6502 opcode to read them.
/// </remarks>
[TestFixture]
public class MainBusFaultRegisterTests
{
    private const int PageSize = 4096;

    private static readonly FaultKind HighByte =
        FaultKind.Write | FaultKind.Fetch | FaultKind.BankCrossing | FaultKind.Privilege;

    /// <summary>
    /// A successful RAM read leaves status at zero.
    /// </summary>
    [Test]
    public void SuccessfulRamRead_LeavesStatusAtZero()
    {
        var bus = MapRam(out _);
        var result = bus.TryRead8(Access(0x0010, AccessIntent.DataRead));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
            Assert.That(bus.LatchedFault.IsSuccess, Is.True);
        });
    }

    /// <summary>
    /// A successful RAM read does not replace a status that is already <see cref="FaultKind.Unmapped"/>.
    /// </summary>
    [Test]
    public void SuccessfulRamRead_DoesNotChangeLatchedUnmapped()
    {
        var bus = MapRam(out byte[] raw);
        bus.TryRead8(Access(0x2000, AccessIntent.DataRead, cycle: 4, sourceId: 2));

        var success = bus.TryRead8(Access(0x0010, AccessIntent.DataRead, cycle: 9, sourceId: 6));
        raw[0x0020] = 0xAB;
        bus.Read8(Access(0x0020, AccessIntent.DataRead));
        bus.Read16(Access(0x0030, AccessIntent.DataRead, mode: BusAccessMode.Atomic, widthBits: 16));

        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            AssertLowByte(bus, FaultKind.Unmapped, 0x2000);
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(4ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(2));
            Assert.That(bus.LatchedFault.Intent, Is.EqualTo(AccessIntent.DataRead));
        });
    }

    /// <summary>
    /// Two unmapped reads keep the first address and the first private latch.
    /// </summary>
    [Test]
    public void TwoUnmappedReads_KeepTheFirstAddressAndLatch()
    {
        var bus = new MainBus();
        var first = bus.TryRead8(Access(0x1111, AccessIntent.DataRead, cycle: 4, sourceId: 1));
        var second = bus.TryRead8(Access(0x2222, AccessIntent.DataRead, cycle: 9, sourceId: 7));

        Assert.Multiple(() =>
        {
            Assert.That(first.Failed, Is.True);
            Assert.That(second.Failed, Is.True);
            Assert.That(second.Fault.Address, Is.EqualTo(0x2222u));
            AssertLowByte(bus, FaultKind.Unmapped, 0x1111);
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(4ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(1));
            Assert.That(bus.LatchedFault.WidthBits, Is.EqualTo(8));
            Assert.That(bus.LatchedFault.Intent, Is.EqualTo(AccessIntent.DataRead));
            Assert.That(bus.LatchedFault.DeviceId, Is.EqualTo(first.Fault.DeviceId));
            Assert.That(bus.LatchedFault.RegionTag, Is.EqualTo(first.Fault.RegionTag));
        });
    }

    /// <summary>
    /// A <see cref="MainBus.TryRead16"/> whose second byte is unmapped latches that byte
    /// and does not issue a third access.
    /// </summary>
    [Test]
    public void TryRead16_SecondByteUnmapped_LatchesThatAddress_AndDoesNotIssueAThirdAccess()
    {
        var raw = new byte[PageSize];
        raw[0x0FFF] = 0x11;
        var counting = new CountingTarget(raw);
        var bus = new MainBus();
        bus.MapPage(0, Entry(counting, PagePerms.All));

        int readsBefore = bus.TryRead8CallCount;
        var result = bus.TryRead16(Access(0x0FFF, AccessIntent.DataRead, widthBits: 16, cycle: 5));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Address, Is.EqualTo(0x1000u));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(readsBefore + 2));
            Assert.That(counting.Read8Count, Is.EqualTo(1));
            AssertLowByte(bus, FaultKind.Unmapped, 0x1000);
            Assert.That(bus.LatchedFault.WidthBits, Is.EqualTo(8));
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(5ul));
        });
    }

    /// <summary>
    /// A <see cref="MainBus.TryWrite16"/> whose second page is unmapped keeps the first byte
    /// and latches the second address.
    /// </summary>
    [Test]
    public void TryWrite16_SecondByteUnmapped_KeepsFirstByte_AndLatchesSecondAddress()
    {
        var raw = new byte[PageSize];
        var bus = new MainBus();
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All));

        var result = bus.TryWrite16(Access(0x0FFF, AccessIntent.DataWrite, widthBits: 16), 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Address, Is.EqualTo(0x1000u));
            Assert.That(raw[0x0FFF], Is.EqualTo(0xEF));
            AssertLowByte(bus, FaultKind.Unmapped, 0x1000);
        });
    }

    /// <summary>
    /// A same-page unmapped <see cref="MainBus.TryRead16"/> latches through the wide early return,
    /// which shares the byte helper and does not walk the bytes.
    /// </summary>
    [Test]
    public void TryRead16_UnmappedPage_LatchesBaseAddress_WithoutTryRead8()
    {
        var bus = new MainBus();
        var result = bus.TryRead16(Access(0x4000, AccessIntent.DataRead, widthBits: 16, cycle: 8, sourceId: 3));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(0));
            AssertLowByte(bus, FaultKind.Unmapped, 0x4000);
            Assert.That(bus.LatchedFault.WidthBits, Is.EqualTo(16));
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(8ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// The first failing byte of a wide read stops the rest of the access.
    /// </summary>
    [Test]
    public void TryRead32_SecondByteUnmapped_DoesNotIssueTheLaterBytes()
    {
        var raw = new byte[PageSize];
        var bus = new MainBus();
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All));

        int readsBefore = bus.TryRead8CallCount;
        var result = bus.TryRead32(Access(0x0FFE, AccessIntent.DataRead, widthBits: 32));

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Address, Is.EqualTo(0x1000u));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(readsBefore + 3));
            AssertLowByte(bus, FaultKind.Unmapped, 0x1000);
        });
    }

    /// <summary>
    /// A permission fault and an NX fault latch the low byte only.
    /// A decomposed fetch of an NX page succeeds and does not latch.
    /// </summary>
    [Test]
    public void PermissionAndNx_LatchLowByteOnly()
    {
        var bus = new MainBus();
        var memory = new PhysicalMemory(PageSize, "page");
        memory.AsSpan()[0x100] = 0xEA;
        var target = new RamTarget(memory.Slice(0, PageSize));
        bus.MapPage(0, new(7, RegionTag.Rom, PagePerms.Read, TargetCaps.SupportsPeek, target, 0));

        var permission = bus.TryWrite8(Access(0x0100, AccessIntent.DataWrite, cycle: 11, sourceId: 3), 0x42);
        AssertLowByte(bus, FaultKind.Permission, 0x0100);
        Assert.That(bus.LatchedFault.DeviceId, Is.EqualTo(7));
        Assert.That(bus.LatchedFault.RegionTag, Is.EqualTo(RegionTag.Rom));
        Assert.That(bus.LatchedFault.Intent, Is.EqualTo(AccessIntent.DataWrite));
        Assert.That(permission.Fault.Kind, Is.EqualTo(FaultKind.Permission));

        bus.ClearFaultStatus();
        var nx = bus.TryRead8(Access(0x0100, AccessIntent.InstructionFetch, BusAccessMode.Atomic, cycle: 12));
        Assert.Multiple(() =>
        {
            Assert.That(nx.Fault.Kind, Is.EqualTo(FaultKind.Nx));
            AssertLowByte(bus, FaultKind.Nx, 0x0100);
        });

        bus.ClearFaultStatus();
        var decomposed = bus.TryRead8(Access(0x0100, AccessIntent.InstructionFetch, BusAccessMode.Decomposed));
        Assert.Multiple(() =>
        {
            Assert.That(decomposed.Ok, Is.True);
            Assert.That(decomposed.Value, Is.EqualTo(0xEA));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
        });
    }

    /// <summary>
    /// An unmapped write and an unmapped instruction fetch do not set high-byte flags.
    /// </summary>
    [Test]
    public void UnmappedWriteAndFetch_DoNotSetHighByte()
    {
        var writeBus = new MainBus();
        writeBus.TryWrite8(Access(0x3000, AccessIntent.DataWrite), 0x01);
        AssertLowByte(writeBus, FaultKind.Unmapped, 0x3000);

        var fetchBus = new MainBus();
        fetchBus.TryRead8(Access(0x3100, AccessIntent.InstructionFetch));
        AssertLowByte(fetchBus, FaultKind.Unmapped, 0x3100);
    }

    /// <summary>
    /// A later permission fault does not replace an earlier unmapped latch.
    /// </summary>
    [Test]
    public void FirstFaultWins_AcrossKinds()
    {
        var bus = new MainBus();
        var memory = new PhysicalMemory(PageSize, "rom");
        bus.MapPage(0, new(4, RegionTag.Rom, PagePerms.Read, TargetCaps.SupportsPeek, new RomTarget(memory.Slice(0, PageSize)), 0));
        bus.TryRead8(Access(0x5000, AccessIntent.DataRead, cycle: 2, sourceId: 1));
        bus.TryWrite8(Access(0x0010, AccessIntent.DataWrite, cycle: 20, sourceId: 9), 0x55);

        Assert.Multiple(() =>
        {
            AssertLowByte(bus, FaultKind.Unmapped, 0x5000);
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(2ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(1));
            Assert.That(bus.LatchedFault.DeviceId, Is.EqualTo(-1));
        });
    }

    /// <summary>
    /// Debug reads and writes do not latch and do not replace a latched fault.
    /// </summary>
    [Test]
    public void DebugAccess_DoesNotLatch()
    {
        var bus = new MainBus();
        var read = bus.TryRead8(Access(0x5000, AccessIntent.DebugRead));
        var write = bus.TryWrite8(Access(0x6000, AccessIntent.DebugWrite), 0x42);

        Assert.Multiple(() =>
        {
            Assert.That(read.Failed, Is.True);
            Assert.That(write.Failed, Is.True);
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
        });

        bus.TryRead8(Access(0x1111, AccessIntent.DataRead, cycle: 3));
        bus.TryRead8(Access(0x2222, AccessIntent.DebugRead));
        bus.TryWrite8(Access(0x3333, AccessIntent.DebugWrite), 0x01);
        bus.TryRead16(Access(0x4444, AccessIntent.DebugRead, widthBits: 16));

        Assert.Multiple(() =>
        {
            AssertLowByte(bus, FaultKind.Unmapped, 0x1111);
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(3ul));
        });
    }

    /// <summary>
    /// A composite hole reads <c>0xFF</c> as success and does not latch.
    /// </summary>
    [Test]
    public void CompositeHole_ReadsFf_AndDoesNotLatch()
    {
        var bus = new MainBus();
        bus.MapPage(0, new(1, RegionTag.Io, PagePerms.ReadWrite, TargetCaps.SupportsPeek, new HoleComposite(), 0));
        bus.TryRead8(Access(0x2000, AccessIntent.DataRead));

        var hole = bus.TryRead8(Access(0x0010, AccessIntent.DataRead));
        bus.TryWrite8(Access(0x0011, AccessIntent.DataWrite), 0x00);
        bus.Read8(Access(0x0012, AccessIntent.DataRead));

        Assert.Multiple(() =>
        {
            Assert.That(hole.Ok, Is.True);
            Assert.That(hole.Value, Is.EqualTo(0xFF));
            AssertLowByte(bus, FaultKind.Unmapped, 0x2000);
        });
    }

    /// <summary>
    /// A silent floating-bus read on the non-<c>Try</c> path does not latch, even when recording is on.
    /// </summary>
    [Test]
    public void SilentRead8_DoesNotLatch()
    {
        var ring = new BusFaultRing();
        var bus = new MainBus(faultRecorder: ring) { FaultRecordingEnabled = true };
        bus.TryRead8(Access(0x1000, AccessIntent.DataRead));
        byte value = bus.Read8(Access(0x2000, AccessIntent.DataRead));
        bus.Write8(Access(0x3000, AccessIntent.DataWrite), 0x42);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(0xFF));
            Assert.That(ring.Count, Is.EqualTo(3));
            AssertLowByte(bus, FaultKind.Unmapped, 0x1000);
        });
    }

    /// <summary>
    /// Clearing status zeroes the address and the private latch, and the next fault can latch.
    /// </summary>
    [Test]
    public void ClearFaultStatus_ZeroesAddressAndLatch()
    {
        var bus = new MainBus();
        bus.TryRead8(Access(0x1111, AccessIntent.DataRead, cycle: 6, sourceId: 4));
        bus.ClearFaultStatus();

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
            Assert.That(bus.LatchedFault.Kind, Is.EqualTo(FaultKind.None));
            Assert.That(bus.LatchedFault.Address, Is.EqualTo(0u));
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(0ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(0));
            Assert.That(bus.LatchedFault.DeviceId, Is.EqualTo(0));
        });

        bus.TryRead8(Access(0x2222, AccessIntent.DataRead, cycle: 8));
        AssertLowByte(bus, FaultKind.Unmapped, 0x2222);
        Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(8ul));
    }

    /// <summary>
    /// Reset of the bus zeroes both registers.
    /// </summary>
    [Test]
    public void Reset_ZeroesStatusAndAddress()
    {
        var bus = new MainBus();
        bus.TryWrite8(Access(0x4444, AccessIntent.DataWrite, cycle: 15, sourceId: 5), 0x01);
        bus.OnResetAsserted(new Cycle(1));

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
            Assert.That(bus.LatchedFault.Cycle, Is.EqualTo(0ul));
            Assert.That(bus.LatchedFault.SourceId, Is.EqualTo(0));
        });

        bus.TryRead8(Access(0x5555, AccessIntent.DataRead));
        bus.OnResetReleased(new Cycle(2));
        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
        });
    }

    /// <summary>
    /// A machine reset clears both registers.
    /// </summary>
    [Test]
    public void MachineReset_ZeroesStatusAndAddress()
    {
        var bus = new MainBus(16);
        bus.TryRead8(Access(0x4000, AccessIntent.DataRead, cycle: 3));
        var cpu = new Mock<ICpu>();
        cpu.Setup(c => c.Halted).Returns(false);
        cpu.Setup(c => c.IsStopRequested).Returns(false);
        cpu.Setup(c => c.Step()).Returns(new CpuStepResult(CpuRunState.Running, 1));
        var scheduler = new Scheduler();
        var machine = new Machine(cpu.Object, bus, scheduler, new SignalBus(), new DeviceRegistry());
        scheduler.SetEventContext(machine);

        if (machine.State == MachineState.Off)
        {
            machine.PowerOnAsync().GetAwaiter().GetResult();
        }

        machine.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
            Assert.That(bus.LatchedFault.IsSuccess, Is.True);
        });
    }

    private static void AssertLowByte(MainBus bus, FaultKind kind, Addr address)
    {
        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(kind));
            Assert.That(bus.FaultAddress, Is.EqualTo(address));
            Assert.That(bus.FaultStatus & HighByte, Is.EqualTo(FaultKind.None));
            Assert.That((byte)bus.FaultStatus, Is.EqualTo((byte)kind));
        });
    }

    private static MainBus MapRam(out byte[] raw)
    {
        raw = new byte[PageSize];
        raw[0x0010] = 0x5A;
        var bus = new MainBus();
        bus.MapPage(0, Entry(new RamTarget(raw, "ram"), PagePerms.All));
        return bus;
    }

    private static PageEntry Entry(IBusTarget target, PagePerms perms) =>
        new(1, RegionTag.Ram, perms, target.Capabilities, target, 0);

    private static BusAccess Access(
        Addr address,
        AccessIntent intent,
        BusAccessMode mode = BusAccessMode.Decomposed,
        byte widthBits = 8,
        ulong cycle = 0,
        int sourceId = 0) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: mode,
            EmulationFlag: mode == BusAccessMode.Decomposed,
            Intent: intent,
            SourceId: sourceId,
            Cycle: cycle,
            Flags: AccessFlags.None);

    /// <summary>
    /// A composite page whose every offset is a hole.
    /// </summary>
    private sealed class HoleComposite : ICompositeTarget
    {
        public string Name => "hole";

        public TargetCaps Capabilities => TargetCaps.SupportsPeek;

        public byte Read8(Addr offset, in BusAccess access) => 0xFF;

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            // A hole drops the store.
        }

        public IBusTarget? ResolveTarget(Addr offset, AccessIntent intent) => null;

        public RegionTag GetSubRegionTag(Addr offset) => RegionTag.Io;

        public IEnumerable<(Addr StartOffset, Addr Size, RegionTag Tag, string TargetName)> EnumerateSubRegions()
        {
            yield break;
        }
    }

    /// <summary>
    /// Counts byte reads so a wide access can show it stopped at the fault.
    /// </summary>
    private sealed class CountingTarget : IBusTarget
    {
        private readonly byte[] bytes;

        public CountingTarget(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public int Read8Count { get; private set; }

        public string Name => "counting";

        public TargetCaps Capabilities => TargetCaps.SupportsPeek | TargetCaps.SupportsPoke;

        public byte Read8(Addr offset, in BusAccess access)
        {
            Read8Count++;
            return bytes[(int)offset];
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            bytes[(int)offset] = value;
        }
    }
}