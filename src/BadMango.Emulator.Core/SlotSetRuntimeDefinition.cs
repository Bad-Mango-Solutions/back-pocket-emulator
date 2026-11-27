// <copyright file="SlotSetRuntimeDefinition.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using BadMango.Emulator.Core.Configuration;

/// <summary>
/// A resolved runtime slot set.
/// </summary>
/// <param name="Name">The set name.</param>
/// <param name="Description">Optional set description.</param>
/// <param name="Cards">The fully resolved slot cards for this set.</param>
public sealed record SlotSetRuntimeDefinition(
    string Name,
    string? Description,
    IReadOnlyList<SlotCardProfile> Cards);