// <copyright file="SignalLease.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using Interfaces;

/// <summary>
/// A lease on one cached signal. The ticket and holder table match <see cref="VideoFrame"/>.
/// </summary>
/// <typeparam name="TSignal">The signal marker. A reference type implementing <see cref="IVideoSignal"/>.</typeparam>
/// <remarks>
/// Disposing twice, disposing a copy, or disposing <c>default</c> is harmless.
/// <see cref="Signal"/> throws <see cref="ObjectDisposedException"/> once this ticket
/// is released or when the value is <c>default</c>.
/// </remarks>
public readonly struct SignalLease<TSignal> : IDisposable
    where TSignal : class, IVideoSignal
{
    private readonly FrameSlot? slot;
    private readonly long ticket;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalLease{TSignal}"/> struct.
    /// </summary>
    /// <param name="slot">The pooled slot.</param>
    /// <param name="ticket">The unique acquire ticket.</param>
    internal SignalLease(FrameSlot slot, long ticket)
    {
        ArgumentNullException.ThrowIfNull(slot);
        this.slot = slot;
        this.ticket = ticket;
    }

    /// <summary>
    /// Gets the leased signal.
    /// </summary>
    /// <value>The signal object retained by the slot.</value>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this lease was released or is <c>default</c>.
    /// </exception>
    public TSignal Signal =>
        slot is not null && slot.TryReadSignal(ticket, out TSignal? signal) && signal is not null
            ? signal
            : throw new ObjectDisposedException(nameof(SignalLease<TSignal>));

    /// <summary>
    /// Releases this lease's ticket. A second call, a call on a copy, or a call on
    /// <c>default</c> does nothing.
    /// </summary>
    public void Dispose() => slot?.Release(ticket);
}