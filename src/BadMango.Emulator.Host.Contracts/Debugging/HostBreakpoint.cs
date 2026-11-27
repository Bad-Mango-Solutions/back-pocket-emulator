// <copyright file="HostBreakpoint.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A breakpoint on the debugger extension.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="Label">An optional label.</param>
public sealed record HostBreakpoint(uint Address, string? Label);