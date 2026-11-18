// <copyright file="CycleStepRegisters.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// Architectural registers for one <see cref="CycleStepCpu"/> instruction boundary.
/// </summary>
/// <param name="PC">Program counter.</param>
/// <param name="A">Accumulator.</param>
/// <param name="X">X index.</param>
/// <param name="Y">Y index.</param>
/// <param name="S">Stack pointer.</param>
/// <param name="P">Processor status, including the unused bits the test vectors check.</param>
public readonly record struct CycleStepRegisters(ushort PC, byte A, byte X, byte Y, byte S, byte P);