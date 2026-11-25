// <copyright file="IOsCallLog.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// A bounded log of ProDOS MLI and DOS 3.3 calls. Absent from the machine when the feature is off.
/// </summary>
/// <remarks>
/// The log subscribes to the boot monitor's observe-only hooks. It peeks guest memory and registers.
/// It does not write them, and it does not register a trap. <see cref="OsCallLogMode.Off"/> drops the
/// subscription, so a later call does not enter the log.
/// </remarks>
public interface IOsCallLog
{
    /// <summary>Raised after a call is stored. Allocates the event argument, so the log raises it only when someone listens.</summary>
    event EventHandler<OsCall>? Recorded;

    /// <summary>Gets which calls are recorded.</summary>
    /// <value>The mode last applied.</value>
    OsCallLogMode Mode { get; }

    /// <summary>Gets the recorded calls, oldest first.</summary>
    /// <value>At most <see cref="OsCallLog.Capacity"/> entries.</value>
    IReadOnlyList<OsCall> Entries { get; }
}