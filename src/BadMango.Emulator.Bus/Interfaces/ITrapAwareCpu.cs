// <copyright file="ITrapAwareCpu.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// A CPU that executes call traps from a shared <see cref="ITrapRegistry"/>.
/// </summary>
/// <remarks>
/// The machine builder assigns the machine's registry onto this property.
/// A CPU constructed on its own may still default to the null registry.
/// </remarks>
public interface ITrapAwareCpu
{
    /// <summary>
    /// Gets or sets the trap registry consulted during instruction fetch.
    /// </summary>
    /// <value>The registry shared with the machine component bag.</value>
    ITrapRegistry TrapRegistry { get; set; }
}