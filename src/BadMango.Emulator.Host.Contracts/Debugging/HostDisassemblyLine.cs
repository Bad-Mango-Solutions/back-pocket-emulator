// <copyright file="HostDisassemblyLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One disassembly line. The loopback reports the stub byte as data. A bound machine supplies a real mnemonic.
/// </summary>
/// <param name="Address">The instruction address.</param>
/// <param name="Length">The byte length.</param>
/// <param name="Bytes">The bytes, as hex.</param>
/// <param name="Mnemonic">The mnemonic.</param>
/// <param name="Operand">The operand text, or <see langword="null"/>.</param>
public sealed record HostDisassemblyLine(uint Address, int Length, string Bytes, string Mnemonic, string? Operand);