// <copyright file="IHostNotificationSink.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Receives control-plane notices. Methods are called on the client thread that caused the change,
/// after the host has released its lock. A sink must not throw.
/// </summary>
public interface IHostNotificationSink
{
    /// <summary>
    /// A machine was added.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    void OnInstanceAdded(MachineId machineId)
    {
    }

    /// <summary>
    /// A machine was removed.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    void OnInstanceRemoved(MachineId machineId)
    {
    }

    /// <summary>
    /// The run state changed.
    /// </summary>
    /// <param name="notice">The new state.</param>
    void OnStateChanged(HostStateNotice notice)
    {
    }

    /// <summary>
    /// Reset or Hard Reset was accepted.
    /// </summary>
    /// <param name="notice">Which reset.</param>
    void OnReset(HostResetNotice notice)
    {
    }

    /// <summary>
    /// The disk topology or a drive's flags changed.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    void OnDisksChanged(MachineId machineId)
    {
    }

    /// <summary>
    /// A breakpoint hit was reported.
    /// </summary>
    /// <param name="notice">The hit.</param>
    void OnBreakpointHit(HostBreakpointNotice notice)
    {
    }

    /// <summary>
    /// The debugger was attached or detached.
    /// </summary>
    /// <param name="notice">The attachment.</param>
    void OnDebuggerAttachmentChanged(HostDebuggerNotice notice)
    {
    }
}