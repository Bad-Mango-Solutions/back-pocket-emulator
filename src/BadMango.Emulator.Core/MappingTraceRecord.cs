// <copyright file="MappingTraceRecord.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// One fixed-ring entry for a slow-path byte access or a page-table store.
/// </summary>
/// <remarks>
/// The target name is the existing target name reference. Copying it
/// does not allocate a string. A remap stores one soft-switch list produced by
/// <see cref="Interfaces.ISoftSwitchProvider.GetSoftSwitchStates"/>; an access stores none.
/// </remarks>
public readonly struct MappingTraceRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MappingTraceRecord"/> struct.
    /// </summary>
    /// <param name="kind">Whether this entry is an access or a remap.</param>
    /// <param name="cycle">The access cycle, or zero for a remap.</param>
    /// <param name="address">The byte address, or the first address of a remap's page range.</param>
    /// <param name="intent">The access intent. Remaps leave this at the default.</param>
    /// <param name="targetName">The target's existing name reference, or <see langword="null"/>.</param>
    /// <param name="region">The page's region tag.</param>
    /// <param name="physicalBase">The target-local physical base.</param>
    /// <param name="deviceId">The page's device id.</param>
    /// <param name="pageStart">The first 4 KB page index, inclusive.</param>
    /// <param name="pageEnd">The last 4 KB page index, inclusive.</param>
    /// <param name="perms">The permissions stored with a remap.</param>
    /// <param name="naturalFastOps">The section 7.2 fast-path ops before a trace range forces <see cref="FastPathOps.None"/>.</param>
    /// <param name="softSwitches">The one soft-switch snapshot for a remap, or <see langword="null"/> for an access.</param>
    private MappingTraceRecord(
        MappingTraceKind kind,
        ulong cycle,
        uint address,
        AccessIntent intent,
        string? targetName,
        RegionTag region,
        uint physicalBase,
        int deviceId,
        int pageStart,
        int pageEnd,
        PagePerms perms,
        FastPathOps naturalFastOps,
        IReadOnlyList<SoftSwitchState>? softSwitches)
    {
        Kind = kind;
        Cycle = cycle;
        Address = address;
        Intent = intent;
        TargetName = targetName;
        Region = region;
        PhysicalBase = physicalBase;
        DeviceId = deviceId;
        PageStart = pageStart;
        PageEnd = pageEnd;
        Perms = perms;
        NaturalFastOps = naturalFastOps;
        SoftSwitches = softSwitches;
    }

    /// <summary>
    /// Gets whether this entry is an access or a remap.
    /// </summary>
    public MappingTraceKind Kind { get; }

    /// <summary>
    /// Gets the access cycle. Remaps store zero.
    /// </summary>
    public ulong Cycle { get; }

    /// <summary>
    /// Gets the byte address, or the first address of a remap's page range.
    /// </summary>
    public uint Address { get; }

    /// <summary>
    /// Gets the access intent.
    /// </summary>
    public AccessIntent Intent { get; }

    /// <summary>
    /// Gets the target's existing name reference.
    /// </summary>
    public string? TargetName { get; }

    /// <summary>
    /// Gets the page's region tag.
    /// </summary>
    public RegionTag Region { get; }

    /// <summary>
    /// Gets the target-local physical base after the store, or the page's base for an access.
    /// </summary>
    public uint PhysicalBase { get; }

    /// <summary>
    /// Gets the page's device id.
    /// </summary>
    public int DeviceId { get; }

    /// <summary>
    /// Gets the first 4 KB page index covered by this entry, inclusive.
    /// </summary>
    public int PageStart { get; }

    /// <summary>
    /// Gets the last 4 KB page index covered by this entry, inclusive.
    /// </summary>
    public int PageEnd { get; }

    /// <summary>
    /// Gets the permissions published by a remap.
    /// </summary>
    public PagePerms Perms { get; }

    /// <summary>
    /// Gets the natural fast-path operations from section 7.2, before a trace range is applied.
    /// </summary>
    public FastPathOps NaturalFastOps { get; }

    /// <summary>
    /// Gets the soft-switch snapshot taken for a remap, or <see langword="null"/> for an access.
    /// </summary>
    public IReadOnlyList<SoftSwitchState>? SoftSwitches { get; }

    /// <summary>
    /// Builds an access record from the slow-path byte that just completed.
    /// </summary>
    /// <param name="cycle">The access cycle.</param>
    /// <param name="address">The byte address.</param>
    /// <param name="intent">The access intent.</param>
    /// <param name="targetName">The page target's existing name reference.</param>
    /// <param name="region">The page's region tag.</param>
    /// <param name="physicalBase">The page's physical base.</param>
    /// <param name="deviceId">The page's device id.</param>
    /// <param name="pageIndex">The 4 KB page index.</param>
    /// <returns>A record that does not reference a soft-switch list.</returns>
    public static MappingTraceRecord Access(
        ulong cycle,
        uint address,
        AccessIntent intent,
        string? targetName,
        RegionTag region,
        uint physicalBase,
        int deviceId,
        int pageIndex)
    {
        return new MappingTraceRecord(
            MappingTraceKind.Access,
            cycle,
            address,
            intent,
            targetName,
            region,
            physicalBase,
            deviceId,
            pageIndex,
            pageIndex,
            PagePerms.None,
            FastPathOps.None,
            null);
    }

    /// <summary>
    /// Builds a remap record for one stored page.
    /// </summary>
    /// <param name="pageIndex">The page that was stored.</param>
    /// <param name="targetName">The new target's existing name reference.</param>
    /// <param name="region">The new region tag.</param>
    /// <param name="physicalBase">The new physical base.</param>
    /// <param name="deviceId">The device id.</param>
    /// <param name="perms">The permissions stored on the page.</param>
    /// <param name="naturalFastOps">The natural fast-path operations before a trace range forces none.</param>
    /// <param name="softSwitches">The one snapshot for this rebuild. The same list is shared by every page of the rebuild.</param>
    /// <returns>A remap record. <paramref name="targetName"/> is not copied into a new string.</returns>
    public static MappingTraceRecord Remap(
        int pageIndex,
        string? targetName,
        RegionTag region,
        uint physicalBase,
        int deviceId,
        PagePerms perms,
        FastPathOps naturalFastOps,
        IReadOnlyList<SoftSwitchState> softSwitches)
    {
        return new MappingTraceRecord(
            MappingTraceKind.Remap,
            0,
            (uint)pageIndex << 12,
            default,
            targetName,
            region,
            physicalBase,
            deviceId,
            pageIndex,
            pageIndex,
            perms,
            naturalFastOps,
            softSwitches);
    }

    /// <summary>
    /// Reports whether <paramref name="pageIndex"/> lies in this entry's page range.
    /// </summary>
    /// <param name="pageIndex">The 4 KB page index.</param>
    /// <returns><see langword="true"/> when the page is inside <see cref="PageStart"/> and <see cref="PageEnd"/>.</returns>
    public bool IncludesPage(int pageIndex)
    {
        return pageIndex >= PageStart && pageIndex <= PageEnd;
    }
}