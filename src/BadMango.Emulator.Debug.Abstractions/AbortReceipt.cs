// <copyright file="AbortReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json.Serialization;

using BadMango.Emulator.Core.Cpu;

/// <summary>
/// The abort reaction after an <c>abort</c> command.
/// </summary>
/// <param name="Reaction">The reaction now in effect.</param>
public sealed record AbortReceipt(
    [property: JsonConverter(typeof(JsonStringEnumConverter<AbortReaction>))] AbortReaction Reaction);