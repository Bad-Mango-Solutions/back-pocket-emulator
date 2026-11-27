// <copyright file="HostMachineInfo.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The control-plane summary for one machine. Profile fields are editable only while <see cref="State"/> is <see cref="HostRunState.Off"/>.
/// </summary>
/// <param name="Id">The machine id.</param>
/// <param name="Name">The instance name.</param>
/// <param name="State">The run state.</param>
/// <param name="PowerCapabilities">
/// The power record, or <see langword="null"/> when the host sends none. Clients then use the conservative set.
/// </param>
/// <param name="NominalClockHz">The nominal clock in Hz, before the throttle.</param>
/// <param name="Throttle">The selected throttle. This is not the measured speed.</param>
/// <param name="EffectiveClockHz">
/// The clock the throttle selects. 0 means <see cref="HostThrottle.Max"/> (unthrottled).
/// </param>
/// <param name="MeasuredSpeed">
/// The measured speed as a ratio of nominal, such as 1.0 for 100 percent.
/// <see langword="null"/> until the host has a sample. Distinct from <see cref="Throttle"/>.
/// </param>
/// <param name="ProfileId">The template this instance follows.</param>
/// <param name="ProfileOverlayJson">
/// The instance overlay document, or <see langword="null"/> when the instance follows the template with no local overrides.
/// </param>
/// <param name="DebuggerAttached">Whether the debugger extension is attached.</param>
/// <param name="CpuSummary">CPU text for the status window, or <see langword="null"/>.</param>
/// <param name="SchedulerSummary">Scheduler text for the status window, or <see langword="null"/>.</param>
/// <param name="BootDriveId">
/// The boot drive label (<c>S6 D1</c>), or <see langword="null"/> when the host marks no boot drive.
/// A drop on the monitor inserts into this drive.
/// </param>
/// <param name="OutputKind">Composite (raw signal, client may decode) or RGB (finished BGRA).</param>
/// <param name="StartPaused">Whether Power On leaves the machine paused.</param>
public sealed record HostMachineInfo(
    MachineId Id,
    string Name,
    HostRunState State,
    PowerCapabilities? PowerCapabilities,
    long NominalClockHz,
    HostThrottle Throttle,
    long EffectiveClockHz,
    double? MeasuredSpeed,
    string ProfileId,
    string? ProfileOverlayJson,
    bool DebuggerAttached,
    string? CpuSummary,
    string? SchedulerSummary,
    string? BootDriveId,
    HostVideoKind OutputKind,
    bool StartPaused);