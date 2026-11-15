// <copyright file="VideoLeaseSafetyTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Unit.Components;

using Serilog;

/// <summary>
/// Lease-safety tests for the slot, ticket, and holder-table mechanics (§14.31).
/// </summary>
[TestFixture]
public class VideoLeaseSafetyTests
{
    private const int PixelsPerBuffer = 4;

    /// <summary>
    /// Two holders share one buffer. Disposing one lease twice, and disposing a copy, leaves the
    /// other lease's pixels intact. The buffer returns to the pool only once that other lease
    /// is disposed and the buffer is no longer the latest cache entry.
    /// </summary>
    [Test]
    public void TwoHolders_NoisyDispose_KeepsTheOtherValidUntilItDisposes()
    {
        uint[] firstPixels = [1, 2, 3, 4];
        uint[] secondPixels = [5, 6, 7, 8];
        VideoLeasePool pool = CreatePool(bufferCount: 2);

        Assert.That(pool.TryAcquireFrame(1, 100, 2, 2, 2, firstPixels, out VideoFrame first), Is.True);
        Assert.That(pool.TryAcquireFrame(1, 100, 2, 2, 2, firstPixels, out VideoFrame second), Is.True);

        // A newer sequence takes the cache. The shared buffer stays allocated because it still has holders.
        Assert.That(pool.TryAcquireFrame(2, 200, 2, 2, 2, secondPixels, out VideoFrame newer), Is.True);

        VideoFrame copy = first;
        first.Dispose();
        first.Dispose();
        copy.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(second.Pixels.ToArray(), Is.EqualTo(firstPixels));
            Assert.That(second.IsBufferFree, Is.False);
            Assert.That(first.IsBufferFree, Is.False);
            Assert.That(pool.Stats.PoolExhausted, Is.EqualTo(0));
            Assert.That(pool.Stats.LeasesOutstanding, Is.EqualTo(2));
        });

        second.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsBufferFree, Is.True);
            Assert.That(first.IsBufferFree, Is.True);
            Assert.That(() => _ = second.Pixels, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(pool.Stats.LeasesOutstanding, Is.EqualTo(1));
        });

        newer.Dispose();
        Assert.That(pool.Stats.LeasesOutstanding, Is.EqualTo(0));
    }

    /// <summary>
    /// The last holder of the current cache entry does not free the buffer. It stays published
    /// until a newer sequence replaces it.
    /// </summary>
    [Test]
    public void LastDispose_OfTheCachedBuffer_DoesNotFreeIt()
    {
        uint[] pixels = [9, 8, 7, 6];
        VideoLeasePool pool = CreatePool(bufferCount: 1);
        Assert.That(pool.TryAcquireFrame(1, 10, 2, 2, 2, pixels, out VideoFrame frame), Is.True);
        ReadOnlyMemory<uint> kept = frame.Pixels;

        frame.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(frame.IsBufferFree, Is.False);
            Assert.That(kept.Span.ToArray(), Is.EqualTo(pixels));
            Assert.That(() => _ = frame.Pixels, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(pool.Stats.LatestSequence, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A default frame lease and a released frame lease do not throw from <see cref="VideoFrame.Dispose"/>,
    /// and reading pixels from either throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public void DefaultAndDisposedFrame_DisposeIsHarmless_AndPixelsThrows()
    {
        Assert.That(() => default(VideoFrame).Dispose(), Throws.Nothing);
        Assert.That(() => _ = default(VideoFrame).Pixels, Throws.TypeOf<ObjectDisposedException>());

        VideoLeasePool pool = CreatePool(bufferCount: 1);
        uint[] pixels = [1, 1, 1, 1];
        Assert.That(pool.TryAcquireFrame(1, 1, 2, 2, 2, pixels, out VideoFrame frame), Is.True);
        frame.Dispose();
        frame.Dispose();

        Assert.That(() => _ = frame.Pixels, Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>
    /// A default signal lease and a released signal lease do not throw from dispose, and reading
    /// <see cref="SignalLease{TSignal}.Signal"/> throws <see cref="ObjectDisposedException"/>.
    /// Disposing one of two holders leaves the other signal readable.
    /// </summary>
    [Test]
    public void DefaultAndDisposedSignal_DisposeIsHarmless_AndSignalThrows()
    {
        Assert.That(() => default(SignalLease<ProbeSignal>).Dispose(), Throws.Nothing);
        Assert.That(() => _ = default(SignalLease<ProbeSignal>).Signal, Throws.TypeOf<ObjectDisposedException>());

        VideoLeasePool pool = CreatePool(bufferCount: 2);
        var firstSignal = new ProbeSignal(1, 30);
        var laterSignal = new ProbeSignal(2, 40);
        Assert.That(pool.TryAcquireSignal(firstSignal, out SignalLease<ProbeSignal> first), Is.True);
        Assert.That(pool.TryAcquireSignal(firstSignal, out SignalLease<ProbeSignal> second), Is.True);
        Assert.That(pool.TryAcquireSignal(laterSignal, out SignalLease<ProbeSignal> later), Is.True);

        SignalLease<ProbeSignal> copy = first;
        first.Dispose();
        first.Dispose();
        copy.Dispose();

        Assert.That(second.Signal, Is.SameAs(firstSignal));

        second.Dispose();
        Assert.That(() => _ = second.Signal, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(later.Signal, Is.SameAs(laterSignal));
        later.Dispose();
    }

    /// <summary>
    /// A 17th holder on one buffer fails the acquire and increments <see cref="VideoPipelineStats.PoolExhausted"/>.
    /// </summary>
    [Test]
    public void SeventeenthHolder_ReturnsFalse_AndIncrementsPoolExhausted()
    {
        VideoLeasePool pool = CreatePool(bufferCount: 1);
        uint[] pixels = [4, 4, 4, 4];
        var holders = new VideoFrame[FrameSlot.MaxHolders];

        for (int i = 0; i < holders.Length; i++)
        {
            Assert.That(
                pool.TryAcquireFrame(1, 1, 2, 2, 2, pixels, out holders[i]),
                Is.True,
                $"Holder {i + 1} should be accepted.");
        }

        Assert.That(pool.TryAcquireFrame(1, 1, 2, 2, 2, pixels, out VideoFrame rejected), Is.False);
        Assert.That(pool.Stats.PoolExhausted, Is.EqualTo(1));
        Assert.That(holders[0].Pixels.ToArray(), Is.EqualTo(pixels));

        for (int i = 0; i < holders.Length; i++)
        {
            holders[i].Dispose();
        }
    }

    /// <summary>
    /// Acquiring a new sequence when all 8 buffers are held fails and increments
    /// <see cref="VideoPipelineStats.PoolExhausted"/>.
    /// </summary>
    [Test]
    public void AllEightBuffersHeld_AcquireReturnsFalse_AndIncrementsPoolExhausted()
    {
        const int BufferCount = 8;
        VideoLeasePool pool = CreatePool(BufferCount);
        uint[] pixels = [3, 3, 3, 3];
        var holders = new VideoFrame[BufferCount];

        for (int i = 0; i < holders.Length; i++)
        {
            ulong sequence = (ulong)(i + 1);
            Assert.That(
                pool.TryAcquireFrame(sequence, sequence, 2, 2, 2, pixels, out holders[i]),
                Is.True,
                $"Buffer {i + 1} should be acquired.");
        }

        Assert.That(pool.TryAcquireFrame(BufferCount + 1UL, 0, 2, 2, 2, pixels, out VideoFrame rejected), Is.False);
        Assert.That(pool.Stats.PoolExhausted, Is.EqualTo(1));
        Assert.That(pool.Stats.LatestSequence, Is.EqualTo(BufferCount));

        for (int i = 0; i < holders.Length; i++)
        {
            holders[i].Dispose();
        }
    }

#if DEBUG
    /// <summary>
    /// In Debug, a pixel buffer kept past dispose reads <c>0xDEADBEEF</c> once the buffer is freed.
    /// </summary>
    [Test]
    public void FreedBuffer_IsPoisonedWithDeadBeef()
    {
        uint[] pixels = [9, 8, 7, 6];
        VideoLeasePool pool = CreatePool(bufferCount: 2);
        Assert.That(pool.TryAcquireFrame(1, 1, 2, 2, 2, pixels, out VideoFrame held), Is.True);
        ReadOnlyMemory<uint> kept = held.Pixels;

        Assert.That(pool.TryAcquireFrame(2, 2, 2, 2, 2, pixels, out VideoFrame newer), Is.True);
        Assert.That(kept.Span.ToArray(), Is.EqualTo(pixels), "The buffer is still held, so it is not poisoned yet.");

        held.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(held.IsBufferFree, Is.True);
            Assert.That(kept.Span.ToArray(), Is.All.EqualTo(0xDEADBEEFu));
        });

        newer.Dispose();
    }
#endif

    private static VideoLeasePool CreatePool(int bufferCount)
    {
        return new VideoLeasePool(bufferCount, PixelsPerBuffer, Generator.Log().Object);
    }

    private sealed class ProbeSignal : IVideoSignal
    {
        public ProbeSignal(ulong sequence, ulong cycle)
        {
            Sequence = sequence;
            Cycle = cycle;
        }

        public ulong Sequence { get; }

        public ulong Cycle { get; }
    }
}