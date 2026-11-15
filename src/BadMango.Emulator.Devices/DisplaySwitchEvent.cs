// <copyright file="DisplaySwitchEvent.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// One recorded display-switch change inside a frame window.
/// </summary>
/// <param name="CycleInFrame">
/// The cycle of the change, counted from the frame window start (<c>scheduler.Now − frameStartCycle</c>).
/// </param>
/// <param name="Switch">The switch that changed.</param>
/// <param name="Value">The value written.</param>
public readonly record struct DisplaySwitchEvent(uint CycleInFrame, DisplaySwitch Switch, bool Value);