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
    /// <summary>No recognized operating system in memory. Value <c>none</c>.</summary>
    public static readonly GuestOsId None = new("none");

    /// <summary>A recognized shape whose version is not in the known table. Value <c>unknown</c>.</summary>
    public static readonly GuestOsId Unknown = new("unknown");

    /// <summary>DOS 3.3. Value <c>dos33</c>.</summary>
    public static readonly GuestOsId Dos33 = new("dos33");

    /// <summary>ProDOS 8. Value <c>prodos8</c>.</summary>
    public static readonly GuestOsId ProDos8 = new("prodos8");

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsId"/> struct.
    /// </summary>
    /// <param name="value">Stable id such as <c>dos33</c>. Not empty.</param>
    /// <exception cref="ArgumentException">If <paramref name="value"/> is empty.</exception>
    public GuestOsId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.Value = value;
    }

    /// <summary>Gets the stable id.</summary>
    /// <value>A non-empty ordinal string such as <c>prodos8</c>.</value>
    public string Value { get; }

    /// <summary>Compares two ids by ordinal string order.</summary>
    /// <param name="other">The id to compare.</param>
    /// <returns>The ordinal comparison of <see cref="Value"/>.</returns>
    public int CompareTo(GuestOsId other) => string.CompareOrdinal(this.Value, other.Value);

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The id string.</returns>
    public override string ToString() => this.Value;
}