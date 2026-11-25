// <copyright file="OsCallLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>One OS call in the log snapshot.</summary>
/// <param name="Cycle">The machine cycle.</param>
/// <param name="Os">The operating system id, such as <c>prodos8</c> or <c>dos33</c>.</param>
/// <param name="Code">The command byte, <c>$</c> and two hex digits.</param>
/// <param name="Name">The command name, or <c>$xx (unknown)</c>.</param>
/// <param name="Caller">The calling instruction, four hex digits with <c>$</c>.</param>
/// <param name="ParamList">The parameter list or IOB, four hex digits with <c>$</c>.</param>
/// <param name="Summary">A pathname or RWTS location, or <see langword="null"/>.</param>
public sealed record OsCallLine(
    ulong Cycle,
    string Os,
    string Code,
    string Name,
    string Caller,
    string ParamList,
    string? Summary);