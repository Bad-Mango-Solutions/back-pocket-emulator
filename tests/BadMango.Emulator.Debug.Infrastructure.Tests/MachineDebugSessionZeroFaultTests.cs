// <copyright file="MachineDebugSessionZeroFaultTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// A successful debug read is a zero <see cref="BusFault"/>.
/// <see cref="MachineDebugSession.ReadMemory"/> keeps the existing memory block.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionZeroFaultTests
{
    /// <summary>
    /// A mapped byte omits the fault mask, and the debug <c>TryRead8</c> the session uses
    /// returns a zero fault even when the page has a device and a region.
    /// </summary>
    [Test]
    public void ReadMemory_MappedByte_OmitsMask_AndTryRead8SuccessIsZeroFault()
    {
        byte[] raw = new byte[0x8000];
        raw[0x0100] = 0x5A;
        MainBus bus = CreatePartialBus(raw);
        MachineDebugSession session = CreateSession(bus);

        MemoryBlock block = session.ReadMemory(0x0100, 1);
        BusResult<byte> read = bus.TryRead8(DebugRead(0x0100));

        Assert.Multiple(() =>
        {
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0x5A }));
            Assert.That(block.Faults, Is.Null);
            Assert.That(read.Ok, Is.True);
            Assert.That(read.Value, Is.EqualTo(0x5A));
            Assert.That(read.Fault, Is.EqualTo(default(BusFault)));
            Assert.That(read.Fault.Kind, Is.EqualTo(FaultKind.None));
            Assert.That(read.Fault.Address, Is.EqualTo(0u));
            Assert.That(read.Fault.DeviceId, Is.EqualTo(0));
            Assert.That(read.Fault.RegionTag, Is.EqualTo(default(RegionTag)));
        });
    }

    /// <summary>
    /// An unmapped byte still comes back as a memory block with a mask. The read does not throw.
    /// </summary>
    [Test]
    public void ReadMemory_UnmappedByte_ReturnsAMask()
    {
        MainBus bus = CreatePartialBus(new byte[0x8000]);
        MachineDebugSession session = CreateSession(bus);

        MemoryBlock block = session.ReadMemory(0x8000, 1);

        Assert.Multiple(() =>
        {
            Assert.That(block.Length, Is.EqualTo(1));
            Assert.That(block.Data, Is.EqualTo(new byte[] { 0x00 }));
            Assert.That(block.Faults, Is.Not.Null);
            Assert.That(block.Faults!, Has.Count.EqualTo(1));
            Assert.That(block.Faults![0], Is.Not.EqualTo(0));
        });
    }

    /// <summary>
    /// <see cref="MemCommand"/> still prints <c>??</c> for an unmapped byte.
    /// </summary>
    [Test]
    public void MemCommand_UnmappedByte_PrintsQuestionMarks()
    {
        MainBus bus = CreatePartialBus(new byte[0x8000]);
        var output = new StringWriter();
        var context = new DebugContext(new CommandDispatcher(), output, new StringWriter(), cpu: null, bus, disassembler: null);

        CommandResult result = new MemCommand().Execute(context, ["$8000", "1"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("??"));
            Assert.That(output.ToString(), Does.Contain("Unmapped"));
        });
    }

    private static MainBus CreatePartialBus(byte[] raw)
    {
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

    private static MachineDebugSession CreateSession(MainBus bus)
    {
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter(), cpu: null, bus, disassembler: null);
        return new MachineDebugSession(context);
    }

    private static BusAccess DebugRead(uint address) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DebugRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.NoSideEffects);
}