// <copyright file="HostSessions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Opens a loopback host and adds a machine that does not load a ROM.
/// </summary>
internal static class HostSessions
{
    /// <summary>
    /// Opens a version-1 loopback session.
    /// </summary>
    /// <returns>The session.</returns>
    public static LoopbackHostSession Open()
    {
        return new LoopbackHostTransport().Connect(HostApi.Version);
    }

    /// <summary>
    /// Stores an opaque profile and adds a machine.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="name">The instance name.</param>
    /// <param name="power">The power record, or <see langword="null"/> for the conservative set.</param>
    /// <param name="startPaused">Whether Power On leaves the machine paused.</param>
    /// <param name="output">The video output.</param>
    /// <returns>The machine id.</returns>
    public static MachineId Add(
        LoopbackHostSession session,
        string name,
        PowerCapabilities? power = null,
        bool startPaused = false,
        HostVideoKind output = HostVideoKind.Composite)
    {
        const string ProfileId = "pocket";
        session.Control.UpsertProfile(new HostProfileDocument(ProfileId, "Pocket", "{}"));
        return session.Control.CreateInstance(new HostInstanceSpec(
            name,
            ProfileId,
            startPaused,
            power,
            OutputKind: output,
            CpuSummary: "65C02",
            SchedulerSummary: "idle"));
    }
}