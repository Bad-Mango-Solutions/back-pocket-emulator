// <copyright file="ImpliedMode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>Generic implied addressing mode resolver.</summary>
public readonly struct ImpliedMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.Implied(cpu);
}