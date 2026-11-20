// <copyright file="SlotCardFactory.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Text.Json;

using BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Shared slot-card factory invocation helpers used by profile loading and runtime commands.
/// </summary>
public static class SlotCardFactory
{
    /// <summary>
    /// Creates a slot card from a registered factory map.
    /// </summary>
    /// <param name="builder">The builder context passed to the registered factory.</param>
    /// <param name="factories">Factory map keyed by slot-card type identifier.</param>
    /// <param name="type">The slot-card type identifier.</param>
    /// <param name="config">Optional per-card configuration JSON.</param>
    /// <param name="card">When successful, the created card; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the type exists and the card was created.</returns>
    public static bool TryCreate(
        MachineBuilder builder,
        IReadOnlyDictionary<string, Func<MachineBuilder, JsonElement?, ISlotCard>> factories,
        string type,
        JsonElement? config,
        out ISlotCard card)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        card = null!;
        if (!factories.TryGetValue(type, out var factory))
        {
            return false;
        }

        card = factory(builder, config);
        return true;
    }
}