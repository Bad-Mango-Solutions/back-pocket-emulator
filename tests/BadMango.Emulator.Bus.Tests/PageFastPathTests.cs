// <copyright file="PageFastPathTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Tests;

using System.Buffers;

using Interfaces;

/// <summary>
/// Tests for the page-entry fast-path fields published by <see cref="MainBus"/>.
/// </summary>
[TestFixture]
public class PageFastPathTests
{
    private const int PageBytes = 0x1000;

    private const FastPathOps RamAllOps = FastPathOps.Read | FastPathOps.Write | FastPathOps.Fetch;

    private const FastPathOps ReadFetchOps = FastPathOps.Read | FastPathOps.Fetch;

    /// <summary>
    /// A <see cref="RamTarget"/> mapped from <see cref="PhysicalMemory"/> with
    /// <see cref="PagePerms.All"/> publishes read, write, and fetch.
    /// </summary>
    [Test]
    public void MapPage_RamFromPhysicalMemory_AllPerms_PublishesReadWriteFetch()
    {
        var pool = new PhysicalMemory(PageBytes, "ram");
        pool.AsSpan()[0x123] = 0x5A;
        var target = new RamTarget(pool.Slice(0, PageBytes), "ram");
        var bus = new MainBus();

        bus.MapPage(0, Entry(target, PagePerms.All, target.Capabilities, RegionTag.Ram, isSealed: true));

        PageEntry page = bus.GetPageEntryByIndex(0);
        AssertFast(page, pool, fastBase: 0, RamAllOps);
        AssertIndexedByte(page, 0x123, 0x5A);
        Assert.That(page.IsSealed, Is.True);
    }

    /// <summary>
    /// A <see cref="RomTarget"/> publishes read and fetch and never write, including when the
    /// page permissions include write and when the slice is a <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    [Test]
    public void MapPage_Rom_PublishesReadAndFetchNotWrite()
    {
        var pool = new PhysicalMemory(PageBytes, "rom");
        pool.AsSpan()[4] = 0xC3;
        ReadOnlyMemory<byte> readOnly = pool.Memory.Slice(0, PageBytes);
        var readOnlyRom = new RomTarget(readOnly, "rom-readonly");
        var writableRom = new RomTarget(pool.Slice(0, PageBytes), "rom-writable");
        var bus = new MainBus();

        bus.MapPage(0, Entry(readOnlyRom, PagePerms.ReadExecute, readOnlyRom.Capabilities, RegionTag.Rom));
        bus.MapPage(1, Entry(writableRom, PagePerms.All, writableRom.Capabilities, RegionTag.Rom));

        PageEntry readOnlyPage = bus.GetPageEntryByIndex(0);
        PageEntry writablePage = bus.GetPageEntryByIndex(1);
        Assert.Multiple(() =>
        {
            AssertFast(readOnlyPage, pool, fastBase: 0, ReadFetchOps);
            AssertIndexedByte(readOnlyPage, 4, 0xC3);
            AssertFast(writablePage, pool, fastBase: 0, ReadFetchOps);
            Assert.That(writablePage.Perms, Is.EqualTo(PagePerms.All));
            Assert.That(writablePage.FastOps.HasFlag(FastPathOps.Write), Is.False);
        });
    }

    /// <summary>
    /// Permission bits select operations. Fetch requires read and execute. A page with no
    /// permission bits keeps the backing array and publishes no operations.
    /// </summary>
    [Test]
    public void MapPage_PermissionBits_SelectFastOps()
    {
        var pool = new PhysicalMemory(PageBytes, "ram");
        var target = new RamTarget(pool.Slice(0, PageBytes));
        var bus = new MainBus();

        bus.MapPage(0, Entry(target, PagePerms.ReadExecute, target.Capabilities));
        bus.MapPage(1, Entry(target, PagePerms.Write, target.Capabilities));
        bus.MapPage(2, Entry(target, PagePerms.Execute, target.Capabilities));
        bus.MapPage(3, Entry(target, PagePerms.None, target.Capabilities));

        Assert.Multiple(() =>
        {
            AssertFast(bus.GetPageEntryByIndex(0), pool, 0, ReadFetchOps);
            AssertFast(bus.GetPageEntryByIndex(1), pool, 0, FastPathOps.Write);
            AssertFast(bus.GetPageEntryByIndex(2), pool, 0, FastPathOps.None);
            AssertFast(bus.GetPageEntryByIndex(3), pool, 0, FastPathOps.None);
        });
    }

