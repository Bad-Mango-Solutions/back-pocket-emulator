// <copyright file="OsCallLogMode.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Which guest OS calls the call log records. <see cref="Off"/> subscribes to nothing.
/// </summary>
public enum OsCallLogMode
{
    /// <summary>Record nothing. The log is not attached to the boot monitor.</summary>
    Off = 0,

    /// <summary>ProDOS MLI calls at <c>$BF00</c>.</summary>
    Mli = 1,

    /// <summary>DOS 3.3 file-manager and RWTS calls.</summary>
    Dos = 2,

    /// <summary>MLI calls and DOS 3.3 calls.</summary>
    All = 3,
}