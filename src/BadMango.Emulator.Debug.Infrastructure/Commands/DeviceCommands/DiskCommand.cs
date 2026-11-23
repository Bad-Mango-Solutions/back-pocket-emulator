// <copyright file="DiskCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;

/// <summary>
/// Top-level <c>disk</c> command that delegates to the offline (<c>create</c>, <c>info</c>),
/// runtime (<c>list</c>, <c>insert</c>, <c>eject</c>, <c>flush</c>, <c>dump-track</c>,
/// <c>read-sector</c>), and file-system (<c>fsinfo</c>, <c>cat</c>, <c>read</c>) subcommand handlers.
/// </summary>
/// <remarks>
/// <para>
/// Each subcommand is also auto-registered as a standalone command handler
/// (<c>disk-create</c>, <c>disk-info</c>, <c>disk-list</c>, <c>disk-insert</c>,
/// <c>disk-eject</c>, <c>disk-flush</c>, <c>disk-dump-track</c>, <c>disk-read-sector</c>,
/// <c>disk-fsinfo</c>, <c>disk-cat</c>, <c>disk-read</c>)
/// by the <c>DeviceDebugCommandsModule</c>. This parent exists so that the documented
/// <c>disk &lt;subcommand&gt; ...</c> CLI syntax works out of the box.
/// <c>fsinfo</c>, <c>cat</c> / <c>catalog</c>, and <c>read</c> are resolved from that
/// registry. This class does not construct them.
/// </para>
/// <para>
/// <c>create</c> and <c>info</c> do not require a running machine and resolve only the
/// <see cref="Storage.Formats.DiskImageFactory"/> and <see cref="IDebugPathResolver"/>
/// from the supplied context. <c>list</c>, <c>insert</c>, <c>eject</c>, <c>flush</c>,
/// <c>dump-track</c>, and <c>read-sector</c> operate on the live
/// <see cref="Bus.Interfaces.ISlotManager"/> exposed via
/// <see cref="IDebugContext.Machine"/> and therefore require a running machine.
/// </para>
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskCommand : CommandHandlerBase, ICommandHelp
{
    private readonly DiskCreateCommand createCommand = new();
    private readonly DiskInfoCommand infoCommand = new();
    private readonly DiskListCommand listCommand;
    private readonly DiskInsertCommand insertCommand = new();
    private readonly DiskEjectCommand ejectCommand = new();
    private readonly DiskFlushCommand flushCommand = new();
    private readonly DiskDumpTrackCommand dumpTrackCommand = new();
    private readonly DiskReadSectorCommand readSectorCommand = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCommand"/> class.
    /// </summary>
    public DiskCommand()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCommand"/> class that lists disks
    /// through an existing session.
    /// </summary>
    /// <param name="session">The session used by <c>disk list</c>, or <see langword="null"/> to open one from the command context.</param>
    public DiskCommand(IMachineIoSession? session)
        : base("disk", "Author, inspect, mount, and read disk images (create / info / list / insert / eject / flush / dump-track / read-sector / fsinfo / cat / read)")
    {
        this.listCommand = new DiskListCommand(session);
    }

    /// <inheritdoc/>
    public override string Usage => "disk <create|info|list|insert|eject|flush|dump-track|read-sector|fsinfo|cat|read> [args]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Authors, inspects, and live-mounts disk images using the same DiskImageFactory " +
        "that runtime controllers use, so authored images round-trip through the same code " +
        "path. Use 'disk create' to write a new fixture image, 'disk info' to report the " +
        "format/geometry/container metadata of an existing image without mounting it, " +
        "'disk list' to print every installed controller and per-drive mount state, " +
        "'disk insert' / 'disk eject' / 'disk flush' to swap removable media at runtime, " +
        "and 'disk dump-track' / 'disk read-sector' to inspect the raw nibble stream and " +
        "decoded sector contents of a mounted drive for diagnostic purposes. " +
        "'disk fsinfo', 'disk cat' (alias 'disk catalog'), and 'disk read' list and read " +
        "DOS 3.3, ProDOS, and Pascal files on a mounted drive or an offline --image. " +
        "'disk read' does not replace the memory 'read' command.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk create blank.dsk",
        "disk create blank.po --format prodos --volume-name BLANK",
        "disk create boot.dsk --format dos33 --bootable master.dsk",
        "disk create huge.hdv --size 32M --format prodos --volume-name BIG",
        "disk info game.2mg",
        "disk list",
        "disk insert 6:1 game.dsk",
        "disk insert 6:2 library://disks/utilities.dsk --write-protect",
        "disk eject 6:1",
        "disk flush 6:2",
        "disk dump-track 6:1 --track 0",
        "disk read-sector 6:1 0 0",
        "disk fsinfo 6:1",
        "disk fsinfo --image game.dsk",
        "disk cat 6:1",
        "disk catalog --image volume.hdv SUB",
        "disk read 6:1 HELLO",
        "disk read --image game.dsk NOTE --hex --raw",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "'disk create' writes a new file at the supplied path (or refuses to overwrite an " +
        "existing file). 'disk info', 'disk list', 'disk dump-track', and 'disk read-sector' " +
        "are read-only. 'disk fsinfo' and 'disk cat' are read-only. 'disk read' is read-only " +
        "except for --to, which writes inside the configured output root. " +
        "'disk insert' opens an image and mounts it on the targeted controller. " +
        "'disk eject' and 'disk flush' write any dirty cached tracks back to the underlying file.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } =
        ["disk-create", "disk-info", "disk-list", "disk-insert", "disk-eject", "disk-flush", "disk-dump-track", "disk-read-sector", "disk-fsinfo", "disk-cat", "disk-read"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return CommandResult.Error(
                "Usage: disk <create|info|list|insert|eject|flush|dump-track|read-sector|fsinfo|cat|read> [args]. " +
                "Try 'help disk' for details.");
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Length > 1 ? args[1..] : [];

        return subcommand switch
        {
            "create" => this.createCommand.Execute(context, subArgs),
            "info" => this.infoCommand.Execute(context, subArgs),
            "list" => this.listCommand.Execute(context, subArgs),
            "insert" => this.insertCommand.Execute(context, subArgs),
            "eject" => this.ejectCommand.Execute(context, subArgs),
            "flush" => this.flushCommand.Execute(context, subArgs),
            "dump-track" or "dumptrack" => this.dumpTrackCommand.Execute(context, subArgs),
            "read-sector" or "readsector" => this.readSectorCommand.Execute(context, subArgs),
            "fsinfo" => ExecuteRegistered(context, "disk-fsinfo", subArgs),
            "cat" or "catalog" => ExecuteRegistered(context, "disk-cat", subArgs),
            "read" => ExecuteRegistered(context, "disk-read", subArgs),
            _ => CommandResult.Error(
                $"Unknown 'disk' subcommand: '{subcommand}'. " +
                "Use 'create', 'info', 'list', 'insert', 'eject', 'flush', 'dump-track', 'read-sector', 'fsinfo', 'cat', or 'read'."),
        };
    }

    private static CommandResult ExecuteRegistered(ICommandContext context, string name, string[] args)
    {
        if (!context.Dispatcher.TryGetHandler(name, out var handler) || handler is null)
        {
            return CommandResult.Error(
                $"'{name}' is not registered. File-system disk commands are resolved from dependency injection.");
        }

        return handler.Execute(context, args);
    }
}