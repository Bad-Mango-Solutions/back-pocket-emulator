// <copyright file="CpuFaultRegisterTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// The CPU slow path latches through <see cref="MainBus"/> <c>Try*</c>.
/// </summary>
/// <remarks>
/// These tests call <see cref="CpuBase.Read8"/>, <see cref="CpuBase.Peek8"/>, and
/// <see cref="CpuBase.Write16"/>. They do not execute a 6502 opcode to read the registers.
/// </remarks>
[TestFixture]
public class CpuFaultRegisterTests
{
    private static readonly FaultKind HighByte =
        FaultKind.Write | FaultKind.Fetch | FaultKind.BankCrossing | FaultKind.Privilege;

    /// <summary>
    /// <see cref="CpuBase.Peek8"/> of an unmapped address does not change status.
    /// </summary>
    [Test]
    public void Peek8_Unmapped_DoesNotChangeStatus()
    {
        var bus = new MainBus(16);
        var cpu = CreateCpu(bus);

        byte peeked = cpu.Peek8(0x2000);

        Assert.Multiple(() =>
        {
            Assert.That(peeked, Is.EqualTo(0xFF));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(bus.FaultAddress, Is.EqualTo(0u));
        });
    }

    /// <summary>
    /// <see cref="CpuBase.Peek8"/> and <see cref="CpuBase.Poke8"/> do not replace a latched fault.
    /// </summary>
    [Test]
    public void Peek8AndPoke8_DoNotReplaceLatchedStatus()
    {
        var bus = new MainBus(16);
        bus.TryRead8(Access(0x1111, AccessIntent.DataRead));
        var cpu = CreateCpu(bus);

        _ = cpu.Peek8(0x2222);
        cpu.Poke8(0x3333, 0x42);

        Assert.Multiple(() =>
        {
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x1111u));
            Assert.That(bus.FaultStatus & HighByte, Is.EqualTo(FaultKind.None));
        });
    }

    /// <summary>
    /// An unmapped <see cref="CpuBase.Read8"/> latches on the slow path, with a zero high byte.
    /// </summary>
    [Test]
    public void Read8_Unmapped_LatchesOnTheSlowPath()
    {
        var bus = new MainBus(16);
        var cpu = CreateCpu(bus);

        byte value = cpu.Read8(0x1800);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(0xFF));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x1800u));
            Assert.That(bus.FaultStatus & HighByte, Is.EqualTo(FaultKind.None));
            Assert.That(bus.LatchedFault.Intent, Is.EqualTo(AccessIntent.DataRead));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A fast RAM read does not enter <c>Try*</c> and does not clear a latched fault.
    /// </summary>
    [Test]
    public void FastRamRead_DoesNotChangeLatchedStatus()
    {
        var raw = new byte[0x1000];
        raw[0x0010] = 0x5A;
        var bus = new MainBus(16);
        bus.MapPage(0, new(0, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));
        bus.TryRead8(Access(0x2000, AccessIntent.DataRead));
        int reads = bus.TryRead8CallCount;
        var cpu = CreateCpu(bus);

        byte value = cpu.Read8(0x0010);
        cpu.Write8(0x0011, 0x17);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(0x5A));
            Assert.That(raw[0x0011], Is.EqualTo(0x17));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x2000u));
        });
    }

    /// <summary>
    /// <see cref="CpuBase.Write16"/> whose second page is unmapped keeps the first byte
    /// and latches that second address.
    /// </summary>
    [Test]
    public void Write16_SecondPageUnmapped_LatchesSecondAddress()
    {
        var raw = new byte[0x1000];
        var bus = new MainBus(16);
        bus.MapPage(0, new(0, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));
        var cpu = CreateCpu(bus);

        int writes = bus.TryWrite8CallCount;
        cpu.Write16(0x0FFF, 0xBEEF);

        Assert.Multiple(() =>
        {
            Assert.That(raw[0x0FFF], Is.EqualTo(0xEF));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(writes + 1));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.None));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x1000u));
            Assert.That(bus.FaultStatus & HighByte, Is.EqualTo(FaultKind.None));
        });
    }

    /// <summary>
    /// <see cref="CpuBase.Read16"/> whose second byte is unmapped latches that address
    /// and does not check a further byte.
    /// </summary>
    [Test]
    public void Read16_SecondByteUnmapped_LatchesThatAddress()
    {
        var raw = new byte[0x1000];
        raw[0x0FFF] = 0x11;
        var bus = new MainBus(16);
        bus.MapPage(0, new(0, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsPeek, new RamTarget(raw, "ram"), 0));
        var cpu = CreateCpu(bus);

        Word value = cpu.Read16(0x0FFF);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo((Word)0xFFFF));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(1));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(bus.FaultAddress, Is.EqualTo(0x1000u));
            Assert.That(cpu.HaltReason, Is.EqualTo(HaltState.Stp));
        });
    }

    /// <summary>
    /// A CPU slow-path fault records one <see cref="BusFault"/> whose <see cref="BusFault.Pc"/>
    /// is the CPU's program counter. A debug peek of another unmapped address does not enter the ring.
    /// </summary>
    [Test]
    public void Read8_Unmapped_RecordsCpuProgramCounter_PeekDoesNotRecord()
    {
        var ring = new BusFaultRing();
        var bus = new MainBus(16, faultRecorder: ring) { FaultRecordingEnabled = true };
        var cpu = CreateCpu(bus);
        const Addr pc = 0x4321;
        cpu.SetPC(pc);

        byte value = cpu.Read8(0x1800);
        cpu.Peek8(0x1900);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(0xFF));
            Assert.That(cpu.GetPC(), Is.EqualTo(pc));
            Assert.That(ring.Count, Is.EqualTo(1));
            Assert.That(ring.Last, Is.Not.Null);
            Assert.That(ring.Last!.Value.Pc, Is.EqualTo(pc));
            Assert.That(ring.Last.Value.Kind, Is.EqualTo(FaultKind.Unmapped));
            Assert.That(ring.Last.Value.Address, Is.EqualTo(0x1800u));
            Assert.That(bus.LatchedFault.Pc, Is.EqualTo(0u));
        });
    }

    /// <summary>
    /// An indexed RAM read does not call <c>Try*</c>, does not latch, and does not enter the ring.
    /// </summary>
    [Test]
    public void FastRamRead_DoesNotRecordAFault()
    {
        var raw = new byte[0x1000];
        raw[0x0010] = 0x5A;
        var ring = new BusFaultRing();
        var bus = new MainBus(16, faultRecorder: ring) { FaultRecordingEnabled = true };
        bus.MapPage(0, new(0, RegionTag.Ram, PagePerms.All, TargetCaps.SupportsPeek | TargetCaps.SupportsPoke, new RamTarget(raw, "ram"), 0));
        var cpu = CreateCpu(bus);
        cpu.SetPC(0x1111);
        int reads = bus.TryRead8CallCount;

        byte value = cpu.Read8(0x0010);
        cpu.Write8(0x0011, 0x17);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(0x5A));
            Assert.That(raw[0x0011], Is.EqualTo(0x17));
            Assert.That(bus.TryRead8CallCount, Is.EqualTo(reads));
            Assert.That(bus.TryWrite8CallCount, Is.EqualTo(0));
            Assert.That(bus.FaultStatus, Is.EqualTo(FaultKind.None));
            Assert.That(ring.Count, Is.Zero);
            Assert.That(ring.TotalFaults, Is.Zero);
        });
    }

    private static Cpu65C02 CreateCpu(MainBus bus) =>
        new(new EventContext(new Scheduler(), new SignalBus(), bus));

    private static BusAccess Access(Addr address, AccessIntent intent) =>
        new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: intent,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
}