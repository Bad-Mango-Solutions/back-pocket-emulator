// <copyright file="IAddressingMode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Core.Cpu;

using Interfaces.Cpu;

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