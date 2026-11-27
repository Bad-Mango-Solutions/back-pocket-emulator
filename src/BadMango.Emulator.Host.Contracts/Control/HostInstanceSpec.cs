// <copyright file="HostInstanceSpec.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// What <see cref="IHostControl.CreateInstance"/> needs to add a machine.
/// </summary>
/// <param name="Name">The instance name shown in the instance list.</param>
/// <param name="ProfileId">The id of a profile already stored on the host.</param>
/// <param name="StartPaused">
/// When <see langword="true"/>, Power On leaves the machine paused. This is the launch option "Start paused".
/// </param>
/// <param name="PowerCapabilities">
/// The power record. <see langword="null"/> means the host sends no record and clients use the conservative set.
/// </param>
/// <param name="NominalClockHz">The nominal CPU clock in Hz. The Apple IIe clock is 1,020,484.</param>
/// <param name="OutputKind">Whether presentation is raw composite or finished BGRA.</param>
/// <param name="CpuSummary">Optional CPU text for the status window.</param>
/// <param name="SchedulerSummary">Optional scheduler text for the status window.</param>
public sealed record HostInstanceSpec(
    string Name,
    string ProfileId,
    bool StartPaused = false,
    PowerCapabilities? PowerCapabilities = null,
    long NominalClockHz = 1_020_484,
    HostVideoKind OutputKind = HostVideoKind.Composite,
    string? CpuSummary = null,
    string? SchedulerSummary = null);