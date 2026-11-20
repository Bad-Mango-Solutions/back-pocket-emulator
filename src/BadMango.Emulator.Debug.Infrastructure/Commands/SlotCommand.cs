// <copyright file="SlotCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;
using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
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
        : base("slot", "List, insert, or remove expansion slot cards")
    {
    }

    /// <inheritdoc/>
    public override string Usage => "slot <list|types|insert|remove> [args]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Shows the current slot map for slots 1-7, lists available slot-card type ids, " +
        "or mutates the live session by inserting/removing cards while the machine is off. " +
        "Runtime slot edits never rewrite the profile file on disk.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "slot list",
        "slot types",
        "slot insert 5 pocketwatch",
        "slot insert 6 disk-ii-compatible rom=slot6-boot",
        "slot remove 4",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "The insert/remove subcommands mutate only the live machine session and require the machine to be off.";

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
            "types" => ExecuteTypes(debugContext),
            "insert" => ExecuteInsert(debugContext, machine, args),
            "remove" => ExecuteRemove(machine, args),
            _ => CommandResult.UsageError(
                $"Unknown 'slot' subcommand '{args[0]}'. Use list, types, insert, or remove."),
        };
    }

    private static CommandResult ExecuteList(IDebugContext context)
    {
        if (!TryGetSlotManager(context, out var slotManager, out var error))
        {
            return CommandResult.Error(error!);
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

        return CommandResult.Ok($"Inserted {card.DeviceType} into slot {slot}.");
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

        return CommandResult.Ok($"Removed slot card from slot {slot}.");
    }

    private static bool TryGetSlotManager(ICommandContext context, out ISlotManager? slotManager, out string? error)
    {
        slotManager = null;
        error = null;
        if (context is not IDebugContext debugContext)
        {
            error = "Debug context required for this command.";
            return false;
        }

        if (debugContext.Machine is null)
        {
            error = "No machine attached.";
            return false;
        }

        slotManager = debugContext.Machine.GetComponent<ISlotManager>();
        if (slotManager is null)
        {
            error = "No slot manager available on the attached machine.";
            return false;
        }

        return true;
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