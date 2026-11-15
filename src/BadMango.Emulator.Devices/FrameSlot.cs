// <copyright file="FrameSlot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// One pooled buffer and its holder table. Shared by <see cref="VideoFrame"/> and
/// <see cref="SignalLease{TSignal}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A sealed class: it owns the buffer. Tickets are recorded in a fixed
/// <see cref="long"/> table of <see cref="MaxHolders"/> entries. A free entry is 0.
/// The table is updated under the slot lock. A release clears an entry only when it
/// still contains that ticket, so disposing twice, disposing a copy, or disposing
/// <c>default</c> cannot release someone else's hold.
/// </para>
/// <para>
/// The buffer returns to the pool only when the table is empty and the slot is no longer
/// the cache entry for the latest sequence. Debug builds then fill the pixel buffer with
/// <c>0xDEADBEEF</c>.
/// </para>
/// </remarks>
internal sealed class FrameSlot
{
    /// <summary>
    /// Holder-table capacity. A 17th holder fails the acquire.
    /// </summary>
    internal const int MaxHolders = 16;

    private readonly Lock gate = new();
    private readonly long[] holders = new long[MaxHolders];
    private readonly uint[] pixels;
    private object? signal;
    private VideoFrameInfo info;
    private bool inUse;
    private bool cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSlot"/> class.
    /// </summary>
    /// <param name="pixelLength">
    /// The exact pixel-buffer length. Zero means this slot holds a signal and does not allocate a pixel buffer.
    /// </param>
    internal FrameSlot(int pixelLength)
    {
        pixels = pixelLength == 0 ? Array.Empty<uint>() : new uint[pixelLength];
    }

    /// <summary>
    /// Gets a value indicating whether this slot is back in the pool.
    /// </summary>
    /// <value><see langword="true"/> when the slot is not published.</value>
    internal bool IsFree
    {
        get
        {
            lock (gate)
            {
                return !inUse;
            }
        }
    }

    /// <summary>
    /// Gets how many tickets currently hold this slot.
    /// </summary>
    /// <value>The number of non-zero holder-table entries.</value>
    internal int HolderCount
    {
        get
        {
            lock (gate)
            {
                return CountHolders();
            }
        }
    }

    /// <summary>
    /// Gets the frame identity stored at publish time.
    /// </summary>
    /// <value>The sequence, cycle, and geometry.</value>
    internal VideoFrameInfo Info
    {
        get
        {
            lock (gate)
            {
                return info;
            }
        }
    }

    /// <summary>
    /// Records <paramref name="ticket"/> in the first free holder entry.
    /// </summary>
    /// <param name="ticket">The acquire ticket. Zero is never a live ticket.</param>
    /// <returns><see langword="false"/> when the slot is free or the table is full.</returns>
    internal bool TryHold(long ticket)
    {
        if (ticket == 0)
        {
            return false;
        }

        lock (gate)
        {
            if (!inUse)
            {
                return false;
            }

            for (int i = 0; i < holders.Length; i++)
            {
                if (holders[i] == 0)
                {
                    holders[i] = ticket;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Drops <paramref name="ticket"/> if it is present. A missing ticket does nothing.
    /// </summary>
    /// <param name="ticket">The ticket to release.</param>
    internal void Release(long ticket)
    {
        if (ticket == 0)
        {
            return;
        }

        lock (gate)
        {
            if (!TryClearTicket(ticket))
            {
                return;
            }

            if (CountHolders() == 0 && !cached)
            {
                Retire();
            }
        }
    }

    /// <summary>
    /// Reads the pixel buffer when <paramref name="ticket"/> still holds the slot.
    /// </summary>
    /// <param name="ticket">The caller's ticket.</param>
    /// <param name="memory">The buffer when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the ticket is still in the holder table.</returns>
    internal bool TryReadPixels(long ticket, out ReadOnlyMemory<uint> memory)
    {
        lock (gate)
        {
            if (!ContainsTicket(ticket))
            {
                memory = default;
                return false;
            }

            memory = pixels;
            return true;
        }
    }

    /// <summary>
    /// Reads the published signal when <paramref name="ticket"/> still holds the slot.
    /// </summary>
    /// <typeparam name="TSignal">The signal type the caller expects.</typeparam>
    /// <param name="ticket">The caller's ticket.</param>
    /// <param name="published">The signal when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the ticket is held and the stored signal has the requested type.</returns>
    internal bool TryReadSignal<TSignal>(long ticket, out TSignal? published)
        where TSignal : class
    {
        lock (gate)
        {
            if (!ContainsTicket(ticket) || signal is not TSignal typed)
            {
                published = null;
                return false;
            }

            published = typed;
            return true;
        }
    }

    /// <summary>
    /// Publishes pixel data into a free slot and makes it the cache entry.
    /// </summary>
    /// <param name="frameInfo">The sequence, cycle, and geometry.</param>
    /// <param name="source">The pixels to copy. The length must match the slot.</param>
    /// <returns><see langword="false"/> when the slot is already published.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> has the wrong length.</exception>
    internal bool TryPublishFrame(VideoFrameInfo frameInfo, ReadOnlySpan<uint> source)
    {
        lock (gate)
        {
            if (inUse)
            {
                return false;
            }

            if (source.Length != pixels.Length)
            {
                throw new ArgumentException(
                    $"Pixel buffer length {source.Length} does not match the slot length {pixels.Length}.",
                    nameof(source));
            }

            source.CopyTo(pixels);
            signal = null;
            info = frameInfo;
            inUse = true;
            cached = true;
            return true;
        }
    }

    /// <summary>
    /// Publishes a signal into a free slot and makes it the cache entry.
    /// </summary>
    /// <param name="published">The signal object to retain.</param>
    /// <param name="frameInfo">The sequence and cycle. Geometry is unused for a signal.</param>
    /// <returns><see langword="false"/> when the slot is already published.</returns>
    internal bool TryPublishSignal(object published, VideoFrameInfo frameInfo)
    {
        ArgumentNullException.ThrowIfNull(published);

        lock (gate)
        {
            if (inUse)
            {
                return false;
            }

            signal = published;
            info = frameInfo;
            inUse = true;
            cached = true;
            return true;
        }
    }

    /// <summary>
    /// Drops the cache flag. Retires the slot when nobody still holds it.
    /// </summary>
    internal void DemoteFromCache()
    {
        lock (gate)
        {
            if (!inUse)
            {
                cached = false;
                return;
            }

            cached = false;
            if (CountHolders() == 0)
            {
                Retire();
            }
        }
    }

    private bool TryClearTicket(long ticket)
    {
        for (int i = 0; i < holders.Length; i++)
        {
            if (holders[i] == ticket)
            {
                holders[i] = 0;
                return true;
            }
        }

        return false;
    }

    private bool ContainsTicket(long ticket)
    {
        if (ticket == 0)
        {
            return false;
        }

        for (int i = 0; i < holders.Length; i++)
        {
            if (holders[i] == ticket)
            {
                return true;
            }
        }

        return false;
    }

    private int CountHolders()
    {
        int count = 0;
        for (int i = 0; i < holders.Length; i++)
        {
            if (holders[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private void Retire()
    {
        cached = false;
        signal = null;
#if DEBUG
        Array.Fill(pixels, 0xDEADBEEFu);
#endif
        inUse = false;
    }
}