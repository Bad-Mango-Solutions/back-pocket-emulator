// <copyright file="GuestOsStatusText.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Status;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Reads and formats the guest-OS status row. The words are the same in the window and in text-mode <c>statmon</c>.
/// </summary>
/// <remarks>
/// The row names the operating system, the boot stage, and, when a file is loading, that file.
/// It does not name a slot, a card, or a disk format, and it does not read boot-milestone detail.
/// A new operating system shows up through <see cref="GuestOsInfo.DisplayName"/> and
/// <see cref="GuestOsInfo.VersionLabel"/>. The load text is <see cref="FileLoadProgress.FormatLine"/>.
/// </remarks>
public static class GuestOsStatusText
{
    /// <summary>Text used when the machine has no <see cref="IGuestOsDetector"/>.</summary>
    public const string DetectionOff = "(detection off)";

    /// <summary>Operating-system text for <see cref="GuestOsBootStage.CustomBoot"/>.</summary>
    public const string CustomLoader = "None (custom loader)";

    /// <summary>Placeholder for "no operating system identified" and for <see cref="GuestOsBootStage.None"/>.</summary>
    public const string NotEntered = "\u2014";

    /// <summary>
    /// Reads the guest OS, running detection when the machine has a detector.
    /// </summary>
    /// <param name="machine">The machine to read.</param>
    /// <returns>The detection result, or <see langword="null"/> when detection is off.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> is <see langword="null"/>.</exception>
    public static GuestOsInfo? ReadGuestOs(IMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return machine.GetComponent<IGuestOsDetector>()?.Detect();
    }

    /// <summary>
    /// Resolves the boot stage for <paramref name="machine"/>.
    /// </summary>
    /// <param name="machine">The machine to read.</param>
    /// <param name="stageSource">
    /// Injected stage. When not <see langword="null"/>, this value is the stage, including
    /// <see cref="GuestOsBootStage.None"/> from <see cref="NoneGuestOsStageSource"/>.
    /// </param>
    /// <param name="guestOs">The detection result already read for this refresh, or <see langword="null"/>.</param>
    /// <returns>
    /// The injected stage, or <see cref="IBootSequenceMonitor.Stage"/> when the monitor is present.
    /// When neither is present and <paramref name="guestOs"/> names an operating system, the result is
    /// <see cref="GuestOsBootStage.Ready"/> (detection with no boot observed). Otherwise the result is
    /// <see cref="GuestOsBootStage.None"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> is <see langword="null"/>.</exception>
    public static GuestOsBootStage ReadBootStage(IMachine machine, IGuestOsStageSource? stageSource, GuestOsInfo? guestOs)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (stageSource is not null)
        {
            return stageSource.Stage;
        }

        var monitor = machine.GetComponent<IBootSequenceMonitor>();
        if (monitor is not null)
        {
            return monitor.Stage;
        }

        if (guestOs is not null && guestOs.Id != GuestOsId.None)
        {
            return GuestOsBootStage.Ready;
        }

