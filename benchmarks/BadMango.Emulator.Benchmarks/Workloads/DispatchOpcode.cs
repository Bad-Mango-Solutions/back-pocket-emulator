// <copyright file="DispatchOpcode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Workloads;

/// <summary>
/// One opcode selected by a per-opcode benchmark.
/// </summary>
public sealed class DispatchOpcode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DispatchOpcode"/> class.
    /// </summary>
    /// <param name="code">The opcode byte.</param>
    /// <param name="mnemonic">The assembly mnemonic, used as the benchmark parameter name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mnemonic"/> is null or empty.</exception>
    public DispatchOpcode(byte code, string mnemonic)
    {
        ArgumentException.ThrowIfNullOrEmpty(mnemonic);
        Code = code;
        Mnemonic = mnemonic;
    }

    /// <summary>
    /// Gets the opcode byte.
    /// </summary>
    public byte Code { get; }

    /// <summary>
    /// Gets the assembly mnemonic.
    /// </summary>
    public string Mnemonic { get; }

    /// <summary>
    /// Returns the mnemonic so a benchmark report names the opcode.
    /// </summary>
    /// <returns>The mnemonic.</returns>
    public override string ToString() => Mnemonic;
}