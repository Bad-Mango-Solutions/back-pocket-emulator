// <copyright file="RecordingSink.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Records control-plane notices for assertions.
/// </summary>
internal sealed class RecordingSink : IHostNotificationSink
{
    /// <summary>
    /// Gets the run states in the order they were reported.
    /// </summary>
    public List<HostRunState> States { get; } = [];

    /// <summary>
    /// Gets whether each reset was a hard reset.
    /// </summary>
    public List<bool> HardResets { get; } = [];

    /// <summary>
    /// Gets how many disk notices arrived.
    /// </summary>
    public int DiskChanges { get; private set; }

    /// <summary>
    /// Gets breakpoint addresses in the order they were reported.
    /// </summary>
    public List<uint> Breakpoints { get; } = [];

    /// <inheritdoc />
    public void OnStateChanged(HostStateNotice notice)
    {
        States.Add(notice.State);
    }

    /// <inheritdoc />
    public void OnReset(HostResetNotice notice)
    {
        HardResets.Add(notice.Hard);
    }

    /// <inheritdoc />
    public void OnDisksChanged(MachineId machineId)
    {
        DiskChanges++;
    }

    /// <inheritdoc />
    public void OnBreakpointHit(HostBreakpointNotice notice)
    {
        Breakpoints.Add(notice.Address);
    }
}