// <copyright file="GuestOsSystemCallEventArgs.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Core;

/// <summary>
/// One entry to a system-call address the boot monitor owns.
/// </summary>
/// <remarks>
/// The monitor registers the single trap at that address, because
/// <see cref="BadMango.Emulator.Bus.TrapRegistry"/> rejects a duplicate key.
/// Subscribers, including a later slotless clock, listen here instead of registering their own trap.
/// The handler returns not-handled, so the guest instruction still runs.
/// </remarks>
public sealed class GuestOsSystemCallEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsSystemCallEventArgs"/> class.
    /// </summary>
    /// <param name="at">The machine cycle when the call was entered.</param>
    /// <param name="pc">The program counter, which is the system-call entry.</param>
    /// <param name="os">The operating system the active boot profile named.</param>
    /// <param name="parameterAddress">The first guest byte after the call instruction.</param>
    /// <param name="detail">The profile's name for the call. Not empty.</param>
    /// <exception cref="ArgumentException">If <paramref name="detail"/> is empty.</exception>
    public GuestOsSystemCallEventArgs(Cycle at, Addr pc, GuestOsId os, ushort parameterAddress, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        this.At = at;
        this.Pc = pc;
        this.Os = os;
        this.ParameterAddress = parameterAddress;
        this.Detail = detail;
    }

    /// <summary>Gets the machine cycle when the call was entered.</summary>
    /// <value>The cycle stamp.</value>
    public Cycle At { get; }

    /// <summary>Gets the program counter.</summary>
    /// <value>The system-call entry address.</value>
    public Addr Pc { get; }

    /// <summary>Gets the operating system that owns the call.</summary>
    /// <value>The active profile's id.</value>
    public GuestOsId Os { get; }

    /// <summary>Gets the first byte after the call instruction.</summary>
    /// <value>For a <c>JSR</c> this is the byte the guest left for the callee.</value>
    public ushort ParameterAddress { get; }

    /// <summary>Gets the profile's name for the call.</summary>
    /// <value>A non-empty string such as "MLI $C8 OPEN".</value>
    public string Detail { get; }
}