// <copyright file="MapTraceSwitch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// One soft switch inside a mapping-trace remap snapshot.
/// </summary>
/// <param name="Name">The switch name.</param>
/// <param name="Address">The switch address.</param>
/// <param name="Value">The switch value after the remap.</param>
public sealed record MapTraceSwitch(string Name, ushort Address, bool Value);