// <copyright file="HostResetNotice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A reset that was accepted. The run state does not change.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="Hard"><see langword="true"/> for Hard Reset, which cycles power. <see langword="false"/> for Reset.</param>
public sealed record HostResetNotice(MachineId MachineId, bool Hard);