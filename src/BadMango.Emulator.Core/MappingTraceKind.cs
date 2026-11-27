// <copyright file="MappingTraceKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

/// <summary>
/// Discriminates a record in the mapping-trace ring.
/// </summary>
public enum MappingTraceKind : byte
{
    /// <summary>
    /// A slow-path byte access.
    /// </summary>
    Access = 1,

    /// <summary>
    /// A page-table store. The record names the target after the store.
    /// </summary>
    Remap = 2,
}