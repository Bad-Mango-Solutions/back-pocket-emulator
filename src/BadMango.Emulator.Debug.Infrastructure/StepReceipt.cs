// <copyright file="StepReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// The result of a step command.
/// </summary>
/// <param name="InstructionsExecuted">The number of instructions the session stepped.</param>
/// <param name="ProgramCounter">The program counter after the step.</param>
public sealed record StepReceipt(ulong InstructionsExecuted, uint ProgramCounter);