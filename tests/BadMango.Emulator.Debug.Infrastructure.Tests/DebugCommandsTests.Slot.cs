// <copyright file="DebugCommandsTests.Slot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Reflection;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices;

using Moq;

/// <content>
/// Unit tests for <see cref="SlotCommand"/>.
/// </content>
public partial class DebugCommandsTests
{
    /// <summary>
    /// Verifies that <see cref="SlotCommand"/> exposes the expected name.
    /// </summary>
    [Test]
    public void SlotCommand_HasCorrectName()
    {
        var command = new SlotCommand();
        Assert.That(command.Name, Is.EqualTo("slot"));
    }

    /// <summary>
    /// Verifies that <c>slot list</c> shows all seven slots and marks empty slots.
    /// </summary>
    [Test]
    public void SlotCommand_List_ShowsAllSlots()
    {
        var command = new SlotCommand();
        var machine = new Mock<IMachine>();
        var slotManager = new Mock<ISlotManager>();
        var slot4 = new Mock<ISlotCard>();
        slot4.SetupGet(card => card.DeviceType).Returns("pocketwatch");
        slot4.SetupGet(card => card.Name).Returns("PocketWatch");
        var slot6 = new Mock<ISlotCard>();
        slot6.SetupGet(card => card.DeviceType).Returns("disk-ii-compatible");
        slot6.SetupGet(card => card.Name).Returns("Disk II Controller");

        slotManager.Setup(manager => manager.GetCard(4)).Returns(slot4.Object);
        slotManager.Setup(manager => manager.GetCard(6)).Returns(slot6.Object);
        machine.Setup(m => m.GetComponent<ISlotManager>()).Returns(slotManager.Object);
        debugContext.AttachMachine(machine.Object);

        var result = command.Execute(debugContext, ["list"]);
        var output = outputWriter.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(output, Does.Contain("Slot  Type"));
            Assert.That(output, Does.Contain("   1  (empty)"));
            Assert.That(output, Does.Contain("   4  pocketwatch"));
            Assert.That(output, Does.Contain("   6  disk-ii-compatible"));
            Assert.That(output, Does.Contain("   7  (empty)"));
        });
    }

    /// <summary>
    /// Verifies that <c>slot types</c> reuses the same slot-card catalog as <see cref="DeviceTypesCommand"/>.
    /// </summary>
    [Test]
    public void SlotCommand_Types_UsesDeviceTypesSlotCatalog()
    {
        var machine = new Mock<IMachine>();
        debugContext.AttachMachine(machine.Object);

        var result = new SlotCommand().Execute(debugContext, ["types"]);
        var output = outputWriter.ToString();
        var expected = DeviceTypesCommand.GetSortedSlotCardTypeIds();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(output, Does.Contain($"Slot cards ({expected.Count})"));
            Assert.That(expected, Is.Not.Empty);
            Assert.That(output, Does.Contain(expected[0]));
            Assert.That(output, Does.Contain("pocketwatch"));
            Assert.That(output, Does.Contain("disk-ii-compatible"));
        });
    }

    /// <summary>
    /// Verifies that <c>slot insert</c> parses key/value config into JSON and forwards it to the slot-card factory.
    /// </summary>
    [Test]
    public void SlotCommand_Insert_ParsesConfigAndForwardsToFactory()
    {
        const string testType = "test-config-slot-card";
        JsonElement? capturedConfig = null;
        using var configDoc = JsonDocument.Parse("{}");
        var insertedCard = new TestConfigSlotCard();
        var machine = new Mock<IMachine>();
        machine.SetupGet(m => m.State).Returns(MachineState.Off);
        machine.Setup(m => m.InsertSlotCard(5, It.IsAny<ISlotCard>()));
        debugContext.AttachMachine(machine.Object);

        using var _ = RegisterTemporarySlotCardFactory(
            testType,
            (_, cfg) =>
            {
                capturedConfig = cfg;
                return insertedCard;
            });

        var result = new SlotCommand().Execute(
            debugContext,
            ["insert", "5", testType, "enabled=true", "drives=2", "name=driveA", "latency=1.5", "rom=null"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            machine.Verify(m => m.InsertSlotCard(5, insertedCard), Times.Once);
            Assert.That(capturedConfig.HasValue, Is.True);
            Assert.That(capturedConfig!.Value.GetProperty("enabled").GetBoolean(), Is.True);
            Assert.That(capturedConfig!.Value.GetProperty("drives").GetInt64(), Is.EqualTo(2));
            Assert.That(capturedConfig!.Value.GetProperty("name").GetString(), Is.EqualTo("driveA"));
            Assert.That(capturedConfig!.Value.GetProperty("latency").GetDouble(), Is.EqualTo(1.5).Within(0.000001));
            Assert.That(capturedConfig!.Value.GetProperty("rom").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    /// <summary>
    /// Verifies that slot mutations are rejected while the machine is not off.
    /// </summary>
    /// <param name="state">The machine state to test.</param>
    [TestCase(MachineState.Stopped)]
    [TestCase(MachineState.Running)]
    [TestCase(MachineState.Paused)]
    public void SlotCommand_InsertRemove_WhenMachineNotOff_ReturnsError(MachineState state)
    {
        var machine = new Mock<IMachine>();
        machine.SetupGet(m => m.State).Returns(state);
        debugContext.AttachMachine(machine.Object);

        var command = new SlotCommand();
        var insert = command.Execute(debugContext, ["insert", "5", "pocketwatch"]);
        var remove = command.Execute(debugContext, ["remove", "5"]);

        Assert.Multiple(() =>
        {
            Assert.That(insert.Success, Is.False);
            Assert.That(remove.Success, Is.False);
            Assert.That(insert.Message, Is.EqualTo("Slot changes require the machine to be off. Use 'power --off' first."));
            Assert.That(remove.Message, Is.EqualTo("Slot changes require the machine to be off. Use 'power --off' first."));
            machine.Verify(m => m.InsertSlotCard(It.IsAny<int>(), It.IsAny<ISlotCard>()), Times.Never);
            machine.Verify(m => m.RemoveSlotCard(It.IsAny<int>()), Times.Never);
        });
    }

    /// <summary>
    /// Verifies that invalid slot tokens and unknown types produce clear errors.
    /// </summary>
    [Test]
    public void SlotCommand_Insert_WithInvalidInputs_ReturnsUsageOrError()
    {
        var machine = new Mock<IMachine>();
        machine.SetupGet(m => m.State).Returns(MachineState.Off);
        debugContext.AttachMachine(machine.Object);
        var command = new SlotCommand();

        var nonNumeric = command.Execute(debugContext, ["insert", "x", "pocketwatch"]);
        var outOfRange = command.Execute(debugContext, ["insert", "8", "pocketwatch"]);
        var unknownType = command.Execute(debugContext, ["insert", "5", "missing-card-type"]);

        Assert.Multiple(() =>
        {
            Assert.That(nonNumeric.Success, Is.False);
            Assert.That(nonNumeric.ExitCode, Is.EqualTo(2));
            Assert.That(nonNumeric.Message, Does.Contain("Slot must be an integer"));
            Assert.That(outOfRange.Success, Is.False);
            Assert.That(outOfRange.ExitCode, Is.EqualTo(2));
            Assert.That(outOfRange.Message, Does.Contain("Slot must be an integer"));
            Assert.That(unknownType.Success, Is.False);
            Assert.That(unknownType.Message, Does.Contain("Unknown slot-card type"));
        });
    }

    private static IDisposable RegisterTemporarySlotCardFactory(
        string typeId,
        Func<MachineBuilder, JsonElement?, ISlotCard> factory)
    {
        FieldInfo field = typeof(DeviceFactoryRegistry).GetField(
            "slotCardFactories",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var dictionary = (Dictionary<string, Func<MachineBuilder, JsonElement?, ISlotCard>>)field.GetValue(null)!;
        dictionary[typeId] = factory;
        return new TemporaryFactoryRegistration(dictionary, typeId);
    }

    private sealed class TemporaryFactoryRegistration : IDisposable
    {
        private readonly Dictionary<string, Func<MachineBuilder, JsonElement?, ISlotCard>> dictionary;
        private readonly string typeId;

        public TemporaryFactoryRegistration(
            Dictionary<string, Func<MachineBuilder, JsonElement?, ISlotCard>> dictionary,
            string typeId)
        {
            this.dictionary = dictionary;
            this.typeId = typeId;
        }

        public void Dispose()
        {
            dictionary.Remove(typeId);
        }
    }

    private sealed class TestConfigSlotCard : ISlotCard
    {
        public int SlotNumber { get; set; }

        public SlotIOHandlers? IOHandlers => null;

        public IBusTarget? ROMRegion => null;

        public IBusTarget? ExpansionROMRegion => null;

        public string Name => "Test Config Slot Card";

        public string DeviceType => "test-config-slot-card";

        public PeripheralKind Kind => PeripheralKind.SlotCard;

        public void Initialize(IEventContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
        }

        public void Reset()
        {
        }

        public void OnExpansionROMSelected()
        {
        }

        public void OnExpansionROMDeselected()
        {
        }
    }
}