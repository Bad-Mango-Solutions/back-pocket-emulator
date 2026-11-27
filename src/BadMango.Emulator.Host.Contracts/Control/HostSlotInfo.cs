// <copyright file="HostSlotInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One row of the slot strip. Writes that change a slot are profile-document edits and are legal only while the machine is off.
/// </summary>
/// <param name="Id">The slot id. <c>aux</c>, or <c>1</c> through <c>7</c>.</param>
/// <param name="Label">The label, such as <c>S6</c> or <c>Aux</c>.</param>
/// <param name="CardName">The card name, or <see langword="null"/> when the slot is empty.</param>
/// <param name="Enabled">Whether the slot is enabled.</param>
public sealed record HostSlotInfo(string Id, string Label, string? CardName, bool Enabled);