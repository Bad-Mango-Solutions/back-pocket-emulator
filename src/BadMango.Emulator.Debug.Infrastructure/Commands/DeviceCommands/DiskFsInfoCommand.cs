// <copyright file="DiskFsInfoCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using BadMango.Emulator.Devices;
using BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Reports which read-only file system a mounted drive or an offline image contains.
/// </summary>
[DeviceDebugCommand]
public sealed class DiskFsInfoCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IAppleFileSystemDetector detector;
    private readonly IDebugOutputRenderer renderer;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskFsInfoCommand"/> class.
    /// </summary>
    /// <param name="fileSystemDetector">The shared file-system detector. Not <see langword="null"/>.</param>
    /// <param name="outputRenderer">The output renderer. <see langword="null"/> writes text unless <c>--json</c> is passed.</param>
    /// <param name="diagnosticLogger">Logger for media failures. <see langword="null"/> discards those messages.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="fileSystemDetector"/> is <see langword="null"/>.</exception>
    public DiskFsInfoCommand(IAppleFileSystemDetector fileSystemDetector, IDebugOutputRenderer? outputRenderer, ILogger? diagnosticLogger)
        : base("disk-fsinfo", "Report the file system on a mounted drive or an offline image")
    {
        ArgumentNullException.ThrowIfNull(fileSystemDetector);
        this.detector = fileSystemDetector;
        this.renderer = outputRenderer ?? DebugOutputRenderer.PlainText;
        this.logger = diagnosticLogger ?? Serilog.Core.Logger.None;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["diskfsinfo"];

    /// <inheritdoc/>
    public override string Usage => "disk-fsinfo <slot:drive> | disk-fsinfo --image <path> [--json]";

    /// <inheritdoc/>
    public string Synopsis => this.Usage;

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Recognizes a DOS 3.3, ProDOS, or Pascal volume and prints the volume name plus every " +
        "probe that ran and why it was accepted or rejected. The source is a mounted slot:drive " +
        "or --image with no machine. Names that contain inverse or flashing catalog characters " +
        "are escaped. JSON is an emudbg/v1 envelope.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--image", null, "path", "Read an offline image instead of a mounted drive.", null),
        new("--json", null, "flag", "Emit an emudbg/v1 JSON envelope.", null),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "disk fsinfo 6:1",
        "disk fsinfo --image game.dsk --json",
        "disk-fsinfo --image volume.hdv",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "None. The image is opened read-only and is not mounted.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["disk", "disk-cat", "disk-read", "disk-info"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        if (!DiskFileSystemSupport.TryParse(args, DiskFileSystemSupport.Verb.Info, out var request, out var error) || request is null)
        {
            return CommandResult.Error(error ?? "Usage: disk fsinfo <slot:drive> | disk fsinfo --image <path>");
        }

        if (!DiskFileSystemSupport.TryOpen(context, this.detector, this.logger, request, out var session, out error) || session is null)
        {
            return CommandResult.Error(error ?? "Cannot read the file system.");
        }

        using (session)
        {
            var payload = DiskFileSystemSupport.DescribeInfo(session);
            var text = DiskFileSystemSupport.FormatInfo(payload);
            this.renderer.WriteDiskFileSystemInfo(context.Output, payload, text, request.Json || this.renderer.JsonOutput);
            return CommandResult.Ok();
        }
    }
}