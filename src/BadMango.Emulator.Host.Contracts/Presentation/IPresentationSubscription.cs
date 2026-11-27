// <copyright file="IPresentationSubscription.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One window's view of a machine's presentation stream.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryReadLatest"/> is latest-wins and does not remove the sample. Two windows share
/// that sample and keep their own <see cref="Monitor"/>. Audio is fanned out per subscription.
/// A subscription that falls behind drops the oldest blocks. Reading never blocks.
/// </para>
/// <para>
/// The loopback returns the published object. It does not copy pixels or PCM. A later transport
/// copies at the IPC or network boundary. Issue #738 owns that encoding.
/// </para>
/// </remarks>
public interface IPresentationSubscription : IDisposable
{
    /// <summary>
    /// Gets the machine.
    /// </summary>
    MachineId MachineId { get; }

    /// <summary>
    /// Gets the window this subscription belongs to.
    /// </summary>
    string WindowId { get; }

    /// <summary>
    /// Gets the monitor choice for this window.
    /// </summary>
    HostMonitorChoice Monitor { get; }

    /// <summary>
    /// Gets the PCM format for this machine.
    /// </summary>
    HostAudioFormat AudioFormat { get; }

    /// <summary>
    /// Gets the frame timing.
    /// </summary>
    HostPresentationTiming Timing { get; }

    /// <summary>
    /// Sets the monitor choice for this window.
    /// </summary>
    /// <param name="monitor">The choice.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the choice does not match the output. RGB accepts only <see cref="HostMonitorChoice.Rgb"/>.
    /// Composite rejects <see cref="HostMonitorChoice.Rgb"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown when the subscription or the session is disposed.</exception>
    void SetMonitor(HostMonitorChoice monitor);

    /// <summary>
    /// Reads the latest sample without removing it.
    /// </summary>
    /// <param name="sample">The latest sample, or <see langword="null"/> when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when a sample has been published.</returns>
    bool TryReadLatest(out HostPresentationSample? sample);

    /// <summary>
    /// Reads the next audio block for this window.
    /// </summary>
    /// <param name="block">The block, or <see langword="null"/> when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when a block was waiting.</returns>
    bool TryReadAudio(out HostAudioBlock? block);
}