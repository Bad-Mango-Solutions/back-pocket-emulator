// <copyright file="DiskCatCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Lists a DOS 3.3 catalog, a ProDOS directory, or a Pascal directory.
/// </summary>
/// <remarks>
/// <c>disk catalog</c> is the same command. There is no <c>disk ls</c> command.
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskCatCommand : CommandHandlerBase, ICommandHelp
{
    private readonly DiskFileSystemReader files;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCatCommand"/> class.
    /// </summary>
    /// <param name="detector">The shared file-system detector. Not <see langword="null"/>.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text unless <c>--json</c> is passed.</param>
    /// <param name="logger">Logger for media failures. <see langword="null"/> discards those messages.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="detector"/> is <see langword="null"/>.</exception>
    public DiskCatCommand(IAppleFileSystemDetector detector, IDebugOutputRenderer? renderer, ILogger? logger)
        : base("disk-cat", "List a catalog or directory on a mounted drive or an offline image")
    {
        ArgumentNullException.ThrowIfNull(detector);
        this.files = new DiskFileSystemReader(detector, logger ?? Serilog.Core.Logger.None);
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["disk-catalog", "diskcatalog", "diskcat"];

    /// <inheritdoc/>
    public override string Usage => "disk-cat <slot:drive> [path] | disk-cat --image <path> [path] [--json]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Lists a volume root when the path is omitted, empty, or '/'. A ProDOS subdirectory path " +
        "lists that directory. DOS 3.3 and Pascal are flat. Inverse and flashing catalog characters " +
        "are escaped so they stay visible on a host terminal. JSON is an emudbg/v1 envelope. " +
        "'disk catalog' runs this same command.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--image", null, "path", "Read an offline image instead of a mounted drive.", null),
        new("--json", null, "flag", "Emit an emudbg/v1 JSON envelope.", null),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk cat 6:1",
        "disk catalog 6:1",
        "disk cat --image volume.hdv SUB",
        "disk-cat --image game.dsk --json",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "None. The image is opened read-only and is not mounted.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["disk", "disk-fsinfo", "disk-read"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        if (!DiskFileSystemSupport.TryParse(args, DiskFileSystemSupport.Verb.Catalog, out var request, out var error) || request is null)
        {
            return CommandResult.Error(error ?? "Usage: disk cat <slot:drive> [path]");
        }

        if (!this.files.TryList(context, request.Image, request.SlotDrive, request.Path, out DiskCatalogPayload? payload, out error) || payload is null)
        {
            return CommandResult.Error(error ?? "Cannot read the file system.");
        }

        var text = DiskFileSystemSupport.FormatCatalog(payload);
        this.renderer.WriteDiskCatalog(context.Output, payload, text, request.Json || this.renderer.JsonOutput);
        return CommandResult.Ok();
    }
}