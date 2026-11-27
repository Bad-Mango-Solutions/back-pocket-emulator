// <copyright file="HostStateNotice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A run-state change. This is the host signal. The shell's <c>MachineStateChangedEvent</c> stays a UI event.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="State">The state after the change.</param>
public sealed record HostStateNotice(MachineId MachineId, HostRunState State);