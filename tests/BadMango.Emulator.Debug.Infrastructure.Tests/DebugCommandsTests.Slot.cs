// <copyright file="DebugCommandsTests.Slot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Reflection;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Emulation.Cpu;

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
    /// Verifies that <c>slot list</c> shows active slot-set status and modified marker.
    /// </summary>
    [Test]
    public void SlotCommand_List_ShowsActiveSetStatus()
    {
        var command = new SlotCommand();
        var machine = new Mock<IMachine>();
        var slotManager = new Mock<ISlotManager>();
        var runtimeState = new SlotSetRuntimeState(
            new Dictionary<string, SlotSetRuntimeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["game"] = new("game", null, []),
            },
            "game");
        runtimeState.MarkModified();
        machine.Setup(m => m.GetComponent<ISlotManager>()).Returns(slotManager.Object);
        machine.Setup(m => m.GetComponent<SlotSetRuntimeState>()).Returns(runtimeState);
        debugContext.AttachMachine(machine.Object);

        var result = command.Execute(debugContext, ["list"]);
        var output = outputWriter.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(output, Does.Contain("Active slot set: (modified)"));
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
    /// Verifies that <c>slot sets</c> lists named sets and marks the active one.
    /// </summary>
    [Test]
    public void SlotCommand_Sets_ListsNamedSetsAndActiveSet()
    {
        var machine = new Mock<IMachine>();
        machine.Setup(m => m.GetComponent<SlotSetRuntimeState>()).Returns(
            new SlotSetRuntimeState(
                new Dictionary<string, SlotSetRuntimeDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["game"] = new("game", "Mockingboard setup", []),
                    ["workstation"] = new("workstation", "Clock setup", []),
                },
                "game"));
        debugContext.AttachMachine(machine.Object);

        var result = new SlotCommand().Execute(debugContext, ["sets"]);
        var output = outputWriter.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(output, Does.Contain("Slot sets:"));
            Assert.That(output, Does.Contain("game (active)"));
            Assert.That(output, Does.Contain("workstation"));
        });
    }

    /// <summary>
    /// Verifies that <c>slot use</c> switches to the target set and preserves unchanged slot instances.
    /// </summary>
    [Test]
    public void SlotCommand_Use_OffStateSwitchesSetsAndKeepsUnchangedCardInstances()
    {
        using var baseFactory = RegisterTemporarySlotCardFactory("set-base-card", (_, _) => new TestConfigSlotCard { TypeId = "set-base-card" });
        using var gameFactory = RegisterTemporarySlotCardFactory("set-game-card", (_, _) => new TestConfigSlotCard { TypeId = "set-game-card" });
        using var workFactory = RegisterTemporarySlotCardFactory("set-work-card", (_, _) => new TestConfigSlotCard { TypeId = "set-work-card" });
        var slotManager = new SlotManager(new IOPageDispatcher());
        var runtimeState = new SlotSetRuntimeState(
            new Dictionary<string, SlotSetRuntimeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["game"] = new(
                    "game",
                    "game set",
                    [
                        new SlotCardProfile { Slot = 4, Type = "set-game-card" },
                        new SlotCardProfile { Slot = 6, Type = "set-base-card" },
                    ]),
                ["workstation"] = new(
                    "workstation",
                    "work set",
                    [
                        new SlotCardProfile { Slot = 2, Type = "set-work-card" },
                        new SlotCardProfile { Slot = 6, Type = "set-base-card" },
                    ]),
            },
            "game");
        IMachine machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(static ctx => new Cpu65C02(ctx))
            .AddComponent(slotManager)
            .AddComponent(runtimeState)
            .Build();
        machine.InsertSlotCard(4, new TestConfigSlotCard { TypeId = "set-game-card" });
        machine.InsertSlotCard(6, new TestConfigSlotCard { TypeId = "set-base-card" });
        var slot6Before = slotManager.GetCard(6);
        debugContext.AttachMachine(machine);

        var result = new SlotCommand().Execute(debugContext, ["use", "workstation"]);
        var slot6After = slotManager.GetCard(6);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(slotManager.GetCard(2)?.DeviceType, Is.EqualTo("set-work-card"));
            Assert.That(slotManager.GetCard(4), Is.Null);
            Assert.That(slot6After, Is.SameAs(slot6Before));
            Assert.That(runtimeState.ActiveSetName, Is.EqualTo("workstation"));
            Assert.That(runtimeState.IsModified, Is.False);
        });
    }

    /// <summary>
    /// Verifies that <c>slot use</c> rebuilds a card when the active and target sets share a type
    /// but differ in config or preset, and keeps the instance when type and config both match.
    /// </summary>
    [Test]
    public void SlotCommand_Use_ConfigOrPresetChange_ReplacesCardAndKeepsUnchangedInstance()
    {
        JsonElement sharedConfig = CloneJson("{\"mode\":\"shared\"}");
        JsonElement gameConfig = CloneJson("{\"mode\":\"game\"}");
        JsonElement workConfig = CloneJson("{\"mode\":\"work\"}");
        using var factory = RegisterTemporarySlotCardFactory(
            "set-config-card",
            (_, config) => new TestConfigSlotCard
            {
                TypeId = "set-config-card",
                AppliedConfig = config,
            });
        var slotManager = new SlotManager(new IOPageDispatcher());
        var runtimeState = new SlotSetRuntimeState(
            new Dictionary<string, SlotSetRuntimeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["game"] = new(
                    "game",
                    "game set",
                    [
                        new SlotCardProfile { Slot = 4, Type = "set-config-card", Config = gameConfig },
                        new SlotCardProfile { Slot = 5, Type = "set-config-card", Config = sharedConfig },
                        new SlotCardProfile { Slot = 6, Type = "set-config-card", Preset = "alpha" },
                    ]),
                ["workstation"] = new(
                    "workstation",
                    "work set",
                    [
                        new SlotCardProfile { Slot = 4, Type = "set-config-card", Config = workConfig },
                        new SlotCardProfile { Slot = 5, Type = "set-config-card", Config = sharedConfig },
                        new SlotCardProfile { Slot = 6, Type = "set-config-card", Preset = "beta" },
                    ]),
            },
            "game");
        IMachine machine = new MachineBuilder()
            .WithAddressSpace(16)
            .WithCpuFactory(static ctx => new Cpu65C02(ctx))
            .AddComponent(slotManager)
            .AddComponent(runtimeState)
            .Build();
        var slot4Before = new TestConfigSlotCard { TypeId = "set-config-card" };
        var slot5Before = new TestConfigSlotCard { TypeId = "set-config-card" };
        var slot6Before = new TestConfigSlotCard { TypeId = "set-config-card" };
        machine.InsertSlotCard(4, slot4Before);
        machine.InsertSlotCard(5, slot5Before);
        machine.InsertSlotCard(6, slot6Before);
        debugContext.AttachMachine(machine);
        Assume.That(machine.State, Is.EqualTo(MachineState.Off));
        Assume.That(runtimeState.IsModified, Is.False);

        var result = new SlotCommand().Execute(debugContext, ["use", "workstation"]);
        var slot4After = slotManager.GetCard(4) as TestConfigSlotCard;
        var slot5After = slotManager.GetCard(5);
        var slot6After = slotManager.GetCard(6);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(slot4After, Is.Not.Null);
            Assert.That(slot4After, Is.Not.SameAs(slot4Before));
            Assert.That(slot4After!.AppliedConfig?.GetProperty("mode").GetString(), Is.EqualTo("work"));
            Assert.That(slot5After, Is.SameAs(slot5Before));
            Assert.That(slot6After, Is.Not.Null);
            Assert.That(slot6After, Is.Not.SameAs(slot6Before));
            Assert.That(slot6After!.DeviceType, Is.EqualTo("set-config-card"));
            Assert.That(runtimeState.ActiveSetName, Is.EqualTo("workstation"));
            Assert.That(runtimeState.IsModified, Is.False);
        });
    }

    /// <summary>
    /// Verifies that <c>slot use</c> rejects unknown set names and non-off machine states.
    /// </summary>
    [Test]
    public void SlotCommand_Use_InvalidStateOrUnknownSet_ReturnsError()
    {
        var runtimeState = new SlotSetRuntimeState(
            new Dictionary<string, SlotSetRuntimeDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["game"] = new("game", null, []),
            },
            "game");
        var runningMachine = new Mock<IMachine>();
        runningMachine.SetupGet(m => m.State).Returns(MachineState.Running);
        runningMachine.Setup(m => m.GetComponent<SlotSetRuntimeState>()).Returns(runtimeState);
        debugContext.AttachMachine(runningMachine.Object);
        var command = new SlotCommand();
        var runningResult = command.Execute(debugContext, ["use", "game"]);

        var offMachine = new Mock<IMachine>();
        offMachine.SetupGet(m => m.State).Returns(MachineState.Off);
        offMachine.Setup(m => m.GetComponent<SlotSetRuntimeState>()).Returns(runtimeState);
        debugContext.AttachMachine(offMachine.Object);
        var unknownResult = command.Execute(debugContext, ["use", "missing"]);

        Assert.Multiple(() =>
        {
            Assert.That(runningResult.Success, Is.False);
            Assert.That(runningResult.Message, Is.EqualTo("Slot changes require the machine to be off. Use 'power --off' first."));
            Assert.That(unknownResult.Success, Is.False);
            Assert.That(unknownResult.Message, Is.EqualTo("Unknown slot set 'missing'."));
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

    /// <summary>
    /// Insert and remove change the live session only. The profile file on disk is left byte-for-byte unchanged.
    /// </summary>
    [Test]
    public void SlotCommand_InsertRemove_DoesNotModifyProfileFile()
    {
        string directory = Path.Join(Path.GetTempPath(), "bpe-slot-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string profilePath = Path.Join(directory, "session.json");
            File.WriteAllText(profilePath, "{\"name\":\"session\",\"devices\":{\"slots\":{\"cards\":[]}}}");
            byte[] before = File.ReadAllBytes(profilePath);
            string[] beforeFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

            var slotManager = new SlotManager(new IOPageDispatcher());
            IMachine machine = new MachineBuilder()
                .WithAddressSpace(16)
                .WithCpuFactory(static ctx => new Cpu65C02(ctx))
                .AddComponent(slotManager)
                .Build();
            debugContext.AttachMachine(machine);
            Assume.That(machine.State, Is.EqualTo(MachineState.Off));
            Assume.That(File.ReadAllBytes(profilePath), Is.EqualTo(before));

            var command = new SlotCommand();
            var insert = command.Execute(debugContext, ["insert", "5", "pocketwatch"]);
            var remove = command.Execute(debugContext, ["remove", "5"]);
            byte[] after = File.ReadAllBytes(profilePath);
            string[] afterFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

            Assert.Multiple(() =>
            {
                Assert.That(insert.Success, Is.True, insert.Message);
                Assert.That(remove.Success, Is.True, remove.Message);
                Assert.That(slotManager.GetCard(5), Is.Null);
                Assert.That(after, Is.EqualTo(before));
                Assert.That(afterFiles, Is.EqualTo(beforeFiles));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonElement CloneJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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
        public string TypeId { get; init; } = "test-config-slot-card";

        public JsonElement? AppliedConfig { get; init; }

        public int SlotNumber { get; set; }

        public SlotIOHandlers? IOHandlers => null;

        public IBusTarget? ROMRegion => null;

        public IBusTarget? ExpansionROMRegion => null;

        public string Name => "Test Config Slot Card";

        public string DeviceType => TypeId;

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