    /// <summary>
    /// Region tag is not an eligibility input. Shadow, I/O, and video tags stay on the fast path
    /// when the target is plain RAM.
    /// </summary>
    /// <param name="tag">The region tag stored on the page.</param>
    [TestCase(RegionTag.Shadow)]
    [TestCase(RegionTag.Io)]
    [TestCase(RegionTag.Video)]
    public void MapPage_RegionTag_DoesNotExcludeRam(RegionTag tag)
    {
        var pool = new PhysicalMemory(PageBytes, "ram");
        var target = new RamTarget(pool.Slice(0, PageBytes));
        var bus = new MainBus();

        bus.MapPage(0, Entry(target, PagePerms.All, target.Capabilities, tag));

        AssertFast(bus.GetPageEntryByIndex(0), pool, 0, RamAllOps);
    }

    /// <summary>
    /// A composite page, a device target, a null target, and caller-supplied fast-path fields
    /// on those pages all publish a cleared fast path.
    /// </summary>
    [Test]
    public void MapPage_DeviceCompositeAndNull_ClearFastPath()
    {
        var composite = new DefaultCompositeTarget("IO");
        var device = new MockTarget();
        var lie = new byte[] { 9 };
        var bus = new MainBus();

        bus.MapPage(0xC, new PageEntry(
            1,
            RegionTag.Composite,
            PagePerms.ReadWrite,
            composite.Capabilities,
            composite,
            0,
            FastBacking: lie,
            FastBase: 4,
            FastOps: FastPathOps.Read));
        bus.MapRegion(0xC000, PageBytes, 2, RegionTag.Io, PagePerms.ReadWrite, composite.Capabilities, composite, 0);
        bus.MapPage(1, Entry(device, PagePerms.All, TargetCaps.None, RegionTag.Io, fastBacking: lie, fastBase: 3, fastOps: FastPathOps.Write));
        bus.MapPage(2, new PageEntry(3, RegionTag.Unmapped, PagePerms.None, TargetCaps.None, null!, 0, FastBacking: lie, FastOps: FastPathOps.Read));

        Assert.Multiple(() =>
        {
            AssertSlow(bus.GetPageEntry(0xC000));
            Assert.That(bus.GetPageEntry(0xC000).Target, Is.SameAs(composite));
            AssertSlow(bus.GetPageEntryByIndex(1));
            AssertSlow(bus.GetPageEntryByIndex(2));
        });
    }

    /// <summary>
    /// Side effects, timing sensitivity, and any non-default privilege keep the page slow.
    /// </summary>
    /// <param name="caps">Capability flags stored on the page.</param>
    /// <param name="read">Minimum read privilege.</param>
    /// <param name="write">Minimum write privilege.</param>
    /// <param name="execute">Minimum execute privilege.</param>
    [TestCase(TargetCaps.HasSideEffects, PrivilegeLevel.User, PrivilegeLevel.User, PrivilegeLevel.User)]
    [TestCase(TargetCaps.TimingSensitive, PrivilegeLevel.User, PrivilegeLevel.User, PrivilegeLevel.User)]
    [TestCase(TargetCaps.SupportsPeek | TargetCaps.HasSideEffects, PrivilegeLevel.User, PrivilegeLevel.User, PrivilegeLevel.User)]
    [TestCase(TargetCaps.None, PrivilegeLevel.Kernel, PrivilegeLevel.User, PrivilegeLevel.User)]
    [TestCase(TargetCaps.None, PrivilegeLevel.User, PrivilegeLevel.Hypervisor, PrivilegeLevel.User)]
    [TestCase(TargetCaps.None, PrivilegeLevel.User, PrivilegeLevel.User, PrivilegeLevel.Reserved)]
    public void MapPage_RestrictedPage_StaysSlow(TargetCaps caps, PrivilegeLevel read, PrivilegeLevel write, PrivilegeLevel execute)
    {
        var pool = new PhysicalMemory(PageBytes, "ram");
        var target = new RamTarget(pool.Slice(0, PageBytes));
        var bus = new MainBus();

        bus.MapPage(0, new PageEntry(
            1,
            RegionTag.Ram,
            PagePerms.All,
            caps,
            target,
            0,
            read,
            write,
            execute,
            FastBacking: [1],
            FastBase: 8,
            FastOps: RamAllOps));

        AssertSlow(bus.GetPageEntryByIndex(0));
    }

