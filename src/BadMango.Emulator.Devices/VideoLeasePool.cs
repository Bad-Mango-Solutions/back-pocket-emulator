// <copyright file="VideoLeasePool.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using Interfaces;

using Serilog;

/// <summary>
/// Fixed pool of frame and signal slots for the lease tests.
/// </summary>
/// <remarks>
/// <para>
/// The real hub pools arrive later and grow from 3 buffers to 8. This pool is a fixed
/// array of the length it was constructed with, which is what the lease tests drive.
/// Buffers are exact-length <see cref="uint"/> arrays owned here, not
/// <c>ArrayPool&lt;T&gt;.Shared</c> and not <c>IMemoryOwner&lt;T&gt;</c>.
/// </para>
/// <para>
/// Tickets come from one counter advanced with <c>Interlocked.Increment</c>.
/// A buffer is freed only when its holder table is empty and it is no longer the cache
/// entry for the latest sequence. A full holder table, or no free buffer, makes the
/// acquire return <see langword="false"/>, increments <see cref="VideoPipelineStats.PoolExhausted"/>,
/// and logs the exhaustion. The first report is logged immediately. Repeats are summarized
/// at most once every 5 seconds.
/// </para>
/// </remarks>
internal sealed class VideoLeasePool
{
    private const long ExhaustedLogIntervalMilliseconds = 5000;
    private readonly Lock gate = new();
    private readonly ILogger logger;
    private readonly int pixelsPerBuffer;
    private readonly Func<long> clock;
    private readonly FrameSlot[] frameSlots;
    private readonly FrameSlot[] signalSlots;
    private long nextTicket;
    private int cachedFrameIndex = -1;
    private int cachedSignalIndex = -1;
    private ulong latestSequence;
    private long poolExhausted;
    private long nextExhaustedSummaryAt = long.MinValue;
    private long suppressedSinceSummary;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoLeasePool"/> class.
    /// </summary>
    /// <param name="bufferCount">How many frame buffers, and how many signal buffers, to allocate.</param>
    /// <param name="pixelsPerBuffer">The exact pixel length of each frame buffer.</param>
    /// <param name="logger">The logger that receives pool-exhausted warnings.</param>
    /// <param name="clock">
    /// Millisecond clock used to rate-limit exhaustion logs. The default is <see cref="Environment.TickCount64"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="bufferCount"/> or <paramref name="pixelsPerBuffer"/> is less than 1.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
    internal VideoLeasePool(int bufferCount, int pixelsPerBuffer, ILogger logger, Func<long>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerBuffer, 1);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger.ForContext<VideoLeasePool>();
        this.clock = clock ?? (static () => Environment.TickCount64);
        this.pixelsPerBuffer = pixelsPerBuffer;
        frameSlots = new FrameSlot[bufferCount];
        signalSlots = new FrameSlot[bufferCount];
        for (int i = 0; i < bufferCount; i++)
        {
            frameSlots[i] = new FrameSlot(pixelsPerBuffer);
            signalSlots[i] = new FrameSlot(0);
        }
    }

    /// <summary>
    /// Gets the counters this pool owns. The other pipeline counters stay at zero.
    /// </summary>
    /// <value>A snapshot of outstanding leases, exhausted acquires, and the latest frame sequence.</value>
    internal VideoPipelineStats Stats
    {
        get
        {
            lock (gate)
            {
                return new VideoPipelineStats(
                    FramesCaptured: 0,
                    SwitchEvents: 0,
                    SwitchLogOverflows: 0,
                    Scans: 0,
                    SignalsGenerated: 0,
                    FramesProvided: 0,
                    CacheHits: 0,
                    CaptureErrors: 0,
                    ScanErrors: 0,
                    SignalErrors: 0,
                    ProvideErrors: 0,
                    SubscriberErrors: 0,
                    LeasesOutstanding: CountHolders(),
                    PoolExhausted: Volatile.Read(ref poolExhausted),
                    RecorderDroppedFrames: 0,
                    LatestSequence: latestSequence);
            }
        }
    }

    /// <summary>
    /// Leases the cached frame for <paramref name="sequence"/>, publishing it first when it is new.
    /// </summary>
    /// <param name="sequence">The frame sequence. A newer sequence replaces the cache.</param>
    /// <param name="cycle">The capture cycle stored on the frame.</param>
    /// <param name="width">The frame width in pixels.</param>
    /// <param name="height">The frame height in pixels.</param>
    /// <param name="stride">The row stride in pixels.</param>
    /// <param name="pixels">The pixels copied in when this sequence is published. Ignored when the sequence is already cached.</param>
    /// <param name="frame">The lease when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the cached buffer already has 16 holders, when every buffer is held,
    /// or when <paramref name="sequence"/> is older than the cache.
    /// </returns>
    internal bool TryAcquireFrame(
        ulong sequence,
        ulong cycle,
        int width,
        int height,
        int stride,
        ReadOnlySpan<uint> pixels,
        out VideoFrame frame)
    {
        lock (gate)
        {
            if (cachedFrameIndex >= 0 && frameSlots[cachedFrameIndex].Info.Sequence == sequence)
            {
                return TryLeaseFrame(frameSlots[cachedFrameIndex], out frame);
            }

            if (cachedFrameIndex >= 0 && sequence < frameSlots[cachedFrameIndex].Info.Sequence)
            {
                frame = default;
                return false;
            }

            if (pixels.Length != pixelsPerBuffer)
            {
                throw new ArgumentException(
                    $"Pixel buffer length {pixels.Length} does not match the pool length {pixelsPerBuffer}.",
                    nameof(pixels));
            }

            int index = FindFreeIndex(frameSlots);
            if (index < 0 && cachedFrameIndex >= 0 && frameSlots[cachedFrameIndex].HolderCount == 0)
            {
                frameSlots[cachedFrameIndex].DemoteFromCache();
                index = cachedFrameIndex;
            }

            if (index < 0)
            {
                NoteExhausted();
                frame = default;
                return false;
            }

            if (cachedFrameIndex >= 0 && cachedFrameIndex != index)
            {
                frameSlots[cachedFrameIndex].DemoteFromCache();
            }

            var info = new VideoFrameInfo(sequence, cycle, width, height, stride);
            if (!frameSlots[index].TryPublishFrame(info, pixels))
            {
                NoteExhausted();
                frame = default;
                return false;
            }

            cachedFrameIndex = index;
            latestSequence = sequence;
            return TryLeaseFrame(frameSlots[index], out frame);
        }
    }

    /// <summary>
    /// Leases the cached signal for <paramref name="signal"/>'s sequence, publishing it first when it is new.
    /// </summary>
    /// <typeparam name="TSignal">The signal type.</typeparam>
    /// <param name="signal">The signal to publish, or a signal whose sequence is already cached.</param>
    /// <param name="lease">The lease when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the cached buffer already has 16 holders, when every signal buffer is held,
    /// or when the sequence is older than the cache.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal"/> is <see langword="null"/>.</exception>
    internal bool TryAcquireSignal<TSignal>(TSignal signal, out SignalLease<TSignal> lease)
        where TSignal : class, IVideoSignal
    {
        ArgumentNullException.ThrowIfNull(signal);

        lock (gate)
        {
            if (cachedSignalIndex >= 0
                && signalSlots[cachedSignalIndex].Info.Sequence == signal.Sequence)
            {
                return TryLeaseSignal(signalSlots[cachedSignalIndex], out lease);
            }

            if (cachedSignalIndex >= 0 && signal.Sequence < signalSlots[cachedSignalIndex].Info.Sequence)
            {
                lease = default;
                return false;
            }

            int index = FindFreeIndex(signalSlots);
            if (index < 0 && cachedSignalIndex >= 0 && signalSlots[cachedSignalIndex].HolderCount == 0)
            {
                signalSlots[cachedSignalIndex].DemoteFromCache();
                index = cachedSignalIndex;
            }

            if (index < 0)
            {
                NoteExhausted();
                lease = default;
                return false;
            }

            if (cachedSignalIndex >= 0 && cachedSignalIndex != index)
            {
                signalSlots[cachedSignalIndex].DemoteFromCache();
            }

            var info = new VideoFrameInfo(signal.Sequence, signal.Cycle, 0, 0, 0);
            if (!signalSlots[index].TryPublishSignal(signal, info))
            {
                NoteExhausted();
                lease = default;
                return false;
            }

            cachedSignalIndex = index;
            return TryLeaseSignal(signalSlots[index], out lease);
        }
    }

    private bool TryLeaseFrame(FrameSlot slot, out VideoFrame frame)
    {
        long ticket = NextTicket();
        if (!slot.TryHold(ticket))
        {
            NoteExhausted();
            frame = default;
            return false;
        }

        frame = new VideoFrame(slot, ticket, slot.Info);
        return true;
    }

    private bool TryLeaseSignal<TSignal>(FrameSlot slot, out SignalLease<TSignal> lease)
        where TSignal : class, IVideoSignal
    {
        long ticket = NextTicket();
        if (!slot.TryHold(ticket))
        {
            NoteExhausted();
            lease = default;
            return false;
        }

        lease = new SignalLease<TSignal>(slot, ticket);
        return true;
    }

    private long NextTicket()
    {
        long ticket = Interlocked.Increment(ref nextTicket);
        if (ticket == 0)
        {
            ticket = Interlocked.Increment(ref nextTicket);
        }

        return ticket;
    }

    private int FindFreeIndex(FrameSlot[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsFree)
            {
                return i;
            }
        }

        return -1;
    }

    private long CountHolders()
    {
        long count = 0;
        for (int i = 0; i < frameSlots.Length; i++)
        {
            count += frameSlots[i].HolderCount;
            count += signalSlots[i].HolderCount;
        }

        return count;
    }

    private void NoteExhausted()
    {
        // Caller holds gate. Lock is not re-entrant, so this must not take it again.
        long total = Interlocked.Increment(ref poolExhausted);
        long now = clock();
        if (nextExhaustedSummaryAt == long.MinValue)
        {
            nextExhaustedSummaryAt = now + ExhaustedLogIntervalMilliseconds;
            logger.Warning("Video lease pool exhausted ({PoolExhausted} total).", total);
            return;
        }

        suppressedSinceSummary++;
        if (now < nextExhaustedSummaryAt)
        {
            return;
        }

        logger.Warning(
            "Video lease pool exhausted {SuppressedCount} more times since the last report ({PoolExhausted} total).",
            suppressedSinceSummary,
            total);
        suppressedSinceSummary = 0;
        nextExhaustedSummaryAt = now + ExhaustedLogIntervalMilliseconds;
    }
}