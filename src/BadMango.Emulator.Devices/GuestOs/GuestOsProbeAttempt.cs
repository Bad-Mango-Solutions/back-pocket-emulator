// <copyright file="GuestOsProbeAttempt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One guest OS a detector tried, and why that probe matched or rejected the memory image.
/// </summary>
public sealed record GuestOsProbeAttempt
{
    private GuestOsProbeAttempt(GuestOsId id, bool recognized, string reason, GuestOsInfo? info)
    {
        this.Id = id;
        this.Recognized = recognized;
        this.Reason = reason;
        this.Info = info;
    }

    /// <summary>Gets the probe that was tried.</summary>
    /// <value>The probe's id.</value>
    public GuestOsId Id { get; }

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
    /// <param name="id">The probe's id. Not <see cref="GuestOsId.None"/>.</param>
    /// <param name="reason">Why the bytes were accepted. Not empty.</param>
    /// <param name="info">The recognized guest OS. Its id is <paramref name="id"/> or <see cref="GuestOsId.Unknown"/>.</param>
    /// <returns>A recognized attempt.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty, <paramref name="id"/> is <see cref="GuestOsId.None"/>, or <paramref name="info"/> does not belong to this probe.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="info"/> is <see langword="null"/>.</exception>
    public static GuestOsProbeAttempt Match(GuestOsId id, string reason, GuestOsInfo info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(info);
        if (id == GuestOsId.None)
        {
            throw new ArgumentException("A match names the probe, not None.", nameof(id));
        }

        if (info.Id == GuestOsId.None || (info.Id != id && info.Id != GuestOsId.Unknown))
        {
            throw new ArgumentException("The guest OS info does not belong to this probe.", nameof(info));
        }

        return new GuestOsProbeAttempt(id, true, reason, info);
    }

    /// <summary>
    /// Creates an attempt that rejected the memory image.
    /// </summary>
    /// <param name="id">The probe that was tried.</param>
    /// <param name="reason">Why the bytes are not this operating system. Not empty.</param>
    /// <returns>A rejected attempt with no <see cref="Info"/>.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty.</exception>
    public static GuestOsProbeAttempt Reject(GuestOsId id, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new GuestOsProbeAttempt(id, false, reason, null);
    }
}