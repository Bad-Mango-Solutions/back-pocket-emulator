// <copyright file="HostRunState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// The run state a client shows for one machine.
/// </summary>
/// <remarks>
/// <para>
/// Off, Running, Paused, and Shutting down are the states the power menu is specified for.
/// <see cref="Sleeping"/> is the state after Sleep. The menu while sleeping is not specified;
/// Resume wakes the machine, and Reset, Hard Reset, and Shut Down or Power Off stay available.
/// <see cref="Stopped"/> means power is on and the CPU is not executing: the guest halted, or the run loop faulted.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HostRunState>))]
public enum HostRunState
{
    /// <summary>
    /// Power is off. Power On is the only power action.
    /// </summary>
    Off,

    /// <summary>
    /// The machine is executing.
    /// </summary>
    Running,

    /// <summary>
    /// Execution is paused. CPU state is kept.
    /// </summary>
    Paused,

    /// <summary>
    /// The guest is in its sleep state. Resume wakes it.
    /// </summary>
    Sleeping,

    /// <summary>
    /// Shut Down has been accepted and power is not off yet.
    /// </summary>
    ShuttingDown,

    /// <summary>
    /// Power is on and the CPU is not executing. STP and a faulted run loop land here.
    /// Resume, Reset, and Hard Reset start the loop again.
    /// </summary>
    Stopped,
}