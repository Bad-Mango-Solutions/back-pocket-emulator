// <copyright file="SlotCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;
using BadMango.Emulator.Devices;

/// <summary>
/// Lists and mutates runtime slot-card assignments for the current debug session.
/// </summary>
public sealed class SlotCommand : CommandHandlerBase, ICommandHelp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SlotCommand"/> class.
    /// </summary>
    public SlotCommand()
        : base("slot", "List, inspect sets, insert, remove, or switch slot-card assignments")
    {
    }

    /// <inheritdoc/>
    public override string Usage => "slot <list|sets|types|use|insert|remove> [args]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Shows the current slot map for slots 1-7, lists available slot-card type ids, " +
        "lists profile-defined slot sets, switches sets, or mutates the live session by inserting/removing cards " +
        "while the machine is off. " +
        "Runtime slot edits never rewrite the profile file on disk.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "slot list",
        "slot sets",
        "slot use game",
        "slot types",
        "slot insert 5 pocketwatch",
        "slot insert 6 disk-ii-compatible rom=slot6-boot",
        "slot remove 4",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "The use/insert/remove subcommands mutate only the live machine session and require the machine to be off.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["devicetypes", "devicemap", "power"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (debugContext.Machine is not IMachine machine)
        {
            return CommandResult.Error("No machine attached.");
        }

        if (args.Length == 0)
        {
            return CommandResult.UsageError("Usage: slot <list|types|insert|remove> [args].");
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => ExecuteList(debugContext),
            "sets" => ExecuteSets(machine, debugContext),
            "types" => ExecuteTypes(debugContext),
            "use" => ExecuteUse(debugContext, machine, args),
            "insert" => ExecuteInsert(debugContext, machine, args),
            "remove" => ExecuteRemove(machine, args),
            _ => CommandResult.UsageError(
                $"Unknown 'slot' subcommand '{args[0]}'. Use list, sets, types, use, insert, or remove."),
        };
    }

    private static CommandResult ExecuteList(IDebugContext context)
    {
        if (!DiskRuntimeHelpers.TryGetSlotManager(context, out var slotManager, out var error))
        {
            return CommandResult.Error(error!);
        }

        var runtimeState = context.Machine?.GetComponent<SlotSetRuntimeState>();
        if (runtimeState is not null)
        {
            string activeLabel = runtimeState.IsModified
                ? "(modified)"
                : runtimeState.ActiveSetName;
            context.Output.WriteLine($"Active slot set: {activeLabel}");
        }

        context.Output.WriteLine("Slot  Type                  Name                        Notes");
        context.Output.WriteLine("────  ────────────────────  ──────────────────────────  ─────────────────────");
        for (int slot = PeripheralSlots.Minimum; slot <= PeripheralSlots.Maximum; slot++)
        {
            var card = slotManager!.GetCard(slot);
            string type = card?.DeviceType ?? "(empty)";
            string name = card?.Name ?? "(empty)";
            string notes = slot == 3 && card is null && slotManager.GetSlotRomRegion(3) is not null
                ? "internal C3 ROM"
                : string.Empty;
            context.Output.WriteLine($"{slot,4}  {type,-20}  {name,-26}  {notes}");
        }

        return CommandResult.Ok();
    }

    private static CommandResult ExecuteSets(IMachine machine, IDebugContext context)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(context);

        var runtimeState = machine.GetComponent<SlotSetRuntimeState>();
        if (runtimeState is null || runtimeState.Sets.Count == 0)
        {
            context.Output.WriteLine("No named slot sets are configured for this machine.");
            return CommandResult.Ok();
        }

        string activeSet = runtimeState.ActiveSetName;
        context.Output.WriteLine("Slot sets:");
        foreach (var set in runtimeState.Sets.Values.OrderBy(static set => set.Name, StringComparer.OrdinalIgnoreCase))
        {
            string marker = string.Equals(set.Name, activeSet, StringComparison.OrdinalIgnoreCase)
                ? runtimeState.IsModified ? " (active, modified)" : " (active)"
                : string.Empty;
            string description = string.IsNullOrWhiteSpace(set.Description) ? string.Empty : $" - {set.Description}";
            context.Output.WriteLine($"  {set.Name}{marker}{description}");
        }

        return CommandResult.Ok();
    }

    private static CommandResult ExecuteTypes(IDebugContext context)
    {
        var slotCards = DeviceTypesCommand.GetSortedSlotCardTypeIds();

        context.Output.WriteLine($"Slot cards ({slotCards.Count}):");
        if (slotCards.Count == 0)
        {
            context.Output.WriteLine("  (none)");
            return CommandResult.Ok();
        }

        foreach (var typeId in slotCards)
        {
            context.Output.WriteLine($"  {typeId}");
        }

        return CommandResult.Ok();
    }

    private static CommandResult ExecuteInsert(IDebugContext context, IMachine machine, string[] args)
    {
        if (args.Length < 3)
        {
            return CommandResult.UsageError("Usage: slot insert <slot> <type> [key=value ...].");
        }

        if (!TryParseSlot(args[1], out int slot))
        {
            return CommandResult.UsageError(
                $"Slot must be an integer in {PeripheralSlots.Minimum}..{PeripheralSlots.Maximum}; got '{args[1]}'.");
        }

        if (machine.State != MachineState.Off)
        {
            return CommandResult.Error("Slot changes require the machine to be off. Use 'power --off' first.");
        }

        string cardType = args[2];
        JsonElement? config;
        try
        {
            config = BuildConfig(args.AsSpan(3));
        }
        catch (FormatException ex)
        {
            return CommandResult.UsageError(ex.Message);
        }

        DeviceFactoryRegistry.EnsureInitialized();
        var builder = new MachineBuilder();
        if (!SlotCardFactory.TryCreate(
            builder,
            DeviceFactoryRegistry.SlotCardFactories,
            cardType,
            config,
            out var card))
        {
            var validTypes = string.Join(", ", DeviceTypesCommand.GetSortedSlotCardTypeIds());
            return CommandResult.Error(
                $"Unknown slot-card type '{cardType}'. Valid types: {validTypes}.");
        }

        try
        {
            machine.InsertSlotCard(slot, card);
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }

        machine.GetComponent<SlotSetRuntimeState>()?.MarkModified();
        return CommandResult.Ok($"Inserted {card.DeviceType} into slot {slot}.");
    }

    private static CommandResult ExecuteUse(IDebugContext context, IMachine machine, string[] args)
    {
        if (args.Length != 2)
        {
            return CommandResult.UsageError("Usage: slot use <name>.");
        }

        if (machine.State != MachineState.Off)
        {
            return CommandResult.Error("Slot changes require the machine to be off. Use 'power --off' first.");
        }

        var runtimeState = machine.GetComponent<SlotSetRuntimeState>();
        if (runtimeState is null || runtimeState.Sets.Count == 0)
        {
            return CommandResult.Error("No named slot sets are configured for this machine.");
        }

        if (!runtimeState.Sets.TryGetValue(args[1], out var target))
        {
            return CommandResult.Error($"Unknown slot set '{args[1]}'.");
        }

        if (!DiskRuntimeHelpers.TryGetSlotManager(context, out var slotManager, out var error))
        {
            return CommandResult.Error(error!);
        }

        var desiredBySlot = target.Cards.ToDictionary(card => card.Slot);
        var warnings = new List<string>();

        for (int slot = PeripheralSlots.Minimum; slot <= PeripheralSlots.Maximum; slot++)
        {
            desiredBySlot.TryGetValue(slot, out var targetCard);
            var existing = slotManager!.GetCard(slot);
            if (!NeedsReplacement(runtimeState, slot, targetCard, existing))
            {
                continue;
            }

            if (existing is not null)
            {
                machine.RemoveSlotCard(slot);
            }

            if (targetCard is null)
            {
                continue;
            }

            var built = TryCreateSlotCard(targetCard);
            if (built is null)
            {
                warnings.Add($"Skipped slot {slot}: unknown slot-card type '{targetCard.Type}'.");
                continue;
            }

            machine.InsertSlotCard(slot, built);
        }

        runtimeState.MarkActiveSet(target.Name);

        foreach (var warning in warnings)
        {
            context.Output.WriteLine($"Warning: {warning}");
        }

        return warnings.Count == 0
            ? CommandResult.Ok($"Applied slot set '{target.Name}'.")
            : CommandResult.Ok($"Applied slot set '{target.Name}' with {warnings.Count} warning(s).");
    }

    private static CommandResult ExecuteRemove(IMachine machine, string[] args)
    {
        if (args.Length != 2)
        {
            return CommandResult.UsageError("Usage: slot remove <slot>.");
        }

        if (!TryParseSlot(args[1], out int slot))
        {
            return CommandResult.UsageError(
                $"Slot must be an integer in {PeripheralSlots.Minimum}..{PeripheralSlots.Maximum}; got '{args[1]}'.");
        }

        if (machine.State != MachineState.Off)
        {
            return CommandResult.Error("Slot changes require the machine to be off. Use 'power --off' first.");
        }

        try
        {
            machine.RemoveSlotCard(slot);
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
        catch (IOException ex)
        {
            return CommandResult.Error($"Slot removal failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return CommandResult.Error($"Slot removal failed: {ex.Message}");
        }

        machine.GetComponent<SlotSetRuntimeState>()?.MarkModified();
        return CommandResult.Ok($"Removed slot card from slot {slot}.");
    }

    private static bool NeedsReplacement(
        SlotSetRuntimeState runtimeState,
        int slot,
        SlotCardProfile? targetCard,
        ISlotCard? existingCard)
    {
        if (!runtimeState.IsModified &&
            runtimeState.Sets.TryGetValue(runtimeState.ActiveSetName, out var activeSet))
        {
            var activeCard = activeSet.Cards.FirstOrDefault(card => card.Slot == slot);
            if (CardsEquivalent(activeCard, targetCard))
            {
                return false;
            }
        }

        if (targetCard is null)
        {
            return existingCard is not null;
        }

        return existingCard is null ||
            !string.Equals(existingCard.DeviceType, targetCard.Type, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CardsEquivalent(SlotCardProfile? left, SlotCardProfile? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Slot != right.Slot ||
            !string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.Preset, right.Preset, StringComparison.Ordinal))
        {
            return false;
        }

        return JsonEquals(left.Config, right.Config);
    }

    private static bool JsonEquals(JsonElement? left, JsonElement? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return left.Value.GetRawText() == right.Value.GetRawText();
    }

    private static ISlotCard? TryCreateSlotCard(SlotCardProfile cardProfile)
    {
        DeviceFactoryRegistry.EnsureInitialized();
        var builder = new MachineBuilder();

        bool created = SlotCardFactory.TryCreate(
            builder,
            DeviceFactoryRegistry.SlotCardFactories,
            cardProfile.Type,
            cardProfile.Config,
            out var card);

        return created ? card : null;
    }

    private static bool TryParseSlot(string token, out int slot)
    {
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out slot)
            && slot >= PeripheralSlots.Minimum
            && slot <= PeripheralSlots.Maximum;
    }

    private static JsonElement? BuildConfig(ReadOnlySpan<string> args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var pair in args)
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0 || equals == pair.Length - 1)
                {
                    throw new FormatException($"Invalid config token '{pair}'. Expected key=value.");
                }

                string key = pair[..equals];
                string value = pair[(equals + 1)..];
                writer.WritePropertyName(key);
                WriteConfigValue(writer, value);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteConfigValue(Utf8JsonWriter writer, string value)
    {
        if (bool.TryParse(value, out bool boolValue))
        {
            writer.WriteBooleanValue(boolValue);
            return;
        }

        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteNullValue();
            return;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
        {
            writer.WriteNumberValue(longValue);
            return;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        writer.WriteStringValue(value);
    }
}