// <copyright file="DiskListCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using System.Globalization;
using System.Text;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;

/// <summary>
/// Lists every installed disk controller and its drives' mount state (PRD §6.5 FR-DC3).
/// </summary>
/// <remarks>
/// <para>
/// Reads the live listing from <see cref="IMachineIoSession.ListDisks"/> so the console and the
/// session API share one snapshot. One unreadable drive is reported on that drive's row and does
/// not fail the rest of the command. JSON is an <c>emudbg/v1</c> envelope from
/// <see cref="IDebugOutputRenderer"/>.
/// </para>
/// <para>
/// Reports image paths exactly as the controller records them — typically the absolute resolved
/// path supplied at mount time.
/// </para>
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskListCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IMachineIoSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskListCommand"/> class.
    /// </summary>
    public DiskListCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskListCommand"/> class that lists disks
    /// through an existing session.
    /// </summary>
    /// <param name="session">The session to query, or <see langword="null"/> to open one from the command context.</param>
    public DiskListCommand(IMachineIoSession? session)
        : this(session, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskListCommand"/> class that lists disks
    /// through an existing session and renderer.
    /// </summary>
    /// <param name="session">The session to query, or <see langword="null"/> to open one from the command context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text unless <c>--json</c> is passed.</param>
    public DiskListCommand(IMachineIoSession? session, IDebugOutputRenderer? renderer)
        : base("disk-list", "List installed disk controllers and per-drive mount state")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["disklist"];

    /// <inheritdoc/>
    public override string Usage => "disk-list [--json]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Lists every installed disk controller (Disk II today; SmartPort 3.5\" later), one " +
        "row per drive, showing the slot, drive number, mount state, write-protect flag, " +
        "the path of the mounted image, the motor / quarter-track position, and the " +
        "medium geometry where available. A drive that cannot be read is reported on its " +
        "own row. Requires a running machine.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--json", null, "flag", "Emit machine-readable JSON output.", null),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk-list",
        "disk list",
        "disk list --json",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "None. Snapshot is read-only.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["disk", "disk-insert", "disk-eject", "disk-flush", "devicemap"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        bool emitJson = false;
        if (args.Length > 1)
        {
            return CommandResult.Error("Usage: disk-list [--json]");
        }

        if (args.Length == 1)
        {
            if (!args[0].Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                return CommandResult.Error($"Unknown option: '{args[0]}'.");
            }

            emitJson = true;
        }

        if (!DiskRuntimeHelpers.TryGetSlotManager(context, out _, out var error))
        {
            return CommandResult.Error(error!);
        }

        IMachineIoSession io = this.session
            ?? (context is DebugContext debug && debug.GetSharedSession() is IMachineIoSession shared
                ? shared
                : throw new InvalidOperationException("No machine I/O session is available."));
        IReadOnlyList<DiskControllerSnapshot> controllers = io.ListDisks();
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("Disk controllers:");

        for (int i = 0; i < controllers.Count; i++)
        {
            DiskControllerSnapshot controller = controllers[i];
            text.AppendLine();
            text.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  Slot {0}: {1} ({2} drive{3})",
                controller.Slot,
                controller.Name,
                controller.DriveCount,
                controller.DriveCount == 1 ? string.Empty : "s"));

            for (int d = 0; d < controller.Drives.Count; d++)
            {
                DiskDriveSnapshot snap = controller.Drives[d];
                if (snap.Error is not null)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    Drive {0}: unreadable ({1})",
                        snap.Drive,
                        snap.Error));
                    continue;
                }

                if (!snap.HasMedia)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "    Drive {0}: empty (selected={1}, motor={2}, quarter-track={3})",
                        snap.Drive,
                        YesNo(snap.Selected),
                        YesNo(snap.MotorOn),
                        snap.QuarterTrack));
                    continue;
                }

                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    Drive {0}: mounted '{1}'",
                    snap.Drive,
                    snap.MountedImagePath ?? "<unknown>"));
                text.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "      write-protect={0} selected={1} motor={2} quarter-track={3}",
                    YesNo(snap.WriteProtect),
                    YesNo(snap.Selected),
                    YesNo(snap.MotorOn),
                    snap.QuarterTrack));
                if (snap.Geometry is { } geom)
                {
                    text.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "      geometry: {0} tracks × {1} sectors × {2} bytes ({3}, {4} bytes total)",
                        geom.TrackCount,
                        geom.SectorsPerTrack,
                        geom.BytesPerSector,
                        geom.SectorOrder,
                        geom.TotalBytes));
                }
            }
        }

        if (controllers.Count == 0)
        {
            text.AppendLine();
            text.AppendLine("  No disk controllers installed.");
        }

        text.AppendLine();
        this.renderer.WriteDiskControllers(context.Output, controllers, text.ToString(), emitJson || this.renderer.JsonOutput);
        return CommandResult.Ok();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}