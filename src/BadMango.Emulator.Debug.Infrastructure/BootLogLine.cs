// <copyright file="BootLogLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>One boot milestone in the log snapshot.</summary>
/// <param name="Cycle">The machine cycle.</param>
/// <param name="Pc">The program counter, four hex digits.</param>
/// <param name="Kind">The milestone kind name.</param>
/// <param name="Os">The operating system id, or <see langword="null"/> when the milestone is shared.</param>
/// <param name="Detail">What the monitor saw.</param>
public sealed record BootLogLine(ulong Cycle, string Pc, string Kind, string? Os, string Detail);