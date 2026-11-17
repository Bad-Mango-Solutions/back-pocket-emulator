// <copyright file="PauseReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// The payload for <c>pause</c>.
/// </summary>
/// <param name="MachineState">The machine state after the pause.</param>
/// <param name="ProgramCounter">The program counter after the pause.</param>
public sealed record PauseReceipt(string MachineState, uint ProgramCounter);