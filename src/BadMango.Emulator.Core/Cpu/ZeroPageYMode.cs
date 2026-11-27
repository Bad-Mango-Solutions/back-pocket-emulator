// <copyright file="ZeroPageYMode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using Core.Cpu;
using Core.Interfaces.Cpu;

/// <summary>Generic zero-page,Y addressing mode resolver.</summary>
public readonly struct ZeroPageYMode : IAddressingMode
{
    /// <inheritdoc cref="IAddressingMode.Resolve(ICpu)" />
    public static Addr Resolve(ICpu cpu) => AddressingModes.ZeroPageY(cpu);
}