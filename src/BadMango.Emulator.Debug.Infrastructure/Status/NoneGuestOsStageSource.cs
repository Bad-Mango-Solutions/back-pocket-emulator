// <copyright file="NoneGuestOsStageSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Status;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Boot-stage source that always reports <see cref="GuestOsBootStage.None"/>.
/// </summary>
/// <remarks>
/// Inject this to force <see cref="GuestOsBootStage.None"/> even when a
/// <see cref="T:BadMango.Emulator.Devices.GuestOs.IBootSequenceMonitor"/> is registered.
/// When nothing is injected and that monitor is absent,
/// <see cref="GuestOsStatusText.ReadBootStage"/> returns this stage only if detection did not
/// name an operating system. A detected operating system in that case is
/// <see cref="GuestOsBootStage.Ready"/>. The instance is immutable and installs nothing.
/// </remarks>
public sealed class NoneGuestOsStageSource : IGuestOsStageSource
{
    /// <summary>Shared no-op stage source.</summary>
    public static NoneGuestOsStageSource Instance { get; } = new();

    /// <inheritdoc/>
    public GuestOsBootStage Stage => GuestOsBootStage.None;
}