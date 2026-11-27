// <copyright file="IHostPresentation.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The presentation plane. A client subscribes to frames or a raw composite signal, and to PCM.
/// </summary>
/// <remarks>
/// The client does not reference <c>VideoDevice</c> or call <c>IAudioOutput.Play</c>.
/// Publish happens at the machine edge (a frame tap or an audio-block tap), outside the CPU loop.
/// </remarks>
public interface IHostPresentation
{
    /// <summary>
    /// Subscribes one window to a machine.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="windowId">The window. Unique per machine among live subscriptions.</param>
    /// <param name="monitor">
    /// The initial choice. <see langword="null"/> uses NTSC for composite and RGB for an RGB output.
    /// </param>
    /// <returns>The subscription.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="windowId"/> is empty or already subscribed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown or the monitor does not match the output.</exception>
    IPresentationSubscription Subscribe(MachineId machineId, string windowId, HostMonitorChoice? monitor = null);

    /// <summary>
    /// Gets the text-screen snapshot.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown.</exception>
    HostScreenText GetScreenText(MachineId machineId);

    /// <summary>
    /// Gets the video soft-switch snapshot.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown.</exception>
    HostVideoState GetVideoState(MachineId machineId);
}