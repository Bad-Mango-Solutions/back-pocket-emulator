// <copyright file="MachineDebugSessionDebugReadTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices.Interfaces;

using Moq;

/// <summary>
/// Debug reads stay on <c>TryRead8</c> and do not latch or record.
/// A non-debug <c>read</c> command still returns a fault and records it with program counter 0.
/// </summary>
[TestFixture]
public sealed class MachineDebugSessionDebugReadTests
{
    /// <summary>
    /// <see cref="MachineDebugSession.ReadMemory"/> of an unmapped byte keeps the mask and
    /// leaves status and the ring alone.
    /// </summary>
    [Test]
    public void ReadMemory_Unmapped_DoesNotLatchOrRecord()
    {
        (MainBus bus, BusFaultRing ring) = CreateRecordingBus();
        MachineDebugSession session = CreateSession(bus);

        MemoryBlock block = session.ReadMemory(0x8000, 1);

        Assert.Multiple(() =>
        {
            Assert.That(block.Faults, Is.EqualTo(new byte[] { 0x01 }));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
            Assert.That(ring.Count, Is.Zero);
            Assert.That(ring.TotalFaults, Is.Zero);
        });
    }

    /// <summary>
    /// <see cref="MemCommand"/> still prints <c>??</c> and does not latch or record.
    /// </summary>
    [Test]
    public void MemCommand_Unmapped_PrintsQuestionMarks_WithoutLatching()
    {
        (MainBus bus, BusFaultRing ring) = CreateRecordingBus();
        var output = new StringWriter();
        var context = CreateContext(bus, output);

        CommandResult result = new MemCommand().Execute(context, ["$8000", "1"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("??"));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(ring.Count, Is.Zero);
        });
    }

    /// <summary>
    /// <see cref="PeekCommand"/> stays on the debug read and does not latch or record.
    /// </summary>
    [Test]
    public void PeekCommand_Unmapped_DoesNotLatchOrRecord()
    {
        (MainBus bus, BusFaultRing ring) = CreateRecordingBus();
        var output = new StringWriter();
        var context = CreateContext(bus, output);

        CommandResult result = new PeekCommand().Execute(context, ["$8000"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("??"));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(ring.Count, Is.Zero);
        });
    }

    /// <summary>
    /// Screen text falls back to debug <c>TryRead8</c> and does not latch or record.
    /// </summary>
    [Test]
    public void GetScreenText_DebugReadFallback_DoesNotLatchOrRecord()
    {
        (MainBus bus, BusFaultRing ring) = CreateRecordingBus();
        var video = new Mock<IVideoDevice>(MockBehavior.Strict);
        video.SetupGet(item => item.Is80Column).Returns(false);
        video.SetupGet(item => item.IsTextMode).Returns(true);
        video.SetupGet(item => item.IsMixedMode).Returns(false);
        video.SetupGet(item => item.IsPage2).Returns(false);

        var machine = new Mock<IMachine>(MockBehavior.Strict);
        machine.Setup(item => item.GetComponent<IVideoDevice>()).Returns(video.Object);
        machine.Setup(item => item.GetComponent<IMainMemoryProvider>()).Returns((IMainMemoryProvider?)null);
        machine.Setup(item => item.GetComponent<IExtended80ColumnDevice>()).Returns((IExtended80ColumnDevice?)null);
        machine.SetupGet(item => item.Bus).Returns(bus);

        var context = new Mock<IDebugContext>(MockBehavior.Strict);
        context.SetupGet(item => item.Machine).Returns(machine.Object);
        var session = new MachineDebugSession(context.Object);

        var screen = session.GetScreenText();

        Assert.Multiple(() =>
        {
            Assert.That(screen.Rows, Is.EqualTo(24));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(ring.Count, Is.Zero);
        });
    }

    /// <summary>
    /// <see cref="ReadCommand"/> still calls non-debug <c>TryRead8</c>. The result fails,
    /// the registers latch, and the ring stores that fault with program counter 0.
    /// </summary>
    [Test]
    public void ReadCommand_Unmapped_RecordsTheReturnedFaultWithZeroPc()
    {
        (MainBus bus, BusFaultRing ring) = CreateRecordingBus();
        var output = new StringWriter();
        var context = CreateContext(bus, output);

        CommandResult result = new ReadCommand().Execute(context, ["$8000", "1"]);

        Assert.That(ring.Last, Is.Not.Null);
        BusFault recorded = ring.Last!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(output.ToString(), Does.Contain("??"));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x8000u));
            Assert.That(ring.Count, Is.EqualTo(1));
            Assert.That(recorded.Kind, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(recorded.Address, Is.EqualTo(0x8000u));
            Assert.That(recorded.Pc, Is.EqualTo(0u));
            Assert.That((ushort)recorded.Kind, Is.EqualTo((ushort)bus.FaultStatus));
        });
    }

    private static (MainBus Bus, BusFaultRing Ring) CreateRecordingBus()
    {
        var ring = new BusFaultRing();
        var raw = new byte[0x8000];
        var bus = new MainBus(16, faultRecorder: ring) { FaultRecordingEnabled = true };
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
        return (bus, ring);
    }

    private static DebugContext CreateContext(MainBus bus, StringWriter output) =>
        new(new CommandDispatcher(), output, new StringWriter(), cpu: null, bus, disassembler: null);

    private static MachineDebugSession CreateSession(MainBus bus) =>
        new(CreateContext(bus, new StringWriter()));
}