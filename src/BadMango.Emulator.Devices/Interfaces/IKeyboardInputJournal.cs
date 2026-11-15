// <copyright file="IKeyboardInputJournal.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Core;

/// <summary>
/// Read-only journal of input events that were applied to a machine.
/// </summary>
public interface IKeyboardInputJournal : IReadOnlyList<JournalEntry>
{
}

/// <summary>
/// A single journal entry representing a machine input event at a specific cycle.
/// </summary>
/// <param name="Cycle">The cycle at which the event was applied.</param>
/// <param name="Event">The applied input event.</param>
public readonly record struct JournalEntry(Cycle Cycle, InputEvent Event);