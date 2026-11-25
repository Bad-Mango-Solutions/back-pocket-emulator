// <copyright file="DiskReadCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Prints one file from a mounted drive or an offline image.
/// </summary>
/// <remarks>
/// This is the <c>disk read</c> subcommand. It does not replace the top-level memory <c>read</c> command.
/// The default view is <see cref="FileReadView.Logical"/>. Logical DOS 3.3 text stops at the first <c>$00</c>.
/// <c>--raw</c> keeps last-sector padding.
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskReadCommand : CommandHandlerBase, ICommandHelp
{
    private readonly DiskFileSystemReader files;
    private readonly IDebugOutputRenderer renderer;
    private readonly ILogger logger;
    private readonly IDebugExportRoot? exportRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskReadCommand"/> class.
    /// </summary>
    /// <param name="detector">The shared file-system detector. Not <see langword="null"/>.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text unless <c>--json</c> is passed.</param>
    /// <param name="logger">Logger for media and export failures. <see langword="null"/> discards those messages.</param>
    /// <param name="exportRoot">The directory <c>--to</c> may write into. <see langword="null"/> refuses <c>--to</c>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="detector"/> is <see langword="null"/>.</exception>
    public DiskReadCommand(IAppleFileSystemDetector detector, IDebugOutputRenderer? renderer, ILogger? logger, IDebugExportRoot? exportRoot)
        : base("disk-read", "Print a file from a mounted drive or an offline image")
    {
        ArgumentNullException.ThrowIfNull(detector);
        this.logger = logger ?? Serilog.Core.Logger.None;
        this.files = new DiskFileSystemReader(detector, this.logger);
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
        this.exportRoot = exportRoot;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["diskread"];

    /// <inheritdoc/>
    public override string Usage => "disk-read <slot:drive> <file> [--hex] [--raw|--logical] [--to <name>] [--json]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Prints one file. The default view is logical: DOS A/I/B headers are removed and the payload " +
        "is trimmed to the header length word, and logical DOS text stops at the first $00. " +
        "--raw keeps the on-disk bytes, including that header, last-sector padding, and " +
        "sparse holes as zeros. ProDOS and Pascal logical reads match raw reads. --hex prints a hex dump. " +
        "--to writes the selected view under the configured output root and refuses any other host path. " +
        "JSON is an emudbg/v1 envelope. This command does not read machine memory.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--image", null, "path", "Read an offline image instead of a mounted drive.", null),
        new("--hex", null, "flag", "Print a hex dump instead of text.", null),
        new("--raw", null, "flag", "Print the on-disk bytes, including a DOS A/I/B header and last-sector padding.", null),
        new("--logical", null, "flag", "Print the logical view. DOS text stops at the first $00. This is the default.", "logical"),
        new("--to", null, "path", "Write the selected view under the configured output root.", null),
        new("--json", null, "flag", "Emit an emudbg/v1 JSON envelope.", null),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk read 6:1 HELLO",
        "disk read --image game.dsk NOTE --hex --raw",
        "disk read 6:1 CODE --to code.bin",
        "disk-read --image volume.po SUB/NOTE",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "None, except --to writes the selected file bytes inside the configured output root.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["disk", "disk-cat", "disk-fsinfo", "read"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        if (!DiskFileSystemSupport.TryParse(args, DiskFileSystemSupport.Verb.Read, out var request, out var error) || request is null)
        {
            return CommandResult.Error(error ?? "Usage: disk read <slot:drive> <file>");
        }

        string? savedTo = null;
        string? exportPath = null;
        if (request.ExportTo is not null
            && !DiskFileSystemSupport.TryResolveExport(this.exportRoot?.Directory, request.ExportTo, out exportPath, out error))
        {
            return CommandResult.Error(error ?? "--to is outside the output root.");
        }

        if (!this.files.TryRead(context, request.Image, request.SlotDrive, request.Path!, request.Raw, out DiskVolumeFile? file, out error) || file is null)
        {
            return CommandResult.Error(error ?? "Cannot read the file system.");
        }

        var bytes = DiskFileSystemReader.PresentedBytes(file, request.Raw);
        if (exportPath is not null
            && !DiskFileSystemSupport.TryWriteExport(this.exportRoot?.Directory, exportPath, bytes.Span, this.logger, out error))
        {
            return CommandResult.Error(error ?? "Cannot write the export file.");
        }

        if (request.ExportTo is not null)
        {
            savedTo = request.ExportTo;
        }

        var payload = new DiskFilePayload(
            file.Source,
            file.Location,
            file.Kind,
            file.VolumeName,
            file.Path,
            file.View,
            request.Hex ? "hex" : "text",
            bytes.Length,
            Convert.ToHexStringLower(bytes.Span),
            request.Hex ? null : DiskFileSystemSupport.ToText(bytes.Span),
            savedTo);
        var text = DiskFileSystemSupport.FormatFile(payload, request.Hex, bytes);
        this.renderer.WriteDiskFile(context.Output, payload, text, request.Json || this.renderer.JsonOutput);
        return CommandResult.Ok();
    }
}