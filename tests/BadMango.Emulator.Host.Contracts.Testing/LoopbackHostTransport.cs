// <copyright file="LoopbackHostTransport.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// In-process transport. <see cref="Connect(int)"/> returns the host session itself.
/// There is no serialization and no frame copy.
/// </summary>
/// <remarks>
/// Each call opens an independent host. Two machines on one host are two
/// <see cref="IHostControl.CreateInstance"/> calls on one session, not two connects.
/// </remarks>
public sealed class LoopbackHostTransport : IHostTransport
{
    /// <inheritdoc />
    public string Name => "loopback";

    /// <inheritdoc />
    public int ApiVersion => HostApi.Version;

    /// <summary>
    /// Opens a loopback host.
    /// </summary>
    /// <param name="apiVersion">The version the client requires.</param>
    /// <returns>The session. The same object is the client and the host.</returns>
    /// <exception cref="HostApiVersionException">Thrown when <paramref name="apiVersion"/> is not <see cref="HostApi.Version"/>.</exception>
    public LoopbackHostSession Connect(int apiVersion)
    {
        if (apiVersion != HostApi.Version)
        {
            throw new HostApiVersionException(apiVersion, HostApi.Version);
        }

        return new LoopbackHostSession();
    }

    /// <inheritdoc />
    IHostSession IHostTransport.Connect(int apiVersion) => Connect(apiVersion);
}