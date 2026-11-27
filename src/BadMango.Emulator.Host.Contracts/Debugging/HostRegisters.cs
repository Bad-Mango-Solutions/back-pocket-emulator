// <copyright file="HostRegisters.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// A register snapshot that does not reference the CPU type.
/// </summary>
/// <param name="A">The accumulator.</param>
/// <param name="X">The X index.</param>
/// <param name="Y">The Y index.</param>
/// <param name="StackPointer">The stack pointer.</param>
/// <param name="ProgramCounter">The program counter.</param>
/// <param name="Status">The status byte.</param>
public sealed record HostRegisters(byte A, byte X, byte Y, byte StackPointer, ushort ProgramCounter, byte Status);