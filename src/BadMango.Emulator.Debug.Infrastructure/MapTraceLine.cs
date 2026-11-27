// <copyright file="MapTraceLine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// One mapping-trace line, either a slow-path access or a remap.
/// </summary>
/// <param name="Kind"><c>access</c> or <c>remap</c>.</param>
/// <param name="Cycle">The access cycle, or zero for a remap.</param>
/// <param name="Address">The byte address, or the first address of the page.</param>
/// <param name="Intent">The access intent. Empty for a remap.</param>
/// <param name="TargetName">The target's existing name, or <see langword="null"/> when the page has no target.</param>
/// <param name="Region">The region tag.</param>
/// <param name="PhysicalBase">The target-local physical base.</param>
/// <param name="DeviceId">The device id.</param>
/// <param name="PageStart">The first 4 KB page index, inclusive.</param>
/// <param name="PageEnd">The last 4 KB page index, inclusive.</param>
/// <param name="Perms">The permissions stored by a remap. Empty for an access.</param>
/// <param name="NaturalFastOps">The natural fast-path operations before a range forces none. Empty for an access.</param>
/// <param name="SoftSwitches">The remap snapshot, or <see langword="null"/> for an access.</param>
public sealed record MapTraceLine(
    string Kind,
    ulong Cycle,
    uint Address,
    string Intent,
    string? TargetName,
    string Region,
    uint PhysicalBase,
    int DeviceId,
    int PageStart,
    int PageEnd,
    string Perms,
    string NaturalFastOps,
    IReadOnlyList<MapTraceSwitch>? SoftSwitches);