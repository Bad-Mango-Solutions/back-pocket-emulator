// <copyright file="MachineId.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Addresses one machine on a host. A host can run more than one machine.
/// </summary>
public sealed class MachineId : IEquatable<MachineId>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MachineId"/> class.
    /// </summary>
    /// <param name="value">The opaque id. Callers do not parse it.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public MachineId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Gets the opaque id string.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Compares two ids by their opaque strings.
    /// </summary>
    /// <param name="left">The first id.</param>
    /// <param name="right">The second id.</param>
    /// <returns><see langword="true"/> when both are <see langword="null"/> or both have the same string.</returns>
    public static bool operator ==(MachineId? left, MachineId? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Compares two ids by their opaque strings.
    /// </summary>
    /// <param name="left">The first id.</param>
    /// <param name="right">The second id.</param>
    /// <returns><see langword="true"/> when the ids differ.</returns>
    public static bool operator !=(MachineId? left, MachineId? right) => !(left == right);

    /// <summary>
    /// Creates a new id.
    /// </summary>
    /// <returns>A new machine id.</returns>
    public static MachineId New() => new(Guid.NewGuid().ToString("n"));

    /// <summary>
    /// Compares this id with another by the opaque string.
    /// </summary>
    /// <param name="other">The other id.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> has the same string.</returns>
    public bool Equals(MachineId? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>
    /// Compares this id with another object.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is a <see cref="MachineId"/> with the same string.</returns>
    public override bool Equals(object? obj) => obj is MachineId other && Equals(other);

    /// <summary>
    /// Returns a hash code for the opaque string.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => string.GetHashCode(Value, StringComparison.Ordinal);

    /// <summary>
    /// Returns the opaque id string.
    /// </summary>
    /// <returns>The id string.</returns>
    public override string ToString() => Value;
}