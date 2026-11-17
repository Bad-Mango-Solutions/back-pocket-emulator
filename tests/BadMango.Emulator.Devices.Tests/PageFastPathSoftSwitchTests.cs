// <copyright file="PageFastPathSoftSwitchTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Core.Signaling;

using Moq;

/// <summary>
/// Tests that language-card and auxiliary-memory soft switches republish fast-path fields
/// for the mapping they install, and clear them for composite and split targets.
/// </summary>
[TestFixture]
public class PageFastPathSoftSwitchTests
{
    private const FastPathOps RamAllOps = FastPathOps.Read | FastPathOps.Write | FastPathOps.Fetch;

    private const FastPathOps ReadFetchOps = FastPathOps.Read | FastPathOps.Fetch;

    /// <summary>
    /// Language-card reads of $C080, $C081, $C082, and $C083 move $D000/$E000 between ROM,
    /// read-only RAM, read/write RAM, and the split target, and the fast-path fields follow.
    /// </summary>
    [Test]
    public void LanguageCard_C080_C081_C082_C083_RepublishFastPath()
    {
        var romPool = new PhysicalMemory(0x3000, "SYSROM");
        romPool.AsSpan()[0x10] = 0xD1;
        romPool.AsSpan()[0x1000 + 0x10] = 0xE1;
        romPool.AsSpan()[0x2000 + 0x10] = 0xF1;
        var rom = new RomTarget(romPool.Slice(0, 0x3000), "SYSROM");
        var io = new DefaultCompositeTarget("$C000");
        var bus = new MainBus();
        bus.MapRegion(0xD000, 0x3000, 1, RegionTag.Rom, PagePerms.ReadExecute, rom.Capabilities, rom, 0);
        bus.MapRegion(0xC000, 0x1000, 2, RegionTag.Composite, PagePerms.ReadWrite, io.Capabilities, io, 0);

        var card = new LanguageCardDevice();
        card.ConfigureMemory(bus, new DeviceRegistry());
        card.Initialize(new EventContext(new Scheduler(), new SignalBus(), bus));
        var dispatcher = new IOPageDispatcher();
        card.RegisterHandlers(dispatcher);

        PageEntry romPage = bus.GetPageEntryByIndex(0xD);
        byte[]? romBacking = romPage.FastBacking;
        AssertFast(romPage, romPool, 0, ReadFetchOps);
        AssertIndexedByte(romPage, 0x10, 0xD1);
        AssertFast(bus.GetPageEntryByIndex(0xE), romPool, 0, ReadFetchOps);
        Assert.That(bus.GetPageEntryByIndex(0xE).PhysicalBase, Is.EqualTo(0x1000u));
        AssertIndexedByte(bus.GetPageEntryByIndex(0xF), 0x10, 0xF1);
        AssertSlow(bus.GetPageEntry(0xC000));

        ReadSwitch(dispatcher, 0x80);
        PageEntry bank2 = bus.GetPageEntryByIndex(0xD);
        PageEntry highReadOnly = bus.GetPageEntryByIndex(0xE);
        Assert.That(bank2.Target, Is.InstanceOf<RamTarget>());
        Assert.That(bank2.FastOps, Is.EqualTo(ReadFetchOps));
        Assert.That(bank2.FastBacking, Is.Not.Null);
        Assert.That(bank2.FastBacking, Is.Not.SameAs(romBacking));
        Assert.That(highReadOnly.FastOps, Is.EqualTo(ReadFetchOps));
        Assert.That(highReadOnly.FastBacking, Is.Not.SameAs(romBacking));
        byte[]? bank2Backing = bank2.FastBacking;

        BusResult denied = bus.TryWrite8(Access(0xD010, AccessIntent.DataWrite), 0xB2);
        Assert.That(denied.Ok, Is.False, "read-only language-card RAM rejects the write on the checked path");
        AssertIndexedByte(bus.GetPageEntryByIndex(0xD), 0x10, 0x00);

        ReadSwitch(dispatcher, 0x83);
        Assert.That(bus.GetPageEntryByIndex(0xD).FastOps, Is.EqualTo(ReadFetchOps), "first $C083 read arms write and does not enable it");

        ReadSwitch(dispatcher, 0x83);
        PageEntry readWrite = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(readWrite.FastBacking, Is.SameAs(bank2Backing));
            Assert.That(readWrite.FastOps, Is.EqualTo(RamAllOps));
            Assert.That(bus.GetPageEntryByIndex(0xE).FastOps, Is.EqualTo(RamAllOps));
            Assert.That(bus.GetPageEntryByIndex(0xE).Target, Is.InstanceOf<RamTarget>());
        });

        bus.Write8(Access(0xD010, AccessIntent.DataWrite), 0xB2);
        bus.Write8(Access(0xE010, AccessIntent.DataWrite), 0xEE);
        bus.Write8(Access(0xF010, AccessIntent.DataWrite), 0xFF);
        AssertIndexedByte(bus.GetPageEntryByIndex(0xD), 0x10, 0xB2);
        AssertIndexedByte(bus.GetPageEntryByIndex(0xE), 0x10, 0xEE);
        AssertIndexedByte(bus.GetPageEntryByIndex(0xF), 0x10, 0xFF);
        Assert.That(bus.GetPageEntryByIndex(0xE).FastBacking, Is.SameAs(bus.GetPageEntryByIndex(0xF).FastBacking));
        Assert.That(bus.GetPageEntryByIndex(0xF).PhysicalBase, Is.EqualTo(0x1000u));

        ReadSwitch(dispatcher, 0x82);
        AssertFast(bus.GetPageEntryByIndex(0xD), romPool, 0, ReadFetchOps);
        AssertIndexedByte(bus.GetPageEntryByIndex(0xD), 0x10, 0xD1);
        AssertFast(bus.GetPageEntryByIndex(0xE), romPool, 0, ReadFetchOps);

        ReadSwitch(dispatcher, 0x81);
        AssertFast(bus.GetPageEntryByIndex(0xD), romPool, 0, ReadFetchOps);

        ReadSwitch(dispatcher, 0x81);
        PageEntry split = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(split.Target, Is.InstanceOf<LanguageCardSplitTarget>());
            AssertSlow(split);
            Assert.That(bus.GetPageEntryByIndex(0xE).Target, Is.InstanceOf<LanguageCardSplitTarget>());
            AssertSlow(bus.GetPageEntryByIndex(0xE));
            AssertSlow(bus.GetPageEntryByIndex(0xF));
        });

        card.Reset();
        AssertFast(bus.GetPageEntryByIndex(0xD), romPool, 0, ReadFetchOps);
        Assert.That(bus.GetPageEntryByIndex(0xD).Target, Is.InstanceOf<RomTarget>());
    }

    /// <summary>
    /// Selecting the other $D000 bank replaces <see cref="PageEntry.FastBacking"/> before
    /// <see cref="MainBus.SelectSwapVariant"/> returns to the language card.
    /// </summary>
    [Test]
    public void LanguageCard_BankSelect_ChangesFastBacking()
    {
        var romPool = new PhysicalMemory(0x3000, "SYSROM");
        var rom = new RomTarget(romPool.Slice(0, 0x3000), "SYSROM");
        var bus = new MainBus();
        bus.MapRegion(0xD000, 0x3000, 1, RegionTag.Rom, PagePerms.ReadExecute, rom.Capabilities, rom, 0);

        var card = new LanguageCardDevice();
        card.ConfigureMemory(bus, new DeviceRegistry());
        card.Initialize(new EventContext(new Scheduler(), new SignalBus(), bus));
        var dispatcher = new IOPageDispatcher();
        card.RegisterHandlers(dispatcher);

        ReadSwitch(dispatcher, 0x83);
        ReadSwitch(dispatcher, 0x83);
        bus.Write8(Access(0xD000, AccessIntent.DataWrite), 0x22);
        PageEntry bank2Page = bus.GetPageEntryByIndex(0xD);
        byte[]? bank2 = bank2Page.FastBacking;
        AssertIndexedByte(bank2Page, 0, 0x22);

        ReadSwitch(dispatcher, 0x8B);
        ReadSwitch(dispatcher, 0x8B);
        PageEntry bank1 = bus.GetPageEntryByIndex(0xD);
        Assert.Multiple(() =>
        {
            Assert.That(bank1.FastOps, Is.EqualTo(RamAllOps));
            Assert.That(bank1.FastBacking, Is.Not.Null);
            Assert.That(bank1.FastBacking, Is.Not.SameAs(bank2));
            AssertIndexedByte(bank1, 0, 0x00);
        });

        bus.Write8(Access(0xD000, AccessIntent.DataWrite), 0x11);
        AssertIndexedByte(bus.GetPageEntryByIndex(0xD), 0, 0x11);
        int bank2Index = bank2Page.FastBase + (int)bank2Page.PhysicalBase;
        Assert.That(bank2![bank2Index], Is.EqualTo(0x22), "bank 2 keeps the byte written before the swap");
    }

    /// <summary>
    /// 80STORE, PAGE2, HIRES, RAMRD, RAMWRT, ALTZP, 80COL, and INTCXROM leave fast-path fields
    /// consistent with the page that is actually mapped. Text page 1 stays inside the page-0
    /// target, so that page stays slow while its routed byte changes. The 4 KB remap is hi-res.
    /// </summary>
    [Test]
    public void AuxSoftSwitches_RepublishOrClearFastPath()
    {
        var mainPool = new PhysicalMemory(0xC000, "main-ram-48k");
        mainPool.AsSpan()[0x0050] = 0x5A;
        mainPool.AsSpan()[0x0400] = 0x41;
        mainPool.AsSpan()[0x1000] = 0x11;
        mainPool.AsSpan()[0x2000] = 0x21;
        mainPool.AsSpan()[0x3000] = 0x31;

        var builder = new MachineBuilder();
        builder.AddPhysicalMemory("main-ram-48k", mainPool);
        builder.ConfigureMemory((bus, registry) =>
        {
            var ram = builder.GetPhysicalMemory("main-ram-48k")!;
            var target = new RamTarget(ram.Slice(0, 0xC000));
            int deviceId = registry.GenerateId();
            registry.Register(deviceId, "RAM", "Main RAM", "Memory/MainRAM");
            bus.MapRegion(0x0000, 0xC000, deviceId, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0);
        });
        builder.WithExtended80ColumnDevice();

        var cpu = new Mock<ICpu>();
        cpu.Setup(c => c.Halted).Returns(false);
        var machine = builder.WithCpuFactory(_ => cpu.Object).Build();
        var device = machine.GetComponent<IExtended80ColumnDevice>() as Extended80ColumnDevice
            ?? throw new AssertionException("Extended 80-column device was not registered.");
        device.WriteAuxRam(0x0050, 0xA5);
        device.WriteAuxRam(0x0400, 0x42);
        device.WriteAuxRam(0x1000, 0x12);
        device.WriteAuxRam(0x2000, 0x22);
        device.WriteAuxRam(0x3000, 0x32);

        var dispatcher = new IOPageDispatcher();
        device.RegisterHandlers(dispatcher);
        var busMain = machine.Bus;
        byte[]? mainBacking = busMain.GetPageEntryByIndex(1).FastBacking;

        Assert.Multiple(() =>
        {
            Assert.That(busMain.GetPageEntryByIndex(0).Target, Is.InstanceOf<Extended80ColumnPage0Target>());
            AssertSlow(busMain.GetPageEntryByIndex(0));
            AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
            AssertIndexedByte(busMain.GetPageEntryByIndex(1), 0, 0x11);
            AssertIndexedByte(busMain.GetPageEntryByIndex(2), 0, 0x21);
            AssertIndexedByte(busMain.GetPageEntryByIndex(3), 0, 0x31);
        });

        WriteSwitch(dispatcher, 0x01);
        device.SetPage2(true);
        Assert.Multiple(() =>
        {
            AssertSlow(busMain.GetPageEntryByIndex(0));
            Assert.That(busMain.GetPageEntryByIndex(0).Target, Is.InstanceOf<Extended80ColumnPage0Target>());
            Assert.That(busMain.Read8(Access(0x0400, AccessIntent.DataRead)), Is.EqualTo(0x42), "80STORE + PAGE2 routes text page 1 inside the slow page-0 target");
            AssertFast(busMain.GetPageEntryByIndex(2), mainBacking, 0, RamAllOps);
            AssertIndexedByte(busMain.GetPageEntryByIndex(2), 0, 0x21);
        });

        device.SetHiRes(true);
        PageEntry hires = busMain.GetPageEntryByIndex(2);
        Assert.Multiple(() =>
        {
            Assert.That(hires.Target, Is.InstanceOf<RamTarget>());
            Assert.That(hires.FastBacking, Is.Not.SameAs(mainBacking));
            Assert.That(hires.FastOps, Is.EqualTo(RamAllOps));
            Assert.That(hires.FastBase, Is.EqualTo(0x2000));
            AssertIndexedByte(hires, 0, 0x22);
            Assert.That(busMain.GetPageEntryByIndex(3).PhysicalBase, Is.EqualTo(0x1000u));
            Assert.That(busMain.GetPageEntryByIndex(3).FastBacking, Is.SameAs(hires.FastBacking));
            AssertIndexedByte(busMain.GetPageEntryByIndex(3), 0, 0x32);
            AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
        });

        WriteSwitch(dispatcher, 0x00);
        AssertFast(busMain.GetPageEntryByIndex(2), mainBacking, 0, RamAllOps);
        AssertIndexedByte(busMain.GetPageEntryByIndex(2), 0, 0x21);

        WriteSwitch(dispatcher, 0x01);
        AssertIndexedByte(busMain.GetPageEntryByIndex(2), 0, 0x22);

        WriteSwitch(dispatcher, 0x03);
        Assert.Multiple(() =>
        {
            AssertSlow(busMain.GetPageEntryByIndex(1));
            Assert.That(busMain.GetPageEntryByIndex(1).Target, Is.Not.InstanceOf<RamTarget>());
            Assert.That(busMain.GetPageEntryByIndex(1).Target, Is.Not.InstanceOf<RomTarget>());
            AssertIndexedByte(busMain.GetPageEntryByIndex(2), 0, 0x22);
            Assert.That(busMain.GetPageEntryByIndex(0xB).FastBacking, Is.Null);
        });

        WriteSwitch(dispatcher, 0x02);
        device.SetHiRes(false);
        device.SetPage2(false);
        WriteSwitch(dispatcher, 0x00);
        AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
        AssertIndexedByte(busMain.GetPageEntryByIndex(1), 0, 0x11);
        AssertFast(busMain.GetPageEntryByIndex(2), mainBacking, 0, RamAllOps);

        WriteSwitch(dispatcher, 0x05);
        AssertSlow(busMain.GetPageEntryByIndex(1));
        WriteSwitch(dispatcher, 0x04);
        AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);

        WriteSwitch(dispatcher, 0x09);
        AssertSlow(busMain.GetPageEntryByIndex(0));
        AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
        Assert.That(busMain.Read8(Access(0x0050, AccessIntent.DataRead)), Is.EqualTo(0xA5), "ALTZP routes zero page inside the slow page-0 target");

        WriteSwitch(dispatcher, 0x0D);
        WriteSwitch(dispatcher, 0x07);
        AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
        AssertSlow(busMain.GetPageEntry(0xC000));

        WriteSwitch(dispatcher, 0x03);
        device.Reset();
        Assert.Multiple(() =>
        {
            AssertSlow(busMain.GetPageEntryByIndex(0));
            AssertFast(busMain.GetPageEntryByIndex(1), mainBacking, 0, RamAllOps);
            AssertIndexedByte(busMain.GetPageEntryByIndex(1), 0, 0x11);
            AssertFast(busMain.GetPageEntryByIndex(2), mainBacking, 0, RamAllOps);
        });
    }

    private static void ReadSwitch(IOPageDispatcher dispatcher, byte offset)
    {
        var access = Access((Addr)(0xC000 + offset), AccessIntent.DataRead);
        dispatcher.Read(offset, in access);
    }

    private static void WriteSwitch(IOPageDispatcher dispatcher, byte offset)
    {
        var access = Access((Addr)(0xC000 + offset), AccessIntent.DataWrite);
        dispatcher.Write(offset, 0, in access);
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

    private static void AssertFast(PageEntry page, PhysicalMemory pool, int fastBase, FastPathOps ops)
    {
        Assert.That(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(pool.Memory, out ArraySegment<byte> segment), Is.True);
        AssertFast(page, segment.Array, fastBase, ops);
    }

    private static void AssertFast(PageEntry page, byte[]? backing, int fastBase, FastPathOps ops)
    {
        Assert.Multiple(() =>
        {
            Assert.That(page.FastBacking, Is.SameAs(backing));
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
}