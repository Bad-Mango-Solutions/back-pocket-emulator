// <copyright file="Wdc65C02VectorRunResult.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

/// <summary>
/// The outcome of running every case for one opcode.
/// </summary>
/// <param name="Cases">How many cases ran.</param>
/// <param name="Failed">How many cases disagreed on registers, flags, RAM, or cycle count.</param>
/// <param name="Example">The first disagreement, or <see langword="null"/> when <paramref name="Failed"/> is zero.</param>
internal readonly record struct Wdc65C02VectorRunResult(int Cases, int Failed, string? Example);