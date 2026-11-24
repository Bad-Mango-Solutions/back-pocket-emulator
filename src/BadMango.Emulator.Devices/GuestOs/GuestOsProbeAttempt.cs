// <copyright file="GuestOsProbeAttempt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One guest OS a detector tried, and why that probe matched or rejected the memory image.
/// </summary>
public sealed record GuestOsProbeAttempt
{
    private GuestOsProbeAttempt(GuestOsKind kind, bool recognized, string reason, GuestOsInfo? info)
    {
        this.Kind = kind;
        this.Recognized = recognized;
        this.Reason = reason;
        this.Info = info;
    }

    /// <summary>Gets the probe that was tried.</summary>
    /// <value>The probe's kind.</value>
    public GuestOsKind Kind { get; }

    /// <summary>Gets a value indicating whether this probe recognized the memory image.</summary>
    /// <value><see langword="true"/> when <see cref="Info"/> is set.</value>
    public bool Recognized { get; }

    /// <summary>Gets why the probe matched or rejected the image.</summary>
    /// <value>A non-empty explanation.</value>
    public string Reason { get; }

    /// <summary>Gets the recognized guest OS.</summary>
    /// <value>The info when <see cref="Recognized"/> is <see langword="true"/>; otherwise <see langword="null"/>.</value>
    public GuestOsInfo? Info { get; }

    /// <summary>
    /// Creates an attempt that recognized a guest OS.
    /// </summary>
    /// <param name="kind">The probe's kind. Not <see cref="GuestOsKind.None"/>.</param>
    /// <param name="reason">Why the bytes were accepted. Not empty.</param>
    /// <param name="info">The recognized guest OS. Its kind is <paramref name="kind"/> or <see cref="GuestOsKind.Unknown"/>.</param>
    /// <returns>A recognized attempt.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty, <paramref name="kind"/> is <see cref="GuestOsKind.None"/>, or <paramref name="info"/> does not belong to this probe.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="info"/> is <see langword="null"/>.</exception>
    public static GuestOsProbeAttempt Match(GuestOsKind kind, string reason, GuestOsInfo info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(info);
        if (kind == GuestOsKind.None)
        {
            throw new ArgumentException("A match names the probe, not None.", nameof(kind));
        }

        if (info.Kind == GuestOsKind.None || (info.Kind != kind && info.Kind != GuestOsKind.Unknown))
        {
            throw new ArgumentException("The guest OS info does not belong to this probe.", nameof(info));
        }

        return new GuestOsProbeAttempt(kind, true, reason, info);
    }

    /// <summary>
    /// Creates an attempt that rejected the memory image.
    /// </summary>
    /// <param name="kind">The probe that was tried.</param>
    /// <param name="reason">Why the bytes are not this operating system. Not empty.</param>
    /// <returns>A rejected attempt with no <see cref="Info"/>.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty.</exception>
    public static GuestOsProbeAttempt Reject(GuestOsKind kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new GuestOsProbeAttempt(kind, false, reason, null);
    }
}