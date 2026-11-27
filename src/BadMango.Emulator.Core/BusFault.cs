// <copyright file="BusFault.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Represents a fault that occurred during a bus operation.
/// </summary>
/// <remarks>
/// <para>
/// The fault payload carries exactly what tooling and exception logic need
/// without allocations. The CPU can record the fault address, kind, and intent
/// into its fault registers without the bus knowing anything about the CPU's
/// exception model.
/// </para>
/// <para>
/// Faults are first-class return values from bus operations, not exceptions.
/// This keeps fault handling cheap and predictable in the hot path.
/// </para>
/// </remarks>
/// <param name="Kind">The type of fault that occurred.</param>
/// <param name="Address">The address where the fault occurred.</param>
/// <param name="WidthBits">The width of the attempted access (8, 16, or 32).</param>
/// <param name="Intent">The intent of the access that faulted.</param>
/// <param name="Mode">The bus access mode at the time of the fault.</param>
/// <param name="SourceId">The structural ID of the access initiator.</param>
/// <param name="DeviceId">The device ID from the page entry, or -1 if unmapped.</param>
/// <param name="RegionTag">The region tag from the page entry, if available.</param>
/// <param name="Cycle">The machine cycle when the fault occurred.</param>
/// <param name="Pc">
/// Program counter supplied by the caller. A CPU sets this from its PC when it builds the
/// access that asks for the record. The bus does not read a CPU register. A caller that
/// leaves it unset stores <c>0</c>. The private fault latch does not keep this field.
/// </param>
public readonly record struct BusFault(
    FaultKind Kind,
    Addr Address,
    byte WidthBits,
    AccessIntent Intent,
    BusAccessMode Mode,
    int SourceId,
    int DeviceId,
    RegionTag RegionTag,
    ulong Cycle,
    Addr Pc = 0)
{
    /// <summary>
    /// Gets a value indicating whether this represents no fault (success).
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is <see cref="FaultKind.None"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsSuccess => Kind == FaultKind.None;

    /// <summary>
    /// Gets a value indicating whether a fault occurred.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is not <see cref="FaultKind.None"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsFault => Kind != FaultKind.None;

    /// <summary>
    /// Gets a value indicating whether this is an NX fault.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is <see cref="FaultKind.Nx"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsNxFault => Kind == FaultKind.Nx;

    /// <summary>
    /// Gets a value indicating whether this is a permission fault.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is <see cref="FaultKind.Permission"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsPermissionFault => Kind == FaultKind.Permission;

    /// <summary>
    /// Gets a value indicating whether this is an unmapped fault.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is <see cref="FaultKind.Unmapped"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsUnmappedFault => Kind == FaultKind.Unmapped;

    /// <summary>
    /// Gets a value indicating whether this is a device fault.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if <see cref="Kind"/> is <see cref="FaultKind.DeviceFault"/>;
    /// otherwise, <see langword="false"/>.
    /// </value>
    public bool IsDeviceFault => Kind == FaultKind.DeviceFault;

    /// <summary>
    /// Creates a success (no fault) result.
    /// </summary>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.None"/>.</returns>
    public static BusFault Success() => new(FaultKind.None, 0, 0, AccessIntent.DataRead, BusAccessMode.Atomic, 0, 0, RegionTag.Unknown, 0, 0);

    /// <summary>
    /// Creates an unmapped fault.
    /// </summary>
    /// <param name="access">The bus access that faulted.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.Unmapped"/>.</returns>
    public static BusFault Unmapped(in BusAccess access) =>
        FromAccess(FaultKind.Unmapped, in access, deviceId: -1, RegionTag.Unmapped);

    /// <summary>
    /// Creates a permission fault.
    /// </summary>
    /// <param name="access">The bus access that faulted.</param>
    /// <param name="deviceId">The device ID from the page entry.</param>
    /// <param name="regionTag">The region tag from the page entry.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.Permission"/>.</returns>
    public static BusFault PermissionDenied(in BusAccess access, int deviceId, RegionTag regionTag) =>
        FromAccess(FaultKind.Permission, in access, deviceId, regionTag);

    /// <summary>
    /// Creates an NX (no execute) fault.
    /// </summary>
    /// <param name="access">The bus access that faulted.</param>
    /// <param name="deviceId">The device ID from the page entry.</param>
    /// <param name="regionTag">The region tag from the page entry.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.Nx"/>.</returns>
    public static BusFault NoExecute(in BusAccess access, int deviceId, RegionTag regionTag) =>
        FromAccess(FaultKind.Nx, in access, deviceId, regionTag);

    /// <summary>
    /// Creates a misaligned access fault.
    /// </summary>
    /// <param name="access">The bus access that faulted.</param>
    /// <param name="deviceId">The device ID from the page entry.</param>
    /// <param name="regionTag">The region tag from the page entry.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.Misaligned"/>.</returns>
    public static BusFault Misaligned(in BusAccess access, int deviceId, RegionTag regionTag) =>
        FromAccess(FaultKind.Misaligned, in access, deviceId, regionTag);

    /// <summary>
    /// Creates a device fault.
    /// </summary>
    /// <param name="access">The bus access that faulted.</param>
    /// <param name="deviceId">The device ID that reported the fault.</param>
    /// <param name="regionTag">The region tag from the page entry.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.DeviceFault"/>.</returns>
    public static BusFault Device(in BusAccess access, int deviceId, RegionTag regionTag) =>
        FromAccess(FaultKind.DeviceFault, in access, deviceId, regionTag);

    /// <summary>
    /// Creates a success (no fault) result with context from an access.
    /// </summary>
    /// <param name="access">The bus access that succeeded.</param>
    /// <param name="deviceId">The device ID that handled the access.</param>
    /// <param name="regionTag">The region tag of the accessed page.</param>
    /// <returns>A <see cref="BusFault"/> with <see cref="Kind"/> set to <see cref="FaultKind.None"/>.</returns>
    /// <remarks>
    /// Kept for the named-factory tests. <see cref="MainBus"/> success paths do not call it.
    /// A successful bus result uses a zero <see cref="BusFault"/> instead.
    /// </remarks>
    internal static BusFault Success(in BusAccess access, int deviceId, RegionTag regionTag) =>
        FromAccess(FaultKind.None, in access, deviceId, regionTag);

    /// <summary>
    /// Builds one fault record from an access, including the caller's program counter.
    /// </summary>
    /// <param name="kind">The fault kind.</param>
    /// <param name="access">The access that faulted. <see cref="BusAccess.Pc"/> is copied onto the record.</param>
    /// <param name="deviceId">The device id to store.</param>
    /// <param name="regionTag">The region tag to store.</param>
    /// <returns>The fault record.</returns>
    private static BusFault FromAccess(FaultKind kind, in BusAccess access, int deviceId, RegionTag regionTag) =>
        new(kind, access.Address, access.WidthBits, access.Intent, access.Mode, access.SourceId, deviceId, regionTag, access.Cycle, access.Pc);
}