// <copyright file="IHostControl.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The control plane. Clients power a machine, mount disks, and read machine info through this interface.
/// </summary>
/// <remarks>
/// <para>
/// Power On, Pause, Resume, Sleep, Reset, Hard Reset, Shut Down, and Power Off are the power menu.
/// Shut Down is the soft-power path. Power Off cuts power. Hard Reset cycles power. Sleep enters the
/// guest sleep state. Breakpoints, watches, memory, and disassembly are <see cref="IHostDebugger"/>,
/// not this plane. Attach and detach are here because the shell uses them without the debugger windows.
/// </para>
/// <para>
/// Methods that return <see cref="Task"/> are commands that may wait for the machine thread or a
/// later transport. The loopback applies them before the task completes and does not hop threads.
/// Queries return the last settled snapshot and do not wait. Calls are safe from a client thread.
/// A binding onto a real machine posts the effect through <c>IMachineDispatcher</c> and does not
/// check these methods from the CPU loop.
/// </para>
/// <para>
/// An invalid power transition throws <see cref="InvalidOperationException"/>. The menu only shows
/// the legal actions. Pause while already paused is not a no-op.
/// </para>
/// </remarks>
public interface IHostControl
{
    /// <summary>
    /// Gets the host API version.
    /// </summary>
    int ApiVersion { get; }

    /// <summary>
    /// Gets the transport this host is using.
    /// </summary>
    HostTransportInfo Transport { get; }

    /// <summary>
    /// Adds a machine. It starts off.
    /// </summary>
    /// <param name="spec">The instance name, profile, power record, and clock.</param>
    /// <returns>The new machine id.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the name, profile id, or clock is not usable.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the profile is not on this host.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session is disposed.</exception>
    MachineId CreateInstance(HostInstanceSpec spec);

    /// <summary>
    /// Removes a machine that is off.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown or is not off.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the session is disposed.</exception>
    void RemoveInstance(MachineId machineId);

    /// <summary>
    /// Lists every machine on this host.
    /// </summary>
    /// <returns>The machines. The order is the order they were created.</returns>
    IReadOnlyList<HostMachineInfo> ListInstances();

    /// <summary>
    /// Gets one machine's summary.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The summary, including <see cref="PowerCapabilities"/> when the host sends a record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown.</exception>
    HostMachineInfo GetMachineInfo(MachineId machineId);

    /// <summary>
    /// Stores or replaces a profile document. This does not change an instance that is already running.
    /// </summary>
    /// <param name="profile">The document.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the id, name, or JSON is empty.</exception>
    void UpsertProfile(HostProfileDocument profile);

    /// <summary>
    /// Gets a stored profile.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the id is unknown.</exception>
    HostProfileDocument GetProfile(string profileId);

    /// <summary>
    /// Lists stored profiles.
    /// </summary>
    /// <returns>The id and name of each profile.</returns>
    IReadOnlyList<HostProfileSummary> ListProfiles();

    /// <summary>
    /// Sets or clears an instance's profile overlay. Legal only while the machine is off.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="overlayJson">
    /// A JSON object merged onto the profile, or <see langword="null"/> or whitespace to follow the template.
    /// Nested objects merge field by field. Any other nested value replaces that field.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="overlayJson"/> is not a JSON object.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown or is not off.</exception>
    void SetInstanceOverlay(MachineId machineId, string? overlayJson);

    /// <summary>
    /// Powers the machine on. It runs, or it pauses when the instance was created with start-paused.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown or is not off.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task PowerOnAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a running machine.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is not running.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task PauseAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused, sleeping, or stopped machine. A stopped guest that halted with STP is warm-reset first.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is not paused, sleeping, or stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task ResumeAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters the guest sleep state. Legal only while running, and only when <see cref="PowerCapabilities.Sleep"/> is set.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when sleep is not available or the machine is not running.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task SleepAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the machine and keeps the run state. Legal while the machine is on. From <see cref="HostRunState.Stopped"/>, the halt is cleared and the run loop starts.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is off.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task ResetAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Power-cycles the machine and keeps the run state. Legal while the machine is on, when Hard Reset is in the capability set. From <see cref="HostRunState.Stopped"/>, the run loop starts again.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is off or Hard Reset is not available.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task HardResetAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the guest to power off through the soft-power path. The state passes through <see cref="HostRunState.ShuttingDown"/> and then <see cref="HostRunState.Off"/>.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the machine is off.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine has no soft-power circuit, including when no capability record was sent.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task ShutDownAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cuts power. The state becomes <see cref="HostRunState.Off"/> with no shutting-down step.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the machine is off.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine uses soft power. Shut Down is the action for that machine.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task PowerOffAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the throttle. This does not change the measured speed.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="throttle">The throttle.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the command.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="throttle"/> is not a defined value.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task SetThrottleAsync(MachineId machineId, HostThrottle throttle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mounts an image. The loopback records the path and does not open it.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="drive">The drive.</param>
    /// <param name="path">The image path the client supplied.</param>
    /// <param name="writeProtect">Whether the image is write-protected.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the host has accepted the mount.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the drive is not in the topology or the drive is busy.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task MountDiskAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejects the image in a drive.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="drive">The drive.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the drive is empty.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the drive is empty, unknown, or busy.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task EjectDiskAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the write-protect flag on a mounted image.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="drive">The drive.</param>
    /// <param name="writeProtect">Whether writes are refused.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the flag is stored.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the drive is empty, unknown, or busy.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task SetWriteProtectAsync(MachineId machineId, DriveId drive, bool writeProtect, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists controllers and drives.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The topology the host reports. The client does not assume a Disk II count.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown.</exception>
    IReadOnlyList<HostDiskController> ListDisks(MachineId machineId);

    /// <summary>
    /// Lists the slot strip. Slot changes are profile edits and are not a method on this plane.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>Aux and slots 1–7.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the machine is unknown.</exception>
    IReadOnlyList<HostSlotInfo> ListSlots(MachineId machineId);

    /// <summary>
    /// Attaches the debugger extension. Idempotent when it is already attached.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the debugger is attached.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task AttachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches the debugger extension. Idempotent when it is not attached.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="cancellationToken">Cancels before the command is applied.</param>
    /// <returns>A task that completes when the debugger is detached.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    Task DetachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to control-plane notices.
    /// </summary>
    /// <param name="sink">The sink.</param>
    /// <returns>A handle that removes the sink.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> is <see langword="null"/>.</exception>
    IDisposable Subscribe(IHostNotificationSink sink);
}