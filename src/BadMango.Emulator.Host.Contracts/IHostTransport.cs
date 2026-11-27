// <copyright file="IHostTransport.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Connects a client to a host. The loopback is the version-1 transport.
/// </summary>
/// <remarks>
/// A later IPC or network transport implements this same interface and the same session
/// planes. It checks <see cref="HostApi.Version"/> at connect time. The loopback passes
/// session objects by reference and does not serialize them.
/// </remarks>
public interface IHostTransport
{
    /// <summary>
    /// Gets the transport name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the host API version this transport speaks.
    /// </summary>
    int ApiVersion { get; }

    /// <summary>
    /// Opens a session.
    /// </summary>
    /// <param name="apiVersion">The version the client requires. Pass <see cref="HostApi.Version"/>.</param>
    /// <returns>A session whose planes talk to one host.</returns>
    /// <exception cref="HostApiVersionException">Thrown when <paramref name="apiVersion"/> is not <see cref="ApiVersion"/>.</exception>
    IHostSession Connect(int apiVersion);
}