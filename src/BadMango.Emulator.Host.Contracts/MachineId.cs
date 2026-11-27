// <copyright file="MachineId.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Addresses one machine on a host. A host can run more than one machine.
/// </summary>
/// <param name="Value">The opaque id. Callers do not parse it.</param>
public sealed record MachineId(string Value)
{
    /// <summary>
    /// Gets the opaque id string.
    /// </summary>
    public string Value { get; init; } = Require(Value);

    /// <summary>
    /// Creates a new id.
    /// </summary>
    /// <returns>A new machine id.</returns>
    public static MachineId New() => new(Guid.NewGuid().ToString("n"));

    /// <summary>
    /// Returns the opaque id string.
    /// </summary>
    /// <returns>The id string.</returns>
    public override string ToString() => Value;

    private static string Require(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}