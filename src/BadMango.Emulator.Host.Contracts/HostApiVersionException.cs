// <copyright file="HostApiVersionException.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Thrown when a client asks a transport for a host API version it does not speak.
/// </summary>
public sealed class HostApiVersionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HostApiVersionException"/> class.
    /// </summary>
    /// <param name="requested">The version the client asked for.</param>
    /// <param name="supported">The version this transport speaks.</param>
    public HostApiVersionException(int requested, int supported)
        : base($"Host API version {requested} is not supported. This transport speaks version {supported} ({HostApi.VersionName}).")
    {
        Requested = requested;
        Supported = supported;
    }

    /// <summary>
    /// Gets the version the client asked for.
    /// </summary>
    public int Requested { get; }

    /// <summary>
    /// Gets the version this transport speaks.
    /// </summary>
    public int Supported { get; }
}