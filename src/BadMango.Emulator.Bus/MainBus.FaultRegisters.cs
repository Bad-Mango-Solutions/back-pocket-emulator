// <copyright file="MainBus.FaultRegisters.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Runtime.CompilerServices;

using BadMango.Emulator.Core;

/// <content>
/// Fault-status and fault-address registers, and the private latch shared by every failing <c>Try*</c> path.
/// </content>
public sealed partial class MainBus
{
    /// <summary>
    /// Low byte of <see cref="FaultKind"/>. A 65C02 latch never stores the high byte.
    /// </summary>
    private const FaultKind LowByteMask = (FaultKind)0x00FF;

    /// <summary>
    /// Latched fault status. Zero means no fault is latched.
    /// </summary>
    private FaultKind faultStatus;

    /// <summary>
    /// Address of the byte that raised <see cref="faultStatus"/>.
    /// </summary>
    private Addr faultAddress;

    /// <summary>
    /// Width of the access that was latched. Part of the private latch, not a third register.
    /// </summary>
    private byte latchedWidthBits;

    /// <summary>
    /// Intent of the access that was latched.
    /// </summary>
    private AccessIntent latchedIntent;

    /// <summary>
    /// Mode of the access that was latched.
    /// </summary>
    private BusAccessMode latchedMode;

    /// <summary>
    /// Source id of the access that was latched.
    /// </summary>
    private int latchedSourceId;

    /// <summary>
    /// Device id of the access that was latched.
    /// </summary>
    private int latchedDeviceId;

    /// <summary>
    /// Region tag of the access that was latched.
    /// </summary>
    private RegionTag latchedRegionTag;

    /// <summary>
    /// Cycle stamp of the access that was latched.
    /// </summary>
    private ulong latchedCycle;

    /// <inheritdoc />
    public FaultKind FaultStatus => faultStatus;

    /// <inheritdoc />
    public Addr FaultAddress => faultAddress;

    /// <summary>
    /// Gets a <see cref="BusFault"/> built from the private latch and the two registers.
    /// </summary>
    /// <value>
    /// The latched fault, or a zero fault when <see cref="FaultStatus"/> is <see cref="FaultKind.None"/>.
    /// </value>
    /// <remarks>
    /// This is not a third architectural register. The CPU slow path and every <c>Try*</c> failure
    /// share <see cref="LatchFault"/>, which is what fills the latch. Program counter is not known
    /// here, so a record built from the latch leaves it at zero.
    /// </remarks>
    internal BusFault LatchedFault => faultStatus == FaultKind.None
        ? default
        : new BusFault(
            faultStatus,
            faultAddress,
            latchedWidthBits,
            latchedIntent,
            latchedMode,
            latchedSourceId,
            latchedDeviceId,
            latchedRegionTag,
            latchedCycle);

    /// <inheritdoc />
    public void ClearFaultStatus()
    {
        ClearFaultRegisters();
    }

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
        ClearFaultRegisters();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The release edge clears again so a fault observed while RESET was held does not
    /// survive the pulse. The reset cycle is not stored.
    /// </remarks>
    public void OnResetReleased(Cycle cycle)
    {
        ClearFaultRegisters();
    }

    /// <summary>
    /// Zeroes the status, the address, and the private latch.
    /// </summary>
    /// <remarks>
    /// Host clear and both edges of machine reset share this method. A successful access
    /// and the CPU fast path do not call it.
    /// </remarks>
    private void ClearFaultRegisters()
    {
        faultStatus = FaultKind.None;
        faultAddress = 0;
        latchedWidthBits = 0;
        latchedIntent = default;
        latchedMode = default;
        latchedSourceId = 0;
        latchedDeviceId = 0;
        latchedRegionTag = default;
        latchedCycle = 0;
    }

    /// <summary>
    /// Latches the first non-debug failure and leaves a latched fault unchanged.
    /// </summary>
    /// <param name="access">The access that failed. Debug accesses do not latch.</param>
    /// <param name="fault">The fault being returned to the caller.</param>
    /// <remarks>
    /// <para>
    /// <see cref="TryRead8"/>, <see cref="TryWrite8"/>, and the wide <c>Try*</c> early returns
    /// are the only callers. The CPU slow path reaches this helper by calling those methods.
    /// The fast path does not.
    /// </para>
    /// <para>
    /// High-byte flags are stripped. A 65C02 latch stores one low-byte reason. A value that
    /// has no low-byte bit, such as <see cref="FaultKind.BankCrossing"/> alone, is not a fault
    /// and is not stored.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LatchFault(in BusAccess access, in BusFault fault)
    {
        if (access.IsDebugAccess || fault.Kind == FaultKind.None || faultStatus != FaultKind.None)
        {
            return;
        }

        FaultKind kind = fault.Kind & LowByteMask;
        if (kind == FaultKind.None)
        {
            return;
        }

        faultStatus = kind;
        faultAddress = fault.Address;
        latchedWidthBits = fault.WidthBits;
        latchedIntent = fault.Intent;
        latchedMode = fault.Mode;
        latchedSourceId = fault.SourceId;
        latchedDeviceId = fault.DeviceId;
        latchedRegionTag = fault.RegionTag;
        latchedCycle = fault.Cycle;
    }

    /// <summary>
    /// Latches <paramref name="fault"/> and returns it so a wide <c>Try*</c> early return
    /// uses the same helper as <see cref="TryRead8"/>.
    /// </summary>
    /// <param name="access">The access that failed.</param>
    /// <param name="fault">The fault to return.</param>
    /// <returns><paramref name="fault"/>.</returns>
    private BusFault LatchAndReturn(in BusAccess access, BusFault fault)
    {
        LatchFault(in access, in fault);
        return fault;
    }
}