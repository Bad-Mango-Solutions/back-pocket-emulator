// <copyright file="LoopbackSubscription.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// One window subscribed to a loopback machine. Audio is queued here. The frame is the machine's latest sample.
/// </summary>
internal sealed class LoopbackSubscription : IPresentationSubscription
{
    private readonly LoopbackHostSession session;
    private readonly Queue<HostAudioBlock> audio = new();
    private HostMonitorChoice monitorChoice;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackSubscription"/> class.
    /// </summary>
    /// <param name="session">The owning session.</param>
    /// <param name="machine">The machine.</param>
    /// <param name="windowId">The window id.</param>
    /// <param name="monitor">The initial monitor choice.</param>
    internal LoopbackSubscription(LoopbackHostSession session, LoopbackMachine machine, string windowId, HostMonitorChoice monitor)
    {
        this.session = session;
        Machine = machine;
        WindowId = windowId;
        monitorChoice = monitor;
    }

    /// <inheritdoc />
    public MachineId MachineId => Machine.Id;

    /// <inheritdoc />
    public string WindowId { get; }

    /// <inheritdoc />
    public HostMonitorChoice Monitor => monitorChoice;

    /// <inheritdoc />
    public HostAudioFormat AudioFormat => session.ReadAudioFormat(this);

    /// <inheritdoc />
    public HostPresentationTiming Timing => session.ReadTiming(this);

    /// <summary>
    /// Gets the machine this window is subscribed to.
    /// </summary>
    internal LoopbackMachine Machine { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Dispose"/> has been called.
    /// </summary>
    internal bool IsDisposed => disposed;

    /// <inheritdoc />
    public void SetMonitor(HostMonitorChoice monitor)
    {
        session.ChangeMonitor(this, monitor);
    }

    /// <inheritdoc />
    public bool TryReadLatest(out HostPresentationSample? sample)
    {
        return session.ReadLatest(this, out sample);
    }

    /// <inheritdoc />
    public bool TryReadAudio(out HostAudioBlock? block)
    {
        return session.ReadAudio(this, out block);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.RemoveSubscription(this);
    }

    /// <summary>
    /// Marks this window disposed. The caller holds <see cref="Machine"/>'s gate.
    /// </summary>
    internal void MarkDisposed()
    {
        disposed = true;
    }

    /// <summary>
    /// Queues a block and drops the oldest when the queue is past the cap.
    /// </summary>
    /// <param name="block">The block. The caller holds the machine gate.</param>
    internal void Enqueue(HostAudioBlock block)
    {
        audio.Enqueue(block);
        while (audio.Count > LoopbackMachine.MaxAudioQueued)
        {
            audio.Dequeue();
        }
    }

    /// <summary>
    /// Takes the next block.
    /// </summary>
    /// <param name="block">The block, or <see langword="null"/> when the queue is empty.</param>
    /// <returns><see langword="true"/> when a block was waiting.</returns>
    internal bool TryDequeue(out HostAudioBlock? block)
    {
        if (audio.Count == 0)
        {
            block = null;
            return false;
        }

        block = audio.Dequeue();
        return true;
    }

    /// <summary>
    /// Drops queued audio after a format change.
    /// </summary>
    internal void ClearAudio()
    {
        audio.Clear();
    }

    /// <summary>
    /// Stores the monitor choice. The caller holds the machine gate.
    /// </summary>
    /// <param name="choice">The choice.</param>
    internal void ApplyMonitor(HostMonitorChoice choice)
    {
        monitorChoice = choice;
    }
}