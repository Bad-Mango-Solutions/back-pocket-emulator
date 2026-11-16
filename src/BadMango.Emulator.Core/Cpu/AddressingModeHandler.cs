// <copyright file="AddressingModeHandler.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

using Interfaces.Cpu;

/// <summary>
/// Delegate representing an addressing mode that computes an effective address.
/// </summary>
/// <param name="cpu">The CPU instance providing memory access and state.</param>
/// <returns>The effective address computed by the addressing mode.</returns>
/// <remarks>
/// Addressing modes access CPU state via <see cref="ICpu.Registers"/> and memory via
/// <see cref="ICpu.Read8"/>, <see cref="ICpu.Read16"/>, etc.
/// </remarks>
public delegate Addr AddressingModeHandler(ICpu cpu);

/// <summary>
/// Defines a generic addressing mode contract with a static resolver.
/// </summary>
public interface IAddressingMode
{
    /// <summary>
    /// Resolves the effective address for this addressing mode.
    /// </summary>
    /// <param name="cpu">The CPU instance providing memory and register state.</param>
    /// <returns>The effective address (or encoded mode payload) for the current instruction.</returns>
    static abstract Addr Resolve(ICpu cpu);
}