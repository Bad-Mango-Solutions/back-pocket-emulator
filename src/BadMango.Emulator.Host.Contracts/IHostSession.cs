// <copyright file="IHostSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One client connection to a host. Machines are addressed through <see cref="Control"/>.
/// </summary>
/// <remarks>
/// <para>
/// The four planes are the only way a client powers, displays, hears, or drives a machine.
/// Implementing them does not require a reference to <c>IMachine</c>, <c>VideoDevice</c>,
/// or <c>IAudioOutput</c>.
/// </para>
/// <para>
/// Calls are safe from a client thread. The machine thread stays exclusive. A later binding
/// posts machine mutation through <c>IMachineDispatcher</c> at an instruction boundary.
/// Nothing on this session is polled from the CPU loop or the bus.
/// </para>
/// </remarks>
public interface IHostSession : IDisposable
{
    /// <summary>
    /// Gets the host API version this session speaks.
    /// </summary>
    int ApiVersion { get; }

    /// <summary>
    /// Gets the transport this session is connected with.
    /// </summary>
    HostTransportInfo Transport { get; }

    /// <summary>
    /// Gets the control plane.
    /// </summary>
    IHostControl Control { get; }

    /// <summary>
    /// Gets the presentation plane.
    /// </summary>
    IHostPresentation Presentation { get; }

    /// <summary>
    /// Gets the input plane.
    /// </summary>
    IHostInput Input { get; }

    /// <summary>
    /// Gets the debugger extension of the control plane.
    /// </summary>
    IHostDebugger Debugger { get; }
}