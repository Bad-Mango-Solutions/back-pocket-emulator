// <copyright file="GuestOsMonitorInstaller.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.GuestOs;

using Serilog;

/// <summary>
/// Installs the guest OS detector and <see cref="BootSequenceMonitor"/> the way <c>os on</c> does.
/// </summary>
public static class GuestOsMonitorInstaller
{
    /// <summary>
    /// Installs the detector and boot monitor when they are absent.
    /// </summary>
    /// <param name="machine">The machine that receives the monitor.</param>
    /// <param name="logger">Logger passed to a detector and monitor created by this call.</param>
    /// <param name="clearExistingLog">
    /// When <see langword="true"/> and a monitor is already installed, clears its milestone log and
    /// returns the boot stage to a fresh boot. When <see langword="false"/>, an installed monitor is left unchanged.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A second monitor is never added, and its traps stay observe-only.
    /// <c>os on</c> passes <see langword="false"/> so a log already in progress is kept.
    /// <c>boot --log</c> and a logged cold reset pass <see langword="true"/> so the new boot starts with an empty log.
    /// </remarks>
    public static void Install(IMachine machine, ILogger logger, bool clearExistingLog)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(logger);

        if (machine.GetComponent<BootSequenceMonitor>() is { } existing)
        {
            if (clearExistingLog)
            {
                existing.PrepareForBoot();
            }

            ProDosFileLoadTracker.Install(machine, existing);
            return;
        }

        var detector = machine.GetComponent<IGuestOsDetector>()
            ?? BootSequenceMonitor.CreateDetector(machine, logger);
        if (!machine.HasComponent<IGuestOsDetector>())
        {
            machine.AddComponent(detector);
        }

        var monitor = new BootSequenceMonitor(logger, detector, BootSequenceMonitor.CreateStandardProfiles());
        monitor.Attach(machine);
        machine.AddComponent(monitor);
        ProDosFileLoadTracker.Install(machine, monitor);
        machine.GetComponent<ProDosClockDevice>()?.Bind(machine);
        monitor.PublishFromDetector();
    }
}