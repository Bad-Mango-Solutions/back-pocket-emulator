// <copyright file="Wdc65C02VectorCase.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

/// <summary>
/// One SingleStepTests <c>wdc65c02</c> case, without the per-cycle bus trace.
/// </summary>
/// <param name="Name">The upstream case name.</param>
/// <param name="Pc">The initial program counter.</param>
/// <param name="A">The initial accumulator.</param>
/// <param name="X">The initial X register.</param>
/// <param name="Y">The initial Y register.</param>
/// <param name="S">The initial stack pointer.</param>
/// <param name="P">The initial status byte.</param>
/// <param name="FinalPc">The expected program counter.</param>
/// <param name="FinalA">The expected accumulator.</param>
/// <param name="FinalX">The expected X register.</param>
/// <param name="FinalY">The expected Y register.</param>
/// <param name="FinalS">The expected stack pointer.</param>
/// <param name="FinalP">The expected status byte.</param>
/// <param name="Cycles">The number of entries in the upstream <c>cycles</c> array.</param>
/// <param name="InitialRam">Bytes applied before the instruction. Addresses not listed are zero.</param>
/// <param name="FinalRam">Bytes compared after the instruction.</param>
internal readonly record struct Wdc65C02VectorCase(
    string Name,
    ushort Pc,
    byte A,
    byte X,
    byte Y,
    byte S,
    byte P,
    ushort FinalPc,
    byte FinalA,
    byte FinalX,
    byte FinalY,
    byte FinalS,
    byte FinalP,
    int Cycles,
    (ushort Address, byte Value)[] InitialRam,
    (ushort Address, byte Value)[] FinalRam);