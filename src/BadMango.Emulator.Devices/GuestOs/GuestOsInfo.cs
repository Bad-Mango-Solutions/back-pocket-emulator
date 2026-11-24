// <copyright file="GuestOsInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// What an on-demand guest-OS detection reported, and why.
/// </summary>
/// <remarks>
/// Two values are equal when every property matches. <see cref="IGuestOsDetector"/>
/// raises <see cref="IGuestOsDetector.Changed"/> only when the new value differs.
/// A custom-booted game stays <see cref="GuestOsKind.None"/>; <see cref="GuestOsBootStage.CustomBoot"/>
/// distinguishes it from nothing booted yet. Detection does not identify specific titles.
/// </remarks>
public sealed record GuestOsInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsInfo"/> class.
    /// </summary>
    /// <param name="kind">The detected operating system.</param>
    /// <param name="versionLabel">Short label such as "ProDOS 8 2.4.x" or "DOS 3.3". May be empty.</param>
    /// <param name="kVersion"><c>KVERSION</c> at <c>$BFFF</c>, or <see langword="null"/> when the page has none.</param>
    /// <param name="iVersion"><c>IVERSION</c> at <c>$BFFD</c>, or <see langword="null"/>.</param>
    /// <param name="machId"><c>MACHID</c> at <c>$BF98</c>, or <see langword="null"/>.</param>
    /// <param name="hasClock"><see langword="true"/> when <c>MACHID</c> bit 0 is set.</param>
    /// <param name="reason">Why this result was reported. Not empty.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="versionLabel"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty.</exception>
    public GuestOsInfo(
        GuestOsKind kind,
        string versionLabel,
        byte? kVersion,
        byte? iVersion,
        byte? machId,
        bool hasClock,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(versionLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        this.Kind = kind;
        this.VersionLabel = versionLabel;
        this.KVersion = kVersion;
        this.IVersion = iVersion;
        this.MachId = machId;
        this.HasClock = hasClock;
        this.Reason = reason;
    }

    /// <summary>Gets the detected operating system.</summary>
    /// <value>The kind.</value>
    public GuestOsKind Kind { get; }

    /// <summary>Gets the short version label.</summary>
    /// <value>A label such as "ProDOS 8 2.4.x", or empty when <see cref="Kind"/> is <see cref="GuestOsKind.None"/>.</value>
    public string VersionLabel { get; }

    /// <summary>Gets ProDOS <c>KVERSION</c>.</summary>
    /// <value>The byte at <c>$BFFF</c>, or <see langword="null"/> for DOS and for no signature.</value>
    public byte? KVersion { get; }

    /// <summary>Gets ProDOS <c>IVERSION</c>.</summary>
    /// <value>The byte at <c>$BFFD</c>, or <see langword="null"/> when ProDOS is not present.</value>
    public byte? IVersion { get; }

    /// <summary>Gets ProDOS <c>MACHID</c>.</summary>
    /// <value>The byte at <c>$BF98</c>, or <see langword="null"/> when ProDOS is not present.</value>
    public byte? MachId { get; }

    /// <summary>Gets a value indicating whether <c>MACHID</c> bit 0 is set.</summary>
    /// <value><see langword="true"/> when the global page reports a recognizable clock.</value>
    public bool HasClock { get; }

    /// <summary>Gets why this result was reported.</summary>
    /// <value>A non-empty explanation.</value>
    public string Reason { get; }
}