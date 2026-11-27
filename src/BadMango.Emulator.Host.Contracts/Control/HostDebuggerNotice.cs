// <copyright file="HostDebuggerNotice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Debugger attach or detach.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="Attached"><see langword="true"/> when the debugger is attached after the change.</param>
public sealed record HostDebuggerNotice(MachineId MachineId, bool Attached);