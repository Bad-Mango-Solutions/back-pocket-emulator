// <copyright file="MachineDebugSessionFaultMaskTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;

using Moq;

/// <summary>
/// <see cref="MachineDebugSession.ReadMemory"/> stores the section 10 low byte and omits an all-zero mask.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionFaultMaskTests
{
    /// <summary>
    /// An unmapped byte on a real bus stores <c>0x01</c>, and <see cref="MemCommand"/> still prints <c>??</c>.
    /// </summary>
    [Test]
    public void ReadMemory_UnmappedByte_MaskIsUnmappedLowByte()
    {
        MainBus bus = CreatePartialBus();
        var output = new StringWriter();
        var context = new DebugContext(new CommandDispatcher(), output, new StringWriter(), cpu: null, bus, disassembler: null);
        MachineDebugSession session = new(context);

        MemoryBlock block = session.ReadMemory(0x8000, 2);
        CommandResult command = new MemCommand().Execute(context, ["$8000", "1"]);

        Assert.Multiple(() =>
        {
            Assert.That(block.Faults, Is.EqualTo(new byte[] { 0x01, 0x01 }));
            Assert.That(block.Faults![0], Is.EqualTo((byte)FaultKind.Unmapped));
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0x00, 0x00 }));
            Assert.That(command.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("??"));
        });
    }

    /// <summary>
    /// A debug read of a write-only page still returns the byte. Permission is not a third read shape.
    /// </summary>
    [Test]
    public void ReadMemory_WriteOnlyPage_OmitsMask()
    {
        byte[] raw = new byte[0x1000];
        raw[0x0010] = 0xAB;
        var bus = new MainBus();
        var target = new RamTarget(raw, "write-only");
        bus.MapPage(0, new(1, RegionTag.Ram, PagePerms.Write, target.Capabilities, target, 0));
        MachineDebugSession session = CreateSession(bus);

        MemoryBlock block = session.ReadMemory(0x0010, 1);

        Assert.Multiple(() =>
        {
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0xAB }));
            Assert.That(block.Faults, Is.Null);
        });
    }

    /// <summary>
    /// The mask is the low byte of <see cref="FaultKind"/>. A zero low byte keeps the data byte,
    /// and a mask of only zeros is omitted.
    /// </summary>
    [Test]
    public void ReadMemory_StoresSection10LowByte_AndOmitsAllZeroMask()
    {
        BusResult<byte>[] script =
        [
            new(0x11, default),
            Fault(FaultKind.Permission, 0x00),
            Fault(FaultKind.Nx, 0x00),
            Fault(FaultKind.Misaligned, 0x00),
            Fault(FaultKind.DeviceFault, 0x00),
            Fault(FaultKind.Nx | FaultKind.Write, 0x00),
            Fault(FaultKind.Unmapped | FaultKind.Permission, 0x00),
            Fault(FaultKind.Fetch, 0x22),
        ];
        MachineDebugSession session = CreateScriptedSession(script);

        MemoryBlock block = session.ReadMemory(0x1000, script.Length);

        Assert.Multiple(() =>
        {
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x22 }));
            Assert.That(block.Faults, Is.EqualTo(new byte[] { 0x00, 0x02, 0x04, 0x08, 0x10, 0x04, 0x03, 0x00 }));
        });
    }

    /// <summary>
    /// A fault whose low byte is zero is not a mask. The data byte stays, and the mask is omitted.
    /// </summary>
    [Test]
    public void ReadMemory_HighByteOnly_OmitsMaskAndKeepsData()
    {
        MachineDebugSession session = CreateScriptedSession([Fault(FaultKind.Fetch, 0x22)]);

        MemoryBlock block = session.ReadMemory(0x2000, 1);

        Assert.Multiple(() =>
        {
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0x22 }));
            Assert.That(block.Faults, Is.Null);
        });
    }

    private static BusResult<byte> Fault(FaultKind kind, byte value) =>
        new(
            value,
            new BusFault(
                kind,
                0,
                8,
                AccessIntent.DebugRead,
                BusAccessMode.Decomposed,
                0,
                -1,
                RegionTag.Unmapped,
                0));

    private static MachineDebugSession CreateScriptedSession(IReadOnlyList<BusResult<byte>> results)
    {
        var queue = new Queue<BusResult<byte>>(results);
        var bus = new Mock<IMemoryBus>(MockBehavior.Strict);
        bus.Setup(mock => mock.TryRead8(It.Ref<BusAccess>.IsAny)).Returns(() => queue.Dequeue());
        return CreateSession(bus.Object);
    }

    private static MainBus CreatePartialBus()
    {
        var raw = new byte[0x8000];
        var bus = new MainBus(16);
        var target = new RamTarget(raw, "partial-ram");
        bus.MapPageRange(
            startPage: 0,
            pageCount: 8,
            deviceId: 7,
            regionTag: RegionTag.Rom,
            perms: PagePerms.ReadWrite,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);
        return bus;
    }

    private static MachineDebugSession CreateSession(IMemoryBus bus)
    {
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter(), cpu: null, bus, disassembler: null);
        return new MachineDebugSession(context);
    }
}