// <copyright file="HostTransportInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The transport a session is using, and whether that connection is usable.
/// </summary>
/// <param name="Kind">The transport kind.</param>
/// <param name="Name">The transport name, such as <c>loopback</c>.</param>
/// <param name="ApiVersion">The <see cref="HostApi.Version"/> this session speaks.</param>
/// <param name="Healthy">Whether the connection can accept commands.</param>
/// <param name="StatusLine">A short status line, such as <c>Loopback · healthy</c>.</param>
public sealed record HostTransportInfo(
    HostTransportKind Kind,
    string Name,
    int ApiVersion,
    bool Healthy,
    string StatusLine);