    /// <summary>
    /// A slice that is not array-backed stays slow, and the checked path can still read it.
    /// </summary>
    [Test]
    public void MapPage_NonArraySlice_StaysSlow()
    {
        var manager = new NonArrayMemoryManager(PageBytes);
        manager.GetSpan()[7] = 0x77;
        var target = new RamTarget(manager.Memory, "custom");
        var bus = new MainBus();

        bus.MapPage(0, Entry(target, PagePerms.All, target.Capabilities));

        AssertSlow(bus.GetPageEntryByIndex(0));
        byte value = bus.Read8(Access(0x0007, AccessIntent.DataRead));
        Assert.That(value, Is.EqualTo(0x77));
    }

    /// <summary>
    /// A window that runs off the end of the slice, a short target, and a physical base that
    /// overflows the offset add all stay slow.
    /// </summary>
    [Test]
    public void MapPage_WindowDoesNotFit_StaysSlow()
    {
        var full = new PhysicalMemory(PageBytes, "full");
        var shortPool = new PhysicalMemory(0x800, "short");
        var fullTarget = new RamTarget(full.Slice(0, PageBytes));
        var shortTarget = new RamTarget(shortPool.Slice(0, 0x800));
        var bus = new MainBus();

        bus.MapPage(0, Entry(fullTarget, PagePerms.All, fullTarget.Capabilities, physicalBase: 1));
        bus.MapPage(1, Entry(fullTarget, PagePerms.All, fullTarget.Capabilities, physicalBase: PageBytes));
        bus.MapPage(2, Entry(shortTarget, PagePerms.All, shortTarget.Capabilities));
        bus.MapPage(3, Entry(fullTarget, PagePerms.All, fullTarget.Capabilities, physicalBase: 0xFFFFF000));

        Assert.Multiple(() =>
        {
            AssertSlow(bus.GetPageEntryByIndex(0));
            AssertSlow(bus.GetPageEntryByIndex(1));
            AssertSlow(bus.GetPageEntryByIndex(2));
            AssertSlow(bus.GetPageEntryByIndex(3));
        });
    }

    /// <summary>
    /// <see cref="PageEntry.FastBase"/> is the array index of target-local offset 0, so a slice
    /// that does not start at index 0 still addresses the right byte.
    /// </summary>
    [Test]
    public void MapPage_OffsetSlice_FastBaseIsIndexOfOffsetZero()
    {
        var pool = new PhysicalMemory(0x4000, "ram");
        pool.AsSpan()[0x800] = 0x11;
        pool.AsSpan()[0x800 + 0x1000 + 0x20] = 0x22;
        var target = new RamTarget(pool.Slice(0x800, 0x2000), "sliced");
        var bus = new MainBus();

        bus.MapPageRange(4, 2, 1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);

        PageEntry first = bus.GetPageEntryByIndex(4);
        PageEntry second = bus.GetPageEntryByIndex(5);
        Assert.Multiple(() =>
        {
            AssertFast(first, pool, fastBase: 0x800, RamAllOps);
            Assert.That(first.PhysicalBase, Is.EqualTo(0u));
            AssertIndexedByte(first, 0, 0x11);
            AssertFast(second, pool, fastBase: 0x800, RamAllOps);
            Assert.That(second.PhysicalBase, Is.EqualTo(0x1000u));
            AssertIndexedByte(second, 0x20, 0x22);
        });
    }

