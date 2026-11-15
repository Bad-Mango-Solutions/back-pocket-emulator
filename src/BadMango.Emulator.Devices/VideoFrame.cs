// <copyright file="VideoFrame.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// A lease on one cached BGRA8888 frame. Acquiring one allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// A readonly struct so a copy shares the slot and the ticket. Disposing the copy,
/// disposing twice, or disposing <c>default</c> releases that ticket at most once.
/// </para>
/// <para>
/// <see cref="Pixels"/> throws <see cref="ObjectDisposedException"/> once this ticket
/// is released or when the value is <c>default</c>. A <see cref="ReadOnlyMemory{T}"/>
/// already copied out stays readable only until the buffer itself is freed.
/// </para>
/// </remarks>
public readonly struct VideoFrame : IDisposable
{
    private readonly FrameSlot? slot;
    private readonly long ticket;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoFrame"/> struct.
    /// </summary>
    /// <param name="slot">The pooled slot.</param>
    /// <param name="ticket">The unique acquire ticket.</param>
    /// <param name="info">The sequence, cycle, and geometry.</param>
    internal VideoFrame(FrameSlot slot, long ticket, VideoFrameInfo info)
    {
        ArgumentNullException.ThrowIfNull(slot);
        this.slot = slot;
        this.ticket = ticket;
        Info = info;
    }

    /// <summary>
    /// Gets the sequence, cycle, and geometry. This stays readable after dispose.
    /// </summary>
    /// <value>The frame identity captured when the lease was taken.</value>
    public VideoFrameInfo Info { get; }

    /// <summary>
    /// Gets the BGRA8888 pixels.
    /// </summary>
    /// <value>The shared pixel buffer.</value>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when this lease was released or is <c>default</c>.
    /// </exception>
    public ReadOnlyMemory<uint> Pixels =>
        slot is not null && slot.TryReadPixels(ticket, out ReadOnlyMemory<uint> pixels)
            ? pixels
            : throw new ObjectDisposedException(nameof(VideoFrame));

    /// <summary>
    /// Gets a value indicating whether the underlying buffer has returned to the pool.
    /// </summary>
    /// <value>
    /// <see langword="true"/> after the holder table is empty and the slot is no longer
    /// the latest cache entry.
    /// </value>
    internal bool IsBufferFree => slot is { IsFree: true };

    /// <summary>
    /// Releases this lease's ticket. A second call, a call on a copy, or a call on
    /// <c>default</c> does nothing.
    /// </summary>
    public void Dispose() => slot?.Release(ticket);
}