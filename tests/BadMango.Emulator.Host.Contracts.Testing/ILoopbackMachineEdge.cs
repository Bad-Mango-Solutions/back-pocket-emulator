// <copyright file="ILoopbackMachineEdge.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// The machine side of the loopback. A later binding calls these from outside the CPU loop:
/// a frame tap, an audio-block tap, or one posted input drain per host event.
/// </summary>
/// <remarks>
/// <para>
/// These methods are not the client planes. They do not run per cycle and they do not read
/// the bus. Input that a client already submitted is queued; <see cref="DrainInput"/> takes
/// that queue. Frame and audio publish replace or enqueue a reference. They do not copy.
/// </para>
/// <para>
/// Nothing here calls <c>IAudioOutput.Play</c> or a video device.
/// </para>
/// </remarks>
public interface ILoopbackMachineEdge
{
    /// <summary>
    /// Publishes the latest presentation sample. The previous sample is replaced.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="sample">The sample. The caller must not mutate its buffers afterward.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sample"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the sample kind does not match the machine, or a required buffer is missing.</exception>
    void PublishFrame(MachineId machineId, HostPresentationSample sample);

    /// <summary>
    /// Publishes one PCM block to every current subscriber. A subscriber that already holds 8 blocks drops the oldest.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="block">The block.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="block"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the block length does not match the machine's audio format.</exception>
    void PublishAudio(MachineId machineId, HostAudioBlock block);

    /// <summary>
    /// Replaces the line-out format and drops queued audio so blocks cannot mix.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="format">The format.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="format"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the format is not 8- or 16-bit, mono or stereo.</exception>
    void SetAudioFormat(MachineId machineId, HostAudioFormat format);

    /// <summary>
    /// Records a measured speed ratio. This is not the throttle.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="ratio">The ratio of nominal speed. 1.0 is 100 percent. Negative and non-finite values are rejected.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ratio"/> is negative or not finite.</exception>
    void ReportMeasuredSpeed(MachineId machineId, double ratio);

    /// <summary>
    /// Replaces the text-screen snapshot.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="screen">The snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is <see langword="null"/>.</exception>
    void SetScreenText(MachineId machineId, HostScreenText screen);

    /// <summary>
    /// Replaces the soft-switch snapshot. <see cref="HostVideoState.Output"/> must match the machine.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="state">The snapshot.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the output kind does not match the machine.</exception>
    void SetVideoState(MachineId machineId, HostVideoState state);

    /// <summary>
    /// Updates drive activity. This does not mount an image.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="drive">The drive.</param>
    /// <param name="status">The activity.</param>
    /// <exception cref="InvalidOperationException">Thrown when the drive is not in the topology.</exception>
    void SetDriveStatus(MachineId machineId, DriveId drive, HostDriveStatus status);

    /// <summary>
    /// Reports a breakpoint hit. The debugger must be attached and the address must have a breakpoint.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="address">The address.</param>
    /// <exception cref="InvalidOperationException">Thrown when the debugger is not attached or the address has no breakpoint.</exception>
    void RaiseBreakpointHit(MachineId machineId, uint address);

    /// <summary>
    /// Copies the input queue without removing it.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The events, oldest first.</returns>
    IReadOnlyList<HostInputEntry> CopyInputLog(MachineId machineId);

    /// <summary>
    /// Takes the input queue. The machine edge calls this once per batch, not once per cycle.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The events, oldest first. The queue is empty afterward.</returns>
    IReadOnlyList<HostInputEntry> DrainInput(MachineId machineId);
}