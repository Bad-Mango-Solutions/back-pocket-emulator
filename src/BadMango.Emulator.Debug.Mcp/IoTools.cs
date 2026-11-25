// <copyright file="IoTools.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.ComponentModel;
using System.Globalization;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Serilog;

/// <summary>
/// MCP tools for the text screen, keyboard, and sandboxed disk images.
/// </summary>
[McpServerToolType]
public sealed class IoTools
{
    private readonly IMachineIoSession io;
    private readonly IDebugContext context;
    private readonly SandboxPathGuard sandbox;
    private readonly DiskFileSystemReader files;
    private readonly IOptions<DebugMcpOptions> options;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IoTools"/> class.
    /// </summary>
    /// <param name="io">The machine I/O session.</param>
    /// <param name="context">The debug context that owns mounted images.</param>
    /// <param name="sandbox">The sandbox that confines host paths.</param>
    /// <param name="files">The shared disk file-system reader.</param>
    /// <param name="options">The host options, including the file-size cap.</param>
    /// <param name="logger">The host logger.</param>
    public IoTools(
        IMachineIoSession io,
        IDebugContext context,
        SandboxPathGuard sandbox,
        DiskFileSystemReader files,
        IOptions<DebugMcpOptions> options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this.io = io;
        this.context = context;
        this.sandbox = sandbox;
        this.files = files;
        this.options = options;
        this.logger = logger.ForContext<IoTools>();
    }

    /// <summary>
    /// Reads the current text screen.
    /// </summary>
    /// <returns>An emudbg envelope whose payload is the text-screen snapshot, or an error envelope.</returns>
    [McpServerTool(Name = "get_screen_text", UseStructuredContent = true)]
    [Description("Get the current text screen as a grid of lines.")]
    public object GetScreenText()
    {
        try
        {
            return DebugEnvelope<ScreenTextSnapshot>.Create(this.io.GetScreenText());
        }
        catch (InvalidOperationException ex)
        {
            return this.Fail("get_screen_text", ex.Message, ex);
        }
    }

    /// <summary>
    /// Queues keyboard input.
    /// </summary>
    /// <param name="text">The text to type. A carriage return is Return.</param>
    /// <returns>An emudbg envelope whose payload is the typing receipt, or an error envelope.</returns>
    [McpServerTool(Name = "type_keys", UseStructuredContent = true)]
    [Description("Type text into the keyboard. A carriage return is Return.")]
    public object TypeKeys(
        [Description("Text to type. A carriage return is Return.")] string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return this.Fail("type_keys", "Text is required.");
        }

