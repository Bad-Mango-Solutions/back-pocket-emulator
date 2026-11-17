// <copyright file="ResetReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// The result of a reset command.
/// </summary>
/// <param name="Kind">The reset that was applied.</param>
/// <param name="ProgramCounter">The program counter after the reset.</param>
public sealed record ResetReceipt(ResetKind Kind, uint ProgramCounter);