// <copyright file="HostWatch.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A watch on the debugger extension.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="Access">Which accesses are watched.</param>
/// <param name="StopOnHit">Whether a hit should stop the machine. The loopback stores the flag and does not execute.</param>
/// <param name="Label">An optional label.</param>
public sealed record HostWatch(uint Address, HostWatchAccess Access, bool StopOnHit, string? Label);