        return NoneGuestOsStageSource.Instance.Stage;
    }

    /// <summary>
    /// Formats the operating-system half of the row.
    /// </summary>
    /// <param name="guestOs">The detection result, or <see langword="null"/> when detection is off.</param>
    /// <param name="stage">The boot stage resolved for the same refresh.</param>
    /// <returns>
    /// <see cref="CustomLoader"/> for <see cref="GuestOsBootStage.CustomBoot"/>,
    /// <see cref="DetectionOff"/> when <paramref name="guestOs"/> is <see langword="null"/>,
    /// <see cref="NotEntered"/> when the id is <see cref="GuestOsId.None"/>,
    /// otherwise <see cref="GuestOsInfo.DisplayName"/> plus <see cref="GuestOsInfo.VersionLabel"/>.
    /// </returns>
    public static string FormatOperatingSystem(GuestOsInfo? guestOs, GuestOsBootStage stage)
    {
        if (stage == GuestOsBootStage.CustomBoot)
        {
            return CustomLoader;
        }

        if (guestOs is null)
        {
            return DetectionOff;
        }

        if (guestOs.Id == GuestOsId.None)
        {
            return NotEntered;
        }

        return JoinNameAndVersion(guestOs.DisplayName, guestOs.VersionLabel);
    }

    /// <summary>
    /// Formats the stage half of the row.
    /// </summary>
    /// <param name="stage">The boot stage.</param>
    /// <returns>The display word for <paramref name="stage"/>. No card or media name is included.</returns>
    public static string FormatStage(GuestOsBootStage stage)
    {
        var label = stage switch
        {
            GuestOsBootStage.None => NotEntered,
            GuestOsBootStage.Init => "Init",
            GuestOsBootStage.BootDeviceSelected => "Boot device",
            GuestOsBootStage.BootRom => "Boot ROM",
            GuestOsBootStage.Boot1 => "Boot 1",
            GuestOsBootStage.Loader => "Loader",
            GuestOsBootStage.Ready => "Ready",
            GuestOsBootStage.Unknown => "Unknown",
            GuestOsBootStage.CustomBoot => "Custom boot",
            GuestOsBootStage.NoBootDevice => "No boot device",
            _ => null,
        };

        // Unnamed values are not a boot stage this row knows. Show the identifier rather than a card or a disk.
        return label ?? stage.ToString();
    }

    /// <summary>
    /// Formats one <c>OS: … Stage: …</c> line, and appends the current file when one is loading.
    /// </summary>
    /// <param name="guestOs">The detection result, or <see langword="null"/> when detection is off.</param>
    /// <param name="stage">The boot stage resolved for the same refresh.</param>
    /// <param name="currentLoad">
    /// <see cref="FileLoadProgress.FormatLine"/> for the file being loaded, or <see langword="null"/>
    /// when nothing is in progress. Empty is treated the same as <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The line printed by text-mode <c>statmon</c>. A load is appended as <c>Load: BASIC.SYSTEM 6656/10240 (65%)</c>.
    /// </returns>
    public static string FormatLine(GuestOsInfo? guestOs, GuestOsBootStage stage, string? currentLoad = null)
    {
        var operatingSystem = FormatOperatingSystem(guestOs, stage);
        var bootStage = FormatStage(stage);
        var line = $"OS: {operatingSystem}     Stage: {bootStage}";
        if (string.IsNullOrEmpty(currentLoad))
        {
            return line;
        }

        return $"{line}     Load: {currentLoad}";
    }

    /// <summary>Reads the file currently being loaded.</summary>
    /// <param name="machine">The machine to read. May be <see langword="null"/>.</param>
    /// <returns>The tracker's current row, or <see langword="null"/> when the tracker is off or idle.</returns>
    public static FileLoadProgress? ReadCurrentLoad(IMachine? machine) =>
        machine?.GetComponent<IFileLoadTracker>()?.Current;

    /// <summary>Formats the status window's load value.</summary>
    /// <param name="current">The file being loaded, or <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="FileLoadProgress.FormatLine"/> when <paramref name="current"/> is active;
    /// otherwise <see cref="NotEntered"/>.
    /// </returns>
    public static string FormatLoad(FileLoadProgress? current)
    {
        if (current is not FileLoadProgress progress || !progress.Active)
        {
            return NotEntered;
        }

        return progress.FormatLine();
    }

    /// <summary>
    /// Reads <paramref name="machine"/> and formats the status line.
    /// </summary>
    /// <param name="machine">The machine, or <see langword="null"/> when none is attached.</param>
    /// <param name="stageSource">Optional injected stage. <see langword="null"/> reads the machine.</param>
    /// <returns>
    /// The <c>OS: … Stage: …</c> line, with <c>Load:</c> appended while a file is loading.
    /// A missing machine is reported as detection off.
    /// </returns>
    public static string FormatMachineLine(IMachine? machine, IGuestOsStageSource? stageSource = null)
    {
        if (machine is null)
        {
            return FormatLine(null, GuestOsBootStage.None);
        }

        var guestOs = ReadGuestOs(machine);
        var bootStage = ReadBootStage(machine, stageSource, guestOs);
        var current = ReadCurrentLoad(machine);
        string? loadLine = current is FileLoadProgress progress && progress.Active ? progress.FormatLine() : null;
        return FormatLine(guestOs, bootStage, loadLine);
    }

    /// <summary>
    /// Combines a display name and a version label without repeating a shared prefix.
    /// </summary>
    /// <param name="displayName">Short name such as "ProDOS 8".</param>
    /// <param name="versionLabel">Version text. May already start with <paramref name="displayName"/>, or be empty.</param>
    /// <returns>The operating-system text for the row.</returns>
    private static string JoinNameAndVersion(string displayName, string versionLabel)
    {
        if (versionLabel.Length == 0)
        {
            return displayName;
        }

        if (versionLabel.StartsWith(displayName, StringComparison.Ordinal)
            || displayName.StartsWith(versionLabel, StringComparison.Ordinal))
        {
            return versionLabel.Length >= displayName.Length ? versionLabel : displayName;
        }

        return $"{displayName} {versionLabel}";
    }
}