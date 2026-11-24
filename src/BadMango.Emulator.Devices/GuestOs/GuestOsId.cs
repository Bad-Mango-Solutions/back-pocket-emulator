// <copyright file="GuestOsId.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Open identity of a guest operating system.
/// </summary>
/// <remarks>
/// <para>
/// A new operating system is a new probe with its own id. Nothing in the shared
/// detector has to change. Comparison is ordinal, so <c>dos33</c> and <c>DOS33</c> differ.
/// </para>
/// <para>
/// <see cref="None"/> is no DOS, ProDOS, or other recognized system in memory, including
/// a custom-booted game. <see cref="GuestOsBootStage.CustomBoot"/> distinguishes that game
/// from nothing booted yet. <see cref="Unknown"/> is a recognized shape whose version is not known.
/// </para>
/// </remarks>
public readonly record struct GuestOsId : IComparable<GuestOsId>
{
    /// <summary>The id stored for <see cref="None"/>. A zeroed struct uses this too.</summary>
    public const string NoneValue = "none";

    /// <summary>No recognized operating system in memory. Value <c>none</c>.</summary>
    public static readonly GuestOsId None = new(NoneValue);

    /// <summary>A recognized shape whose version is not in the known table. Value <c>unknown</c>.</summary>
    public static readonly GuestOsId Unknown = new("unknown");

    /// <summary>DOS 3.3. Value <c>dos33</c>.</summary>
    public static readonly GuestOsId Dos33 = new("dos33");

    /// <summary>ProDOS 8. Value <c>prodos8</c>.</summary>
    public static readonly GuestOsId ProDos8 = new("prodos8");

    private readonly string? stored;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsId"/> struct.
    /// </summary>
    /// <param name="value">Stable id such as <c>dos33</c>. Not empty.</param>
    /// <exception cref="ArgumentException">If <paramref name="value"/> is empty.</exception>
    public GuestOsId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.stored = value;
    }

    /// <summary>Gets the stable id.</summary>
    /// <value>
    /// A non-empty ordinal string such as <c>prodos8</c>.
    /// <see langword="default"/> of this struct is <see cref="None"/>, so the value is <c>none</c>.
    /// </value>
    public string Value => this.stored ?? NoneValue;

    /// <summary>Compares two ids by ordinal string order.</summary>
    /// <param name="other">The id to compare.</param>
    /// <returns>The ordinal comparison of <see cref="Value"/>.</returns>
    public int CompareTo(GuestOsId other) => string.CompareOrdinal(this.Value, other.Value);

    /// <summary>Returns whether both ids have the same <see cref="Value"/>.</summary>
    /// <param name="other">The id to compare.</param>
    /// <returns><see langword="true"/> when the ordinal values match, including a zeroed struct and <see cref="None"/>.</returns>
    public bool Equals(GuestOsId other) => string.Equals(this.Value, other.Value, StringComparison.Ordinal);

    /// <summary>Returns an ordinal hash of <see cref="Value"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The id string. Never <see langword="null"/>.</returns>
    public override string ToString() => this.Value;
}