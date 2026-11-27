// <copyright file="HostWatchAccess.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Which accesses a watch reports.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HostWatchAccess>))]
public enum HostWatchAccess
{
    /// <summary>
    /// Reads.
    /// </summary>
    Read,

    /// <summary>
    /// Writes.
    /// </summary>
    Write,

    /// <summary>
    /// Reads and writes.
    /// </summary>
    ReadWrite,
}