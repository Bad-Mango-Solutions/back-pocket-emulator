// <copyright file="GuestOsInspection.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Globalization;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Reads the guest OS and the boot log for the console and the MCP tools.
/// </summary>
public static class GuestOsInspection
{
    /// <summary>
    /// Reads the current guest OS. A live monitor publishes from the detector when no milestone was seen.
    /// </summary>
    /// <param name="machine">The machine to read. May be <see langword="null"/>.</param>
    /// <returns>The snapshot. Detection that was never installed reports itself as off.</returns>
    public static GuestOsSnapshot ReadGuestOs(IMachine? machine)
    {
        if (machine is null)
        {
            return Off("No machine is attached.");
        }

        var monitor = machine.GetComponent<BootSequenceMonitor>();
        if (monitor is not null)
        {
            var detected = monitor.PublishFromDetector();
            return FromInfo(detected, monitor.Stage);
        }

        if (machine.GetComponent<IGuestOsDetector>() is IGuestOsDetector detector)
        {
            return FromInfo(detector.Detect(), GuestOsBootStage.None);
        }

        return Off("Guest OS detection is off.");
    }

    /// <summary>Reads the boot milestone log without changing the stage.</summary>
    /// <param name="machine">The machine to read. May be <see langword="null"/>.</param>
    /// <returns>The log. An absent monitor returns an empty log at stage <see cref="GuestOsBootStage.None"/>.</returns>
    public static BootLogSnapshot ReadBootLog(IMachine? machine)
    {
        var monitor = machine?.GetComponent<IBootSequenceMonitor>();
        if (monitor is null)
        {
            return new BootLogSnapshot(GuestOsBootStage.None.ToString(), 0, []);
        }

        var reentries = monitor is BootSequenceMonitor concrete ? concrete.BootSectorReentries : 0;
        var lines = new BootLogLine[monitor.Milestones.Count];
        for (var i = 0; i < monitor.Milestones.Count; i++)
        {
            var milestone = monitor.Milestones[i];
            lines[i] = new BootLogLine(
                milestone.At.Value,
                milestone.Pc.ToString("X4", CultureInfo.InvariantCulture),
                milestone.Kind.ToString(),
                milestone.Os?.Value,
                milestone.Detail);
        }

        return new BootLogSnapshot(monitor.Stage.ToString(), reentries, lines);
    }

    /// <summary>Formats the guest-OS snapshot as console text.</summary>
    /// <param name="snapshot">The snapshot to format.</param>
    /// <returns>One block of text, without a trailing extra blank line.</returns>
    public static string FormatGuestOs(GuestOsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return string.Join(
            Environment.NewLine,
            $"Guest OS: {snapshot.DisplayName} ({snapshot.Id})",
            $"Version: {snapshot.VersionLabel}",
            $"Stage: {snapshot.Stage}",
            $"Reason: {snapshot.Reason}");
    }

    /// <summary>Formats the boot log as console text.</summary>
    /// <param name="log">The log to format.</param>
    /// <returns>The stage, the re-entry count, and one line per milestone.</returns>
    public static string FormatBootLog(BootLogSnapshot log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var lines = new List<string>
        {
            $"Stage: {log.Stage}",
            $"Boot sector re-entries: {log.BootSectorReentries}",
        };
        foreach (var milestone in log.Milestones)
        {
            var os = milestone.Os is null ? string.Empty : $" {milestone.Os}";
            lines.Add($"{milestone.Cycle} ${milestone.Pc} {milestone.Kind}{os} {milestone.Detail}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static GuestOsSnapshot FromInfo(GuestOsInfo info, GuestOsBootStage stage) =>
        new(info.Id.Value, info.DisplayName, info.VersionLabel, info.Reason, stage.ToString());

    private static GuestOsSnapshot Off(string reason) =>
        new(GuestOsId.None.Value, "None", string.Empty, reason, GuestOsBootStage.None.ToString());
}