    /// <summary>
    /// <see cref="MainBus.MapRegion"/>, <see cref="MainBus.MapPageAt"/>, and
    /// <see cref="MainBus.SetPageEntry"/> recompute the fields, and a caller-supplied array is ignored.
    /// </summary>
    [Test]
    public void MapRegion_MapPageAt_SetPageEntry_RecomputeAndIgnoreCallerFields()
    {
        var pool = new PhysicalMemory(PageBytes * 2, "ram");
        pool.AsSpan()[PageBytes + 9] = 0x44;
        var target = new RamTarget(pool.Slice(0, PageBytes * 2));
        var lie = new byte[] { 1, 2, 3 };
        var bus = new MainBus();

        bus.MapRegion(0x4000, PageBytes * 2, 7, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        bus.MapPageAt(0x2000, Entry(target, PagePerms.ReadWrite, target.Capabilities, physicalBase: PageBytes, fastBacking: lie, fastBase: 50, fastOps: FastPathOps.None));
        bus.SetPageEntry(1, Entry(target, PagePerms.Read, TargetCaps.None, physicalBase: 0, fastBacking: lie, fastOps: FastPathOps.Write));

        Assert.Multiple(() =>
        {
            AssertFast(bus.GetPageEntry(0x4000), pool, 0, RamAllOps);
            AssertFast(bus.GetPageEntry(0x5000), pool, 0, RamAllOps);
            Assert.That(bus.GetPageEntry(0x5000).PhysicalBase, Is.EqualTo((Addr)PageBytes));
            AssertIndexedByte(bus.GetPageEntry(0x5000), 9, 0x44);
            PageEntry at = bus.GetPageEntryByIndex(2);
            AssertFast(at, pool, 0, FastPathOps.Read | FastPathOps.Write);
            Assert.That(at.PhysicalBase, Is.EqualTo((Addr)PageBytes));
            AssertFast(bus.GetPageEntryByIndex(1), pool, 0, FastPathOps.Read);
        });
    }

    /// <summary>
    /// Both <see cref="MainBus.RemapPage(int, IBusTarget, Addr)"/> overloads and
    /// <see cref="MainBus.RemapPageRange"/> replace the fast-path fields with the new target.
    /// A ROM remap drops write even when the previous permissions included it.
    /// </summary>
    [Test]
    public void RemapPage_ReplacesFastPath()
    {
        var ramPool = new PhysicalMemory(PageBytes * 2, "ram");
        ramPool.AsSpan()[0] = 0x01;
        var romPool = new PhysicalMemory(PageBytes, "rom");
        romPool.AsSpan()[0] = 0x02;
        var ram = new RamTarget(ramPool.Slice(0, PageBytes * 2));
        var rom = new RomTarget(romPool.Slice(0, PageBytes));
        var bus = new MainBus();
        bus.MapPageRange(0, 2, 1, RegionTag.Ram, PagePerms.All, ram.Capabilities, ram, 0);

        bus.RemapPage(0, rom, 0);
        bus.RemapPage(1, Entry(rom, PagePerms.ReadExecute, rom.Capabilities, RegionTag.Rom));

        PageEntry first = bus.GetPageEntryByIndex(0);
        PageEntry second = bus.GetPageEntryByIndex(1);
        Assert.Multiple(() =>
        {
            Assert.That(first.Perms, Is.EqualTo(PagePerms.All), "target remap keeps the previous permissions");
            AssertFast(first, romPool, 0, ReadFetchOps);
            AssertIndexedByte(first, 0, 0x02);
            AssertFast(second, romPool, 0, ReadFetchOps);
        });

        bus.RemapPageRange(0, 2, ram, 0);
        Assert.Multiple(() =>
        {
            Assert.That(bus.GetPageEntryByIndex(0).Perms, Is.EqualTo(PagePerms.All));
            AssertFast(bus.GetPageEntryByIndex(0), ramPool, 0, RamAllOps);
            AssertIndexedByte(bus.GetPageEntryByIndex(0), 0, 0x01);
            Assert.That(bus.GetPageEntryByIndex(1).Perms, Is.EqualTo(PagePerms.ReadExecute), "range remap keeps the permissions stored on each page");
            Assert.That(bus.GetPageEntryByIndex(1).Target, Is.SameAs(ram));
            AssertFast(bus.GetPageEntryByIndex(1), ramPool, 0, ReadFetchOps);
            Assert.That(bus.GetPageEntryByIndex(1).PhysicalBase, Is.EqualTo((Addr)PageBytes));
        });
    }

    /// <summary>
    /// <see cref="MainBus.SelectSwapVariant"/> between two banks of one array changes
    /// <see cref="PageEntry.FastBase"/> before it returns. Two arrays change
    /// <see cref="PageEntry.FastBacking"/>.
    /// </summary>
    [Test]
    public void SelectSwapVariant_ChangesFastBaseOrBackingBeforeReturn()
    {
        var shared = new PhysicalMemory(PageBytes * 2, "shared");
        shared.AsSpan()[0] = 0xA1;
        shared.AsSpan()[PageBytes] = 0xA2;
        var bank1 = new RamTarget(shared.Slice(0, PageBytes), "bank1");
        var bank2 = new RamTarget(shared.Slice((uint)PageBytes, PageBytes), "bank2");
        var otherPool = new PhysicalMemory(PageBytes, "other");
        otherPool.AsSpan()[0] = 0xB2;
        var other = new RamTarget(otherPool.Slice(0, PageBytes), "other");
        var bus = new MainBus();
        bus.MapPage(0xD, Entry(bank1, PagePerms.All, bank1.Capabilities));

        uint sharedGroup = bus.CreateSwapGroup("shared", 0xD000, PageBytes);
        bus.AddSwapVariant(sharedGroup, "BANK1", bank1, 0, PagePerms.All);
        bus.AddSwapVariant(sharedGroup, "BANK2", bank2, 0, PagePerms.ReadExecute);

        bus.SelectSwapVariant(sharedGroup, "BANK1");
        PageEntry low = bus.GetPageEntryByIndex(0xD);
        AssertFast(low, shared, fastBase: 0, RamAllOps);
        AssertIndexedByte(low, 0, 0xA1);

        bus.SelectSwapVariant(sharedGroup, "BANK2");
        PageEntry high = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(high.FastBacking, Is.SameAs(low.FastBacking));
            Assert.That(high.FastBase, Is.Not.EqualTo(low.FastBase));
            AssertFast(high, shared, fastBase: PageBytes, ReadFetchOps);
            AssertIndexedByte(high, 0, 0xA2);
        });

