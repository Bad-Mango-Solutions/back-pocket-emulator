// <copyright file="HostBreakpointNotice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A breakpoint hit reported by the machine edge. The shell's <c>BreakpointHitEvent</c> stays a UI event.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="Address">The breakpoint address.</param>
public sealed record HostBreakpointNotice(MachineId MachineId, uint Address);