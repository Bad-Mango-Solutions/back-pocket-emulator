// <copyright file="HostTransportKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// How a client is connected to the host.
/// </summary>
/// <remarks>
/// Only <see cref="Loopback"/> is implemented here. Local IPC and the remote transport
/// are later versions of the same contracts.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HostTransportKind>))]
public enum HostTransportKind
{
    /// <summary>
    /// The client and the host are the same object in one process. Frames are not copied.
    /// </summary>
    Loopback,

    /// <summary>
    /// A later local IPC transport. Not implemented in this version.
    /// </summary>
    LocalIpc,

    /// <summary>
    /// A later network transport. Not implemented in this version.
    /// </summary>
    Remote,
}