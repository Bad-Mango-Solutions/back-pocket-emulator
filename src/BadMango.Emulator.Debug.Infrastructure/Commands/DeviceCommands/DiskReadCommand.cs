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
/// The default view is <see cref="FileReadView.Logical"/>.
/// </remarks>
[DeviceDebugCommand]
public sealed class DiskReadCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IAppleFileSystemDetector detector;
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
        this.detector = detector;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
        this.logger = logger ?? Serilog.Core.Logger.None;
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
        "is trimmed to the header length word. --raw keeps the on-disk bytes, including that header and " +
        "sparse holes as zeros. ProDOS and Pascal logical reads match raw reads. --hex prints a hex dump. " +
        "--to writes the selected view under the configured output root and refuses any other host path. " +
        "JSON is an emudbg/v1 envelope. This command does not read machine memory.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--image", null, "path", "Read an offline image instead of a mounted drive.", null),
        new("--hex", null, "flag", "Print a hex dump instead of text.", null),
        new("--raw", null, "flag", "Print the on-disk bytes, including a DOS A/I/B header.", null),
        new("--logical", null, "flag", "Print the DOS logical view. This is the default.", "logical"),
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

        if (!DiskFileSystemSupport.TryOpen(context, this.detector, this.logger, request, out var session, out error) || session is null)
        {
            return CommandResult.Error(error ?? "Cannot read the file system.");
        }

        using (session)
        {
            var fileSystem = session.Detection.FileSystem;
            if (fileSystem is null)
            {
                return CommandResult.Error(DiskFileSystemSupport.UnrecognizedMessage(session.Detection));
            }

            FileReadResult read;
            try
            {
                read = fileSystem.TryReadFile(request.Path!, request.View);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or IOException
                                       or ObjectDisposedException)
            {
                this.logger.Warning(ex, "File read failed for {Location} {Path}", session.Location, request.Path);
                return CommandResult.Error($"Cannot read the file system: {ex.Message}");
            }

            if (!read.Succeeded)
            {
                return CommandResult.Error($"Cannot read '{request.Path}': {read.Reason}");
            }

            if (exportPath is not null
                && !DiskFileSystemSupport.TryWriteExport(this.exportRoot?.Directory, exportPath, read.Data.Span, this.logger, out error))
            {
                return CommandResult.Error(error ?? "Cannot write the export file.");
            }

            if (request.ExportTo is not null)
            {
                savedTo = request.ExportTo;
            }

            var payload = new DiskFilePayload(
                session.Source,
                session.Location,
                fileSystem.Kind.ToString(),
                AppleCatalogName.EscapePlain(fileSystem.VolumeName),
                request.Path!,
                request.Raw ? "raw" : "logical",
                request.Hex ? "hex" : "text",
                read.Data.Length,
                Convert.ToHexStringLower(read.Data.Span),
                request.Hex ? null : DiskFileSystemSupport.ToText(read.Data.Span),
                savedTo);
            var text = DiskFileSystemSupport.FormatFile(payload, request.Hex, read.Data);
            this.renderer.WriteDiskFile(context.Output, payload, text, request.Json || this.renderer.JsonOutput);
            return CommandResult.Ok();
        }
    }
}