// <copyright file="MachineDebugSessionFaultRegisterTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// The debug session reads and clears the bus fault registers.
/// A failing <c>TryRead8</c> still returns <see cref="BusResult{T}.Failed"/>.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionFaultRegisterTests
{
    /// <summary>
    /// A non-debug unmapped read fails, and the session reports that same status and address.
    /// Clearing the session writes both registers back to zero.
    /// </summary>
    [Test]
    public void TryRead8_Unmapped_SessionReadsAndClearsTheRegisters()
    {
        var bus = new MainBus(16);
        BusResult<byte> result = bus.TryRead8(DataRead(0x8000));
        MachineDebugSession session = CreateSession(bus);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.Fault.Kind, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(session.FaultStatus, Is.EqualTo((ushort)FaultKind.Unmapped));
            Assert.That(session.FaultAddress, Is.EqualTo(0x8000u));
        });

        session.ClearFaultStatus();

        Assert.Multiple(() =>
        {
            Assert.That(session.FaultStatus, Is.EqualTo((ushort)FaultKind.None));
            Assert.That(session.FaultAddress, Is.EqualTo(0u));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
        });
    }

    /// <summary>
    /// A later successful read leaves a latched fault in place.
    /// </summary>
    [Test]
    public void SuccessfulRead_LeavesLatchedFaultStatus()
    {
        byte[] raw = new byte[0x1000];
        raw[0x0010] = 0x5A;
        var bus = new MainBus(16);
        var target = new RamTarget(raw, "ram");
        bus.MapPage(0, new(1, RegionTag.Ram, PagePerms.All, target.Capabilities, target, 0));
        _ = bus.TryRead8(DataRead(0x9000));
        BusResult<byte> success = bus.TryRead8(DataRead(0x0010));
        MachineDebugSession session = CreateSession(bus);

        Assert.Multiple(() =>
        {
            Assert.That(success.Ok, Is.True);
            Assert.That(success.Fault, Is.EqualTo(default(BusFault)));
            Assert.That(session.FaultStatus, Is.EqualTo((ushort)FaultKind.Unmapped));
            Assert.That(session.FaultAddress, Is.EqualTo(0x9000u));
        });
    }

    /// <summary>
    /// <c>fault</c> prints the latched registers when the ring has not recorded the access.
    /// </summary>
    [Test]
    public void FaultCommand_PrintsLatchedRegistersWhenTheRingIsEmpty()
    {
        var ring = new BusFaultRing();
        var bus = new MainBus(16, faultRecorder: ring);
        BusResult<byte> result = bus.TryRead8(DataRead(0x8000));
        var output = new StringWriter();
        var context = new DebugContext(new CommandDispatcher(), output, new StringWriter(), cpu: null, bus, disassembler: null);

        CommandResult command = new FaultCommand().Execute(context, []);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(ring.Count, Is.Zero);
            Assert.That(command.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("Bus Fault Status"));
            Assert.That(output.ToString(), Does.Contain("Unmapped"));
            Assert.That(output.ToString(), Does.Contain("$8000"));
            Assert.That(output.ToString(), Does.Contain("No fault recorded"));
        });
    }

    /// <summary>
    /// Reading the registers without a bus throws.
    /// </summary>
    [Test]
    public void FaultStatus_WithoutBus_Throws()
    {
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter());
        var session = new MachineDebugSession(context);

        Assert.Throws<InvalidOperationException>(() => _ = session.FaultStatus);
    }

    private static MachineDebugSession CreateSession(IMemoryBus bus)
    {
        var context = new DebugContext(new CommandDispatcher(), new StringWriter(), new StringWriter(), cpu: null, bus, disassembler: null);
        return new MachineDebugSession(context);
    }

    private static BusAccess DataRead(uint address) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataRead,
            SourceId: 3,
            Cycle: 9,
            Flags: AccessFlags.None);
}