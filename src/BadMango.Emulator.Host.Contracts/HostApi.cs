// <copyright file="HostApi.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Identifies the host API contract this assembly speaks.
/// </summary>
/// <remarks>
/// <para>
/// In-process clients compile against this assembly. A later IPC or network transport
/// exchanges <see cref="Version"/> when it connects and refuses a mismatch, so those
/// transports can be added without breaking a client that stays on the loopback.
/// </para>
/// <para>
/// Adding an optional field with a default stays inside version 1. Removing, renaming,
/// or changing the meaning of a member is a new version.
/// </para>
/// </remarks>
public static class HostApi
{
    /// <summary>
    /// The contract version. Version 1 is the in-process loopback contract.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The handshake name carried with <see cref="Version"/>.
    /// </summary>
    public const string VersionName = "host-api/v1";
}