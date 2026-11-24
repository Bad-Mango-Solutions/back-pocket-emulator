// <copyright file="TrapRegistryWiringTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Emulation.Cpu;
using BadMango.Emulator.Systems;

using Moq;

/// <summary>
/// Covers read/write trap dispatch, slot-dependent keys, and machine wiring.
/// </summary>
[TestFixture]
public class TrapRegistryWiringTests
{
    private const Addr WatchedAddress = 0x1234;

    /// <summary>
    /// A read trap runs on a data read, a write trap runs on a data write, and a call trap
    /// at the same address runs only on fetch.
    /// </summary>
    [Test]
    public void ReadWriteAndCallTraps_FireOnlyForTheirOperation()
    {
        using var machine = CreateRamMachine();
        var cpu = (Cpu65C02)machine.Cpu;
        var registry = machine.GetComponent<ITrapRegistry>()!;
        int reads = 0;
        int writes = 0;
        int calls = 0;

        registry.Register(WatchedAddress, TrapOperation.Read, "READ", TrapCategory.UserDefined, (_, _, _) =>
        {
            reads++;
            return TrapResult.NotHandled;
        });
        registry.Register(WatchedAddress, TrapOperation.Write, "WRITE", TrapCategory.UserDefined, (_, _, _) =>
        {
            writes++;
            return TrapResult.NotHandled;
        });
        registry.Register(WatchedAddress, TrapOperation.Call, "CALL", TrapCategory.UserDefined, (_, _, _) =>
        {
            calls++;
            return TrapResult.SuccessWithRedirect(new(2), WatchedAddress);
        });

        machine.Bus.Read8(Access(WatchedAddress, AccessIntent.DataRead));
        _ = machine.Bus.TryRead8(Access(WatchedAddress, AccessIntent.DataRead));
        machine.Bus.Read8(Access(WatchedAddress, AccessIntent.InstructionFetch));
        _ = machine.Bus.TryRead8(Access(WatchedAddress, AccessIntent.DebugRead));

        machine.Bus.Write8(Access(WatchedAddress, AccessIntent.DataWrite), 0x5A);
        _ = machine.Bus.TryWrite8(Access(WatchedAddress, AccessIntent.DataWrite), 0x5B);
        machine.Bus.Write8(Access(WatchedAddress, AccessIntent.DebugWrite), 0x5C);

        cpu.Write8(WatchedAddress, 0xEA);
        cpu.SetPC(WatchedAddress);
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(2), "Data reads should run the read trap.");
            Assert.That(writes, Is.EqualTo(2), "Data writes should run the write trap.");
            Assert.That(calls, Is.EqualTo(1), "Only instruction fetch should run the call trap.");
            Assert.That(cpu.Read8(WatchedAddress), Is.EqualTo(0xEA), "The opcode byte remains after the call trap.");
        });
    }

    /// <summary>
    /// A read-only trap at the program counter is not a call trap and does not run on fetch.
    /// </summary>
    [Test]
    public void ReadOnlyTrap_DoesNotCountAsCallTrapOnFetch()
    {
        using var machine = CreateRamMachine();
        var cpu = (Cpu65C02)machine.Cpu;
        var registry = (TrapRegistry)machine.GetComponent<ITrapRegistry>()!;
        int reads = 0;

        registry.Register(0x0800, TrapOperation.Read, "READ", TrapCategory.UserDefined, (_, _, _) =>
        {
            reads++;
            return TrapResult.NotHandled;
        });

        cpu.Write8(0x0800, 0xEA);
        cpu.SetPC(0x0800);
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(registry.MayHaveCallTrap(0x0800), Is.False);
            Assert.That(registry.HasTrap(0x0800, TrapOperation.Read), Is.True);
            Assert.That(registry.HasTrap(0x0800, TrapOperation.Call), Is.False);
            Assert.That(reads, Is.Zero, "Fetching the NOP must not run the read trap.");
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0801u));
        });
    }

    /// <summary>
    /// Two slots can register at $C800, and the active expansion slot selects the handler.
    /// </summary>
    [Test]
    public void TwoSlots_AtC800_ActiveExpansionSlotSelectsHandler()
    {
        var slotManager = new Mock<ISlotManager>();
        var card = new Mock<ISlotCard>();
        int? activeSlot = 6;
        slotManager.Setup(manager => manager.GetCard(It.IsAny<int>())).Returns(card.Object);
        slotManager.SetupGet(manager => manager.ActiveExpansionSlot).Returns(() => activeSlot);

        var registry = new TrapRegistry(slotManager.Object);
        int slotOneHits = 0;
        int slotSixHits = 0;
        TrapHandler slotOne = (_, _, _) =>
        {
            slotOneHits++;
            return TrapResult.Success(new(4));
        };
        TrapHandler slotSix = (_, _, _) =>
        {
            slotSixHits++;
            return TrapResult.Success(new(4));
        };

        registry.RegisterSlotDependent(0xC800, TrapOperation.Call, 1, "SLOT1", TrapCategory.SlotFirmware, slotOne);
        registry.RegisterSlotDependent(0xC800, TrapOperation.Call, 6, "SLOT6", TrapCategory.SlotFirmware, slotSix);

        var cpu = new Mock<ICpu>();
        var bus = new Mock<IMemoryBus>();
        var context = new Mock<IEventContext>();

        var slotSixResult = registry.TryExecute(0xC800, TrapOperation.Call, cpu.Object, bus.Object, context.Object);
        activeSlot = 1;
        var slotOneResult = registry.TryExecute(0xC800, TrapOperation.Call, cpu.Object, bus.Object, context.Object);
        activeSlot = null;
        var idleResult = registry.TryExecute(0xC800, TrapOperation.Call, cpu.Object, bus.Object, context.Object);

        Assert.Multiple(() =>
        {
            Assert.That(slotSixResult.Handled, Is.True);
            Assert.That(slotOneResult.Handled, Is.True);
            Assert.That(idleResult.Handled, Is.False);
            Assert.That(slotSixHits, Is.EqualTo(1));
            Assert.That(slotOneHits, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterSlotDependent(0xC800, TrapOperation.Call, 6, "AGAIN", TrapCategory.SlotFirmware, slotSix));
        });
    }

    /// <summary>
    /// AsPocket2e gives the trap registry the I/O page, and internal-ROM overlays suppress slot traps.
    /// </summary>
    [Test]
    public void AsPocket2e_InternalRomOverlay_SuppressesSlotTraps()
    {
        var slotThree = CreateCard();
        var slotSix = CreateCard();
        using var machine = new MachineBuilder()
            .AsPocket2e()
            .WithStubRom()
            .WithCard(3, slotThree.Object)
            .WithCard(6, slotSix.Object)
            .Build();

        var registry = (TrapRegistry)machine.GetComponent<ITrapRegistry>()!;
        var ioPage = (CompositeIOTarget)registry.InternalRomHandler!;
        var slotManager = machine.GetComponent<ISlotManager>()!;
        int hits = 0;
        TrapHandler handler = (_, _, _) =>
        {
            hits++;
            return TrapResult.Success(new(4));
        };

        registry.RegisterSlotDependent(0xC600, 6, "SLOT6", TrapCategory.SlotFirmware, handler);
        registry.RegisterSlotDependent(0xC300, 3, "SLOT3", TrapCategory.SlotFirmware, handler);
        registry.RegisterSlotDependent(0xC800, 6, "EXP6", TrapCategory.SlotFirmware, handler);
        slotManager.SelectExpansionSlot(6);

        TrapResult slotRom = registry.TryExecute(0xC600, machine.Cpu, machine.Bus, machine);
        ioPage.SetIntCxRom(true);
        TrapResult cxSuppressed = registry.TryExecute(0xC600, machine.Cpu, machine.Bus, machine);
        ioPage.SetIntCxRom(false);

        // INTC3ROM defaults to on, which is the IIe power-on state.
        ioPage.SetIntC3Rom(false);
        TrapResult slotThreeRom = registry.TryExecute(0xC300, machine.Cpu, machine.Bus, machine);
        ioPage.SetIntC3Rom(true);
        TrapResult c3Suppressed = registry.TryExecute(0xC300, machine.Cpu, machine.Bus, machine);
        TrapResult otherSlotStillRuns = registry.TryExecute(0xC600, machine.Cpu, machine.Bus, machine);

        TrapResult expansion = registry.TryExecute(0xC800, machine.Cpu, machine.Bus, machine);
        ioPage.Write8(0x300, 0, Access(0xC300, AccessIntent.DataWrite));
        bool intC8Armed = ioPage.IsIntC8RomEnabled;
        slotManager.SelectExpansionSlot(6);
        TrapResult c8Suppressed = registry.TryExecute(0xC800, machine.Cpu, machine.Bus, machine);
        _ = ioPage.Read8(0xFFF, Access(0xCFFF, AccessIntent.DataRead));
        slotManager.SelectExpansionSlot(6);
        TrapResult expansionAgain = registry.TryExecute(0xC800, machine.Cpu, machine.Bus, machine);

        Assert.Multiple(() =>
        {
            Assert.That(registry.InternalRomHandler, Is.SameAs(machine.GetComponent<IInternalRomHandler>()));
            Assert.That(registry.InternalRomHandler, Is.InstanceOf<CompositeIOTarget>());
            Assert.That(slotRom.Handled, Is.True);
            Assert.That(cxSuppressed.Handled, Is.False, "INTCXROM should suppress $C600.");
            Assert.That(slotThreeRom.Handled, Is.True);
            Assert.That(c3Suppressed.Handled, Is.False, "INTC3ROM should suppress $C300.");
            Assert.That(otherSlotStillRuns.Handled, Is.True, "INTC3ROM should leave $C600 runnable.");
            Assert.That(expansion.Handled, Is.True);
            Assert.That(intC8Armed, Is.True);
            Assert.That(c8Suppressed.Handled, Is.False, "INTC8ROM should suppress $C800.");
            Assert.That(expansionAgain.Handled, Is.True, "$CFFF should clear the INTC8ROM suppression.");
            Assert.That(hits, Is.EqualTo(5));
        });
    }

    /// <summary>
    /// A MachineBuilder machine shares one registry with the CPU, and Register does not throw.
    /// </summary>
    [Test]
    public void MachineBuilder_CpuUsesMachineRegistry_AndTrapFiresInStep()
    {
        using var machine = CreateRamMachine();
        var cpu = (Cpu65C02)machine.Cpu;
        var registry = machine.GetComponent<ITrapRegistry>()!;
        int hits = 0;

        Assert.That(cpu.TrapRegistry, Is.SameAs(registry));
        Assert.That(cpu.TrapRegistry, Is.Not.SameAs(NullTrapRegistry.Instance));

        cpu.TrapRegistry.Register(
            0x0800,
            "LOOP",
            TrapCategory.UserDefined,
            (_, _, _) =>
            {
                hits++;
                return TrapResult.SuccessWithRedirect(new(2), 0x0800);
            });

        cpu.Write8(0x0800, 0xEA);
        cpu.SetPC(0x0800);
        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(cpu.GetPC(), Is.EqualTo(0x0800u));
        });
    }

    private static IMachine CreateRamMachine()
    {
        var memory = new PhysicalMemory(64 * 1024, "MainRAM");
        var target = new RamTarget(memory.Slice(0, 64 * 1024));
        return new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpu(CpuFamily.Cpu65C02)
            .WithCpuFactory(context => new Cpu65C02(context))
            .MapRegion(0x0000, 64 * 1024, target, RegionTag.Ram, PagePerms.All)
            .Build();
    }

    private static Mock<ISlotCard> CreateCard()
    {
        var card = new Mock<ISlotCard>();
        card.SetupProperty(slotCard => slotCard.SlotNumber);
        card.Setup(slotCard => slotCard.Name).Returns("Test Card");
        card.Setup(slotCard => slotCard.IOHandlers).Returns((SlotIOHandlers?)null);
        card.Setup(slotCard => slotCard.ROMRegion).Returns((IBusTarget?)null);
        card.Setup(slotCard => slotCard.ExpansionROMRegion).Returns((IBusTarget?)null);
        return card;
    }

    private static BusAccess Access(Addr address, AccessIntent intent)
    {
        return new(
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
}