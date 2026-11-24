// <copyright file="NoneGuestOsStageSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Status;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Boot-stage source that always reports <see cref="GuestOsBootStage.None"/>.
/// </summary>
/// <remarks>
/// This is the default when no <see cref="IBootSequenceMonitor"/> is registered and the
/// caller did not inject another <see cref="IGuestOsStageSource"/>. The instance is immutable
/// and installs nothing.
/// </remarks>
public sealed class NoneGuestOsStageSource : IGuestOsStageSource
{
    /// <summary>Shared no-op stage source.</summary>
    public static NoneGuestOsStageSource Instance { get; } = new();

    /// <inheritdoc/>
    public GuestOsBootStage Stage => GuestOsBootStage.None;
}