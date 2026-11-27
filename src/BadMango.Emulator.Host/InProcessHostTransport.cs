// <copyright file="InProcessHostTransport.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// Production in-process host transport that binds host contracts onto live machines.
/// </summary>
public sealed class InProcessHostTransport : IHostTransport
{
    private readonly string libraryRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessHostTransport"/> class.
    /// </summary>
    /// <param name="libraryRoot">Optional library root used for <c>library://</c> profile paths.</param>
    public InProcessHostTransport(string? libraryRoot = null)
    {
        this.libraryRoot = string.IsNullOrWhiteSpace(libraryRoot)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".backpocket")
            : Path.GetFullPath(libraryRoot);
    }

    /// <inheritdoc />
    public string Name => "loopback";

    /// <inheritdoc />
    public int ApiVersion => HostApi.Version;

    /// <summary>
    /// Opens an in-process host session.
    /// </summary>
    /// <param name="apiVersion">The host API version requested by the client.</param>
    /// <returns>The in-process host session.</returns>
    /// <exception cref="HostApiVersionException">Thrown when <paramref name="apiVersion"/> does not match <see cref="HostApi.Version"/>.</exception>
    public InProcessHostSession Connect(int apiVersion)
    {
        if (apiVersion != HostApi.Version)
        {
            throw new HostApiVersionException(apiVersion, HostApi.Version);
        }

        return new InProcessHostSession(libraryRoot);
    }

    /// <inheritdoc />
    IHostSession IHostTransport.Connect(int apiVersion) => Connect(apiVersion);
}