        try
        {
            TypeKeysReceipt receipt = this.io.TypeKeys(text);
            return DebugEnvelope<TypeKeysReceipt>.Create(receipt);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("type_keys", ex.Message, ex);
        }
    }

    /// <summary>
    /// Mounts a disk image that stays inside a sandbox root.
    /// </summary>
    /// <param name="path">The image path. <c>..</c>, a path outside the roots, and a symlink that leaves the roots are rejected.</param>
    /// <param name="slot">The peripheral slot. Defaults to 6.</param>
    /// <param name="drive">The 1-based drive. Defaults to 1.</param>
    /// <param name="writeProtect">Whether to open the image read-only. Defaults to <see langword="true"/>.</param>
    /// <returns>An emudbg envelope whose payload is the mount receipt, or an error envelope.</returns>
    [McpServerTool(Name = "mount_disk", UseStructuredContent = true)]
    [Description("Mount a disk image on a drive. writeProtect defaults to true, so a caller must set it false to allow writes. Slot defaults to 6 and drive defaults to 1. A relative path is resolved against the first configured sandbox root.")]
    public object MountDisk(
        [Description("Disk image path. A relative path is resolved against the first configured sandbox root.")] string path,
        [Description("Peripheral slot, 1 through 7. Defaults to 6.")] int slot = 6,
        [Description("Drive number, starting at 1. Defaults to 1.")] int drive = 1,
        [Description("Open the image read-only when true. Defaults to true.")] bool writeProtect = true)
    {
        if (!this.sandbox.TryGetAllowedPath(path, out string allowed))
        {
            return this.Fail("mount_disk", $"Path is outside the configured sandbox roots: {path}");
        }

        try
        {
            this.io.MountDisk(slot, drive, allowed, writeProtect);
            this.logger.Information("Mounted {Path} on slot {Slot} drive {Drive}.", allowed, slot, drive);
            return DebugEnvelope<MountDiskReceipt>.Create(new MountDiskReceipt(slot, drive, allowed, writeProtect));
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return this.Fail("mount_disk", ex.Message, ex);
        }
    }

    /// <summary>
    /// Lists a catalog or directory.
    /// </summary>
    /// <param name="image">An offline image inside a sandbox root. Omit it to use a mounted drive.</param>
    /// <param name="slot">The peripheral slot of a mounted drive.</param>
    /// <param name="drive">The 1-based drive of a mounted drive.</param>
    /// <param name="path">The directory to list. Omit it to list the volume root.</param>
    /// <returns>An emudbg envelope whose payload is the catalog, or an error envelope.</returns>
    [McpServerTool(Name = "disk_list_files", UseStructuredContent = true)]
    [Description("List files on a mounted drive or a sandboxed disk image. A relative image path is resolved against the first configured sandbox root. Display names use the same \\c{X}, \\i{X}, and \\f{X} escaping as disk cat. Reading a dirty read-write drive flushes it to the host image. A write-protected drive is not flushed.")]
    public object DiskListFiles(
        [Description("Offline image path. A relative path is resolved against the first configured sandbox root. Omit to use a mounted drive.")] string? image = null,
        [Description("Peripheral slot of a mounted drive. Required with drive when image is omitted.")] int? slot = null,
        [Description("1-based drive of a mounted drive. Required with slot when image is omitted.")] int? drive = null,
        [Description("Directory to list. Omit, or pass / , to list the volume root.")] string? path = null)
    {
        if (!this.TryAuthorize(image, out string? allowed, out string? authError))
        {
            return this.Fail("disk_list_files", authError!);
        }

        if (!TrySlotDrive(slot, drive, out string? slotDrive, out string? slotError))
        {
            return this.Fail("disk_list_files", slotError!);
        }

        if (!this.files.TryList(this.context, allowed, slotDrive, path, out DiskCatalogPayload? catalog, out string? error) || catalog is null)
        {
            return this.Fail("disk_list_files", error ?? "Cannot read the file system.");
        }

        return DebugEnvelope<DiskCatalogPayload>.Create(catalog);
    }

    /// <summary>
    /// Reads one file and returns either host text or base64.
    /// </summary>
    /// <param name="filename">The volume-relative file name.</param>
    /// <param name="image">An offline image inside a sandbox root. Omit it to use a mounted drive.</param>
    /// <param name="slot">The peripheral slot of a mounted drive.</param>
    /// <param name="drive">The 1-based drive of a mounted drive.</param>
    /// <param name="raw">Whether to return on-disk bytes. The default is the logical view.</param>
    /// <returns>
    /// An emudbg envelope whose payload has one encoding, or an error envelope when the file is missing
    /// or larger than <see cref="DebugMcpOptions.MaxDiskFileBytes"/>. The tool does not write a host file.
    /// </returns>
    [McpServerTool(Name = "disk_read_file", UseStructuredContent = true)]
    [Description("Read one file from a mounted drive or a sandboxed disk image. A DOS text, ProDOS TXT, or Pascal text file in the logical view returns text; DOS text stops at the first $00. Every other file, and raw=true, returns base64. The encoding field is \"text\" or \"base64\". A file larger than DebugMcp:MaxDiskFileBytes (default 1 MiB) returns an error. A relative image path is resolved against the first configured sandbox root. Reading a dirty read-write drive flushes it to the host image. A write-protected drive is not flushed. Does not write a new host file.")]
    public object DiskReadFile(
        [Description("Volume-relative file name. DOS lookup is exact, then a unique case-insensitive match.")] string filename,
        [Description("Offline image path. A relative path is resolved against the first configured sandbox root. Omit to use a mounted drive.")] string? image = null,
        [Description("Peripheral slot of a mounted drive. Required with drive when image is omitted.")] int? slot = null,
        [Description("1-based drive of a mounted drive. Required with slot when image is omitted.")] int? drive = null,
        [Description("Return on-disk bytes, including a DOS A/I/B header, when true. The default is the logical view.")] bool raw = false)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return this.Fail("disk_read_file", "A file name is required.");
        }

        if (!this.TryAuthorize(image, out string? allowed, out string? authError))
        {
            return this.Fail("disk_read_file", authError!);
        }

        if (!TrySlotDrive(slot, drive, out string? slotDrive, out string? slotError))
        {
            return this.Fail("disk_read_file", slotError!);
        }

        try
        {
            if (!this.files.TryRead(this.context, allowed, slotDrive, filename, raw, out DiskVolumeFile? file, out string? error) || file is null)
            {
                return this.Fail("disk_read_file", error ?? "Cannot read the file system.");
            }

            ReadOnlyMemory<byte> bytes = DiskFileSystemReader.PresentedBytes(file, raw);
            int cap = this.options.Value.MaxDiskFileBytes;
            if (bytes.Length > cap)
            {
                return this.Fail("disk_read_file", $"File is {bytes.Length} bytes, which exceeds the {cap} byte limit.");
            }

            bool asText = !raw && file.LogicalText;
            var receipt = new DiskReadFileReceipt(
                file.Source,
                file.Location,
                file.Kind,
                file.VolumeName,
                file.Path,
                file.View,
                bytes.Length,
                asText ? "text" : "base64",
                asText ? DiskFileSystemReader.ToHostText(bytes.Span) : null,
                asText ? null : Convert.ToBase64String(bytes.Span));
            return DebugEnvelope<DiskReadFileReceipt>.Create(receipt);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException)
        {
            return this.Fail("disk_read_file", ex.Message, ex);
        }
    }

    private static bool TrySlotDrive(int? slot, int? drive, out string? slotDrive, out string? error)
    {
        slotDrive = null;
        error = null;
        if (slot is null && drive is null)
        {
            return true;
        }

        if (slot is null || drive is null)
        {
            error = "Slot and drive are both required.";
            return false;
        }

        slotDrive = string.Concat(
            slot.Value.ToString(CultureInfo.InvariantCulture),
            ":",
            drive.Value.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private bool TryAuthorize(string? image, out string? allowed, out string? error)
    {
        allowed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(image))
        {
            return true;
        }

        if (!this.sandbox.TryGetAllowedPath(image, out string allowedPath))
        {
            error = $"Path is outside the configured sandbox roots: {image}";
            return false;
        }

        allowed = allowedPath;
        return true;
    }

    private CallToolResult Fail(string tool, string message, Exception? exception = null)
    {
        if (exception is null)
        {
            this.logger.Warning("{Tool} failed: {Message}", tool, message);
        }
        else
        {
            this.logger.Warning(exception, "{Tool} failed: {Message}", tool, message);
        }

        return DebugMcpResults.Error(message);
    }
}