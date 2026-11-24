// <copyright file="GuestOsInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// What an on-demand guest-OS detection reported, and why.
/// </summary>
/// <remarks>
/// <para>
/// Two values are equal when every property matches. <see cref="IGuestOsDetector"/>
/// raises <see cref="IGuestOsDetector.Changed"/> only when the new value differs.
/// </para>
/// <para>
/// The fields here are the same for every operating system. ProDOS clock and version
/// bytes live on <see cref="ProDos8Details"/>. A custom-booted game stays
/// <see cref="GuestOsId.None"/>; <see cref="GuestOsBootStage.CustomBoot"/> distinguishes
/// it from nothing booted yet. Detection does not identify specific titles.
/// </para>
/// </remarks>
public sealed record GuestOsInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsInfo"/> class.
    /// </summary>
    /// <param name="id">The operating system, <see cref="GuestOsId.None"/>, or <see cref="GuestOsId.Unknown"/>.</param>
    /// <param name="displayName">Short name such as "ProDOS 8" or "DOS 3.3". Not empty.</param>
    /// <param name="versionLabel">Version text such as "ProDOS 8 2.4.x". May be empty.</param>
    /// <param name="reason">Why this result was reported. Not empty.</param>
    /// <param name="details">OS-specific facts, or <see langword="null"/> when there are none.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="displayName"/> or <paramref name="versionLabel"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If <paramref name="displayName"/> or <paramref name="reason"/> is empty.</exception>
    public GuestOsInfo(GuestOsId id, string displayName, string versionLabel, string reason, IGuestOsDetails? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(versionLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        this.Id = id;
        this.DisplayName = displayName;
        this.VersionLabel = versionLabel;
        this.Reason = reason;
        this.Details = details;
    }

    /// <summary>Gets the operating system identity.</summary>
    /// <value>The id.</value>
    public GuestOsId Id { get; }

    /// <summary>Gets the short name of the operating system.</summary>
    /// <value>A name such as "ProDOS 8" or "None".</value>
    public string DisplayName { get; }

    /// <summary>Gets the short version label.</summary>
    /// <value>A label such as "ProDOS 8 2.4.x", or empty when <see cref="Id"/> is <see cref="GuestOsId.None"/>.</value>
    public string VersionLabel { get; }

    /// <summary>Gets why this result was reported.</summary>
    /// <value>A non-empty explanation.</value>
    public string Reason { get; }

    /// <summary>Gets OS-specific facts.</summary>
    /// <value>
    /// <see cref="ProDos8Details"/> or <see cref="Dos33Details"/> when the probe has them;
    /// otherwise <see langword="null"/>.
    /// </value>
    public IGuestOsDetails? Details { get; }
}