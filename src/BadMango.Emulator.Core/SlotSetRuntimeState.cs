// <copyright file="SlotSetRuntimeState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Collections.ObjectModel;

using BadMango.Emulator.Core.Configuration;

/// <summary>
/// Runtime metadata for profile-defined named slot sets.
/// </summary>
public sealed class SlotSetRuntimeState
{
    private readonly IReadOnlyDictionary<string, SlotSetRuntimeDefinition> sets;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlotSetRuntimeState"/> class.
    /// </summary>
    /// <param name="sets">Resolved set definitions keyed by name.</param>
    /// <param name="activeSetName">The currently active set name.</param>
    public SlotSetRuntimeState(
        IReadOnlyDictionary<string, SlotSetRuntimeDefinition> sets,
        string activeSetName)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSetName);

        this.sets = new ReadOnlyDictionary<string, SlotSetRuntimeDefinition>(
            new Dictionary<string, SlotSetRuntimeDefinition>(sets, StringComparer.OrdinalIgnoreCase));
        ActiveSetName = activeSetName;
    }

    /// <summary>
    /// Gets the resolved slot sets keyed by set name.
    /// </summary>
    public IReadOnlyDictionary<string, SlotSetRuntimeDefinition> Sets => sets;

    /// <summary>
    /// Gets the currently active set name.
    /// </summary>
    public string ActiveSetName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether runtime edits have diverged from the active named set.
    /// </summary>
    public bool IsModified { get; private set; }

    /// <summary>
    /// Marks the runtime state as matching the supplied set.
    /// </summary>
    /// <param name="setName">The applied set name.</param>
    public void MarkActiveSet(string setName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setName);
        ActiveSetName = setName;
        IsModified = false;
    }

    /// <summary>
    /// Marks the runtime state as manually modified.
    /// </summary>
    public void MarkModified()
    {
        IsModified = true;
    }
}