        uint separateGroup = bus.CreateSwapGroup("separate", 0xE000, PageBytes);
        bus.MapPage(0xE, Entry(bank1, PagePerms.All, bank1.Capabilities));
        bus.AddSwapVariant(separateGroup, "BANK1", bank1, 0, PagePerms.All);
        bus.AddSwapVariant(separateGroup, "OTHER", other, 0, PagePerms.All);
        bus.SelectSwapVariant(separateGroup, "OTHER");

        PageEntry swapped = bus.GetPageEntryByIndex(0xE);
        AssertFast(swapped, otherPool, fastBase: 0, RamAllOps);
        AssertIndexedByte(swapped, 0, 0xB2);
        Assert.That(swapped.FastBacking, Is.Not.SameAs(low.FastBacking));
    }

    /// <summary>
    /// Activating a layer, changing its permissions, and deactivating it each republish the
    /// fast-path fields for the effective target.
    /// </summary>
    [Test]
    public void LayerActivatePermissionsAndDeactivate_RepublishFastPath()
    {
        var romPool = new PhysicalMemory(PageBytes, "rom");
        romPool.AsSpan()[0] = 0xA0;
        var ramPool = new PhysicalMemory(PageBytes, "ram");
        ramPool.AsSpan()[0] = 0xB0;
        var rom = new RomTarget(romPool.Slice(0, PageBytes));
        var ram = new RamTarget(ramPool.Slice(0, PageBytes));
        var bus = new MainBus();
        bus.MapPage(0, Entry(rom, PagePerms.ReadExecute, rom.Capabilities, RegionTag.Rom));
        bus.SaveBaseMapping(0);

        var layer = bus.CreateLayer("overlay", 10);
        bus.AddLayeredMapping(new LayeredMapping(
            0,
            PageBytes,
            layer,
            2,
            RegionTag.Ram,
            PagePerms.ReadWrite,
            ram.Capabilities,
            ram,
            0));

        AssertFast(bus.GetPageEntryByIndex(0), romPool, 0, ReadFetchOps);

        bus.ActivateLayer("overlay");
        AssertFast(bus.GetPageEntryByIndex(0), ramPool, 0, FastPathOps.Read | FastPathOps.Write);
        AssertIndexedByte(bus.GetPageEntryByIndex(0), 0, 0xB0);

        bus.SetLayerPermissions("overlay", PagePerms.All);
        AssertFast(bus.GetPageEntryByIndex(0), ramPool, 0, RamAllOps);

        bus.SetLayerPermissions("overlay", PagePerms.ReadExecute);
        AssertFast(bus.GetPageEntryByIndex(0), ramPool, 0, ReadFetchOps);

        bus.DeactivateLayer("overlay");
        AssertFast(bus.GetPageEntryByIndex(0), romPool, 0, ReadFetchOps);
        AssertIndexedByte(bus.GetPageEntryByIndex(0), 0, 0xA0);
    }

    /// <summary>
    /// <see cref="MainBus.Clear"/> zeroes target storage and leaves the fast-path fields in place.
    /// </summary>
    [Test]
    public void Clear_ZeroesStorageWithoutChangingFastPath()
    {
        var pool = new PhysicalMemory(PageBytes, "ram");
        pool.AsSpan()[0] = 0x42;
        var target = new RamTarget(pool.Slice(0, PageBytes));
        var bus = new MainBus();
        bus.MapPage(0, Entry(target, PagePerms.All, target.Capabilities));
        byte[]? backing = bus.GetPageEntryByIndex(0).FastBacking;
        int fastBase = bus.GetPageEntryByIndex(0).FastBase;

        bus.Clear();

        PageEntry page = bus.GetPageEntryByIndex(0);
        Assert.Multiple(() =>
        {
            Assert.That(page.FastBacking, Is.SameAs(backing));
            Assert.That(page.FastBase, Is.EqualTo(fastBase));
            Assert.That(page.FastOps, Is.EqualTo(RamAllOps));
            AssertIndexedByte(page, 0, 0x00);
        });
    }

    /// <summary>
    /// The slice accessors return the memory the fast path indexes.
    /// </summary>
    [Test]
    public void SliceAccessors_ReturnConstructedWindow()
    {
        var pool = new PhysicalMemory(0x3000, "pool");
        var ram = new RamTarget(pool.Slice(0x1000, PageBytes), "ram");
        var rom = new RomTarget(pool.Memory.Slice(0x2000, PageBytes), "rom");

        Assert.Multiple(() =>
        {
            Assert.That(ram.GetBackingSlice().Length, Is.EqualTo(PageBytes));
            Assert.That(rom.GetBackingSlice().Length, Is.EqualTo(PageBytes));
            Assert.That(MemoryMarshalArray(ram.GetBackingSlice()), Is.SameAs(MemoryMarshalArray(pool.Memory)));
            Assert.That(MemoryMarshalArray(rom.GetBackingSlice()), Is.SameAs(MemoryMarshalArray(pool.Memory)));
        });
    }

    private static PageEntry Entry(
        IBusTarget target,
        PagePerms perms,
        TargetCaps caps,
        RegionTag tag = RegionTag.Ram,
        Addr physicalBase = 0,
        bool isSealed = false,
        byte[]? fastBacking = null,
        int fastBase = 0,
        FastPathOps fastOps = FastPathOps.None)
    {
        return new PageEntry(
            1,
            tag,
            perms,
            caps,
            target,
            physicalBase,
            IsSealed: isSealed,
            FastBacking: fastBacking,
            FastBase: fastBase,
            FastOps: fastOps);
    }

    private static void AssertFast(PageEntry page, PhysicalMemory pool, int fastBase, FastPathOps ops)
    {
        byte[] array = MemoryMarshalArray(pool.Memory)!;
        Assert.Multiple(() =>
        {
            Assert.That(page.FastBacking, Is.SameAs(array));
            Assert.That(page.FastBase, Is.EqualTo(fastBase));
            Assert.That(page.FastOps, Is.EqualTo(ops));
        });
    }

    private static void AssertSlow(PageEntry page)
    {
        Assert.Multiple(() =>
        {
            Assert.That(page.FastBacking, Is.Null);
            Assert.That(page.FastBase, Is.Zero);
            Assert.That(page.FastOps, Is.EqualTo(FastPathOps.None));
        });
    }

    private static void AssertIndexedByte(PageEntry page, int pageOffset, byte expected)
    {
        Assert.That(page.FastBacking, Is.Not.Null);
        int index = page.FastBase + (int)page.PhysicalBase + pageOffset;
        Assert.That(page.FastBacking![index], Is.EqualTo(expected));
    }

    private static byte[]? MemoryMarshalArray(ReadOnlyMemory<byte> memory)
    {
        return System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment)
            ? segment.Array
            : null;
    }

    private static BusAccess Access(Addr address, AccessIntent intent)
    {
        return new BusAccess(
            address,
            0,
            8,
            BusAccessMode.Decomposed,
            true,
            intent,
            0,
            0,
            AccessFlags.None);
    }

    private sealed class MockTarget : IBusTarget
    {
        public string Name => "device";

        public TargetCaps Capabilities => TargetCaps.HasSideEffects;

        public byte Read8(Addr offset, in BusAccess access) => 0;

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            // The fast-path tests never write through this device target.
        }
    }

    /// <summary>
    /// A <see cref="MemoryManager{T}"/> whose <see cref="Memory{T}"/> is not a <see cref="byte"/> array.
    /// </summary>
    private sealed class NonArrayMemoryManager : MemoryManager<byte>
    {
        private readonly byte[] storage;

        public NonArrayMemoryManager(int length)
        {
            storage = new byte[length];
        }

        /// <inheritdoc />
        public override Span<byte> GetSpan() => storage;

        /// <inheritdoc />
        public override MemoryHandle Pin(int elementIndex = 0) => default;

        /// <inheritdoc />
        public override void Unpin()
        {
            // TryGetArray does not pin this manager.
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            // The test keeps the manager alive for the assertion.
        }
    }
}