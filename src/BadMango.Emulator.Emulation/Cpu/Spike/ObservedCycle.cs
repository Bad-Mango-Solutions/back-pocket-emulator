// <copyright file="ObservedCycle.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// One bus cycle observed while a <see cref="CycleStepCpu"/> instruction runs.
/// </summary>
/// <param name="Address">The 16-bit address driven on the bus.</param>
/// <param name="Data">The byte read or written.</param>
/// <param name="IsWrite"><see langword="true"/> when the cycle is a write.</param>
public readonly record struct ObservedCycle(ushort Address, byte Data, bool IsWrite);