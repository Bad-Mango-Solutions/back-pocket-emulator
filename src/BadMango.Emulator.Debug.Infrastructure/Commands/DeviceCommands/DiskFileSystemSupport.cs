// <copyright file="DiskFileSystemSupport.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;

using System.Globalization;
using System.Text;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;

using Serilog;

/// <summary>
/// Shared parsing, image opening, and text formatting for the read-only disk file-system commands.
/// </summary>
internal static class DiskFileSystemSupport
{
    /// <summary>Which file-system command is being parsed.</summary>
    internal enum Verb
    {
        /// <summary><c>disk fsinfo</c>.</summary>
        Info,

        /// <summary><c>disk cat</c>.</summary>
        Catalog,

        /// <summary><c>disk read</c>.</summary>
        Read,
    }

    /// <summary>Parses one file-system command line.</summary>
    /// <param name="args">The arguments after the subcommand.</param>
    /// <param name="verb">Which command is being parsed.</param>
    /// <param name="request">The parsed request when the method returns <see langword="true"/>.</param>
    /// <param name="error">The usage error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="args"/> is valid for <paramref name="verb"/>.</returns>
    internal static bool TryParse(string[] args, Verb verb, out ParsedRequest? request, out string? error)
    {
        string? image = null;
        string? exportTo = null;
        bool json = false;
        bool hex = false;
        bool raw = false;
        bool logical = false;
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            if (arg.Equals("--hex", StringComparison.OrdinalIgnoreCase))
            {
                hex = true;
                continue;
            }

            if (arg.Equals("--raw", StringComparison.OrdinalIgnoreCase))
            {
                raw = true;
                continue;
            }

            if (arg.Equals("--logical", StringComparison.OrdinalIgnoreCase))
            {
                logical = true;
                continue;
            }

            if (TryMatchValue(arg, "--image", out var imageInline))
            {
                if (!TryTake(args, ref i, "--image", imageInline, out var value, out error))
                {
                    request = null;
                    return false;
                }

                if (image is not null)
                {
                    request = null;
                    error = "--image was specified more than once.";
                    return false;
                }

                image = value;
                continue;
            }

            if (TryMatchValue(arg, "--to", out var exportInline))
            {
                if (!TryTake(args, ref i, "--to", exportInline, out var value, out error))
                {
                    request = null;
                    return false;
                }

                if (exportTo is not null)
                {
                    request = null;
                    error = "--to was specified more than once.";
                    return false;
                }

                exportTo = value;
                continue;
            }

            if (arg.StartsWith('-'))
            {
                request = null;
                error = $"Unknown option: '{arg}'.";
                return false;
            }

            positionals.Add(arg);
        }

        if (verb != Verb.Read && (hex || raw || logical || exportTo is not null))
        {
            request = null;
            error = verb == Verb.Info
                ? "disk fsinfo accepts only a drive or --image, plus --json."
                : "disk cat accepts only a drive or --image, an optional directory, and --json.";
            return false;
        }

        if (raw && logical)
        {
            request = null;
            error = "Specify either --raw or --logical.";
            return false;
        }

        if (!TryBindSource(verb, image, positionals, out var slotDrive, out var path, out error))
        {
            request = null;
            return false;
        }

        request = new ParsedRequest(slotDrive, image, path, json, hex, raw, logical, exportTo);
        error = null;
        return true;
    }

    /// <summary>Opens a mounted drive or an offline image and runs the detector.</summary>
    /// <param name="context">The command context.</param>
    /// <param name="detector">The shared detector.</param>
    /// <param name="logger">Logger for media failures.</param>
    /// <param name="request">The parsed source.</param>
    /// <param name="session">The opened volume when the method returns <see langword="true"/>. The caller disposes it.</param>
    /// <param name="error">The error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when detection ran. An unrecognized volume is still a success.</returns>
    internal static bool TryOpen(
        ICommandContext context,
        IAppleFileSystemDetector detector,
        ILogger logger,
        ParsedRequest request,
        out VolumeSession? session,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(request);

        DiskImageOpenResult? owned = null;
        var owns = false;
        string location;
        string source;
        try
        {
            if (request.Image is not null)
            {
                if (!TryOpenImage(context, request.Image, out owned, out location, out error))
                {
                    session = null;
                    return false;
                }

                owns = true;
                source = "image";
            }
            else if (!TryOpenMounted(context, request.SlotDrive!, out owned, out location, out error))
            {
                session = null;
                return false;
            }
            else
            {
                source = "drive";
            }

            if (!TryCreateSource(owned!, out var fileSource, out error))
            {
                session = null;
                return false;
            }

            var detection = detector.Detect(fileSource!);
            session = new VolumeSession(source, location, detection, owns ? owned : null);
            owned = null;
            error = null;
            return true;
        }
        catch (Exception ex) when (IsMediaFailure(ex))
        {
            logger.Warning(ex, "File system read failed for {Location}", request.Image ?? request.SlotDrive);
            session = null;
            error = $"Cannot read the file system: {ex.Message}";
            return false;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>Maps a <c>--to</c> path into the output root.</summary>
    /// <param name="root">The configured output root, or <see langword="null"/>.</param>
    /// <param name="relative">The requested relative path.</param>
    /// <param name="fullPath">The full path inside the root when the method returns <see langword="true"/>.</param>
    /// <param name="error">Why the path was refused when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="relative"/> stays inside <paramref name="root"/>.</returns>
    internal static bool TryResolveExport(string? root, string relative, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "No output root is configured for --to. Pass --export-root when starting emudbg.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\0', StringComparison.Ordinal))
        {
            error = "--to requires a file name inside the output root.";
            return false;
        }

        if (Path.IsPathRooted(relative))
        {
            error = "--to must be a relative path inside the output root.";
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Join(rootFull, relative));
        var relativeBack = Path.GetRelativePath(rootFull, combined);
        if (relativeBack == "."
            || relativeBack == ".."
            || relativeBack.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeBack.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relativeBack))
        {
            error = "--to is outside the output root.";
            return false;
        }

        fullPath = combined;
        error = null;
        return true;
    }

    /// <summary>Writes export bytes, creating parent directories inside the output root.</summary>
    /// <param name="fullPath">A path already checked by <see cref="TryResolveExport"/>.</param>
    /// <param name="data">The selected file view.</param>
    /// <param name="logger">Logger for I/O failures.</param>
    /// <param name="error">The I/O error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    internal static bool TryWriteExport(string fullPath, ReadOnlySpan<byte> data, ILogger logger, out string? error)
    {
        try
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(fullPath, data.ToArray());
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning(ex, "disk read --to could not write {Path}", fullPath);
            error = $"Cannot write '{fullPath}': {ex.Message}";
            return false;
        }
    }

    /// <summary>Builds the <c>disk fsinfo</c> payload, including rejected probes.</summary>
    /// <param name="session">The opened volume.</param>
    /// <returns>The envelope payload.</returns>
    internal static DiskFileSystemInfoPayload DescribeInfo(VolumeSession session)
    {
        var selected = session.Detection.FileSystem;
        var probes = new DiskFileSystemProbePayload[session.Detection.Attempts.Count];
        for (var i = 0; i < probes.Length; i++)
        {
            var attempt = session.Detection.Attempts[i];
            probes[i] = new DiskFileSystemProbePayload(
                attempt.Kind.ToString(),
                attempt.Recognized,
                selected is not null && ReferenceEquals(attempt.FileSystem, selected),
                attempt.Reason);
        }

        return new DiskFileSystemInfoPayload(
            session.Source,
            session.Location,
            selected is not null,
            selected is null ? null : selected.Kind.ToString(),
            selected is null ? null : AppleCatalogName.EscapePlain(selected.VolumeName),
            probes);
    }

    /// <summary>Formats <c>disk fsinfo</c> text.</summary>
    /// <param name="payload">The payload to print.</param>
    /// <returns>The human-readable listing.</returns>
    internal static string FormatInfo(DiskFileSystemInfoPayload payload)
    {
        var text = new StringBuilder();
        text.Append("Source: ");
        text.Append(payload.Source);
        text.Append('\n');
        text.Append("Location: ");
        text.Append(payload.Location);
        text.Append('\n');
        if (!payload.Recognized || payload.Kind is null)
        {
            text.Append("File system: unrecognized");
            text.Append('\n');
        }
        else
        {
            text.Append("File system: ");
            text.Append(KindLabel(payload.Kind));
            text.Append('\n');
            text.Append("Volume: ");
            text.Append(payload.VolumeName);
            text.Append('\n');
        }

        text.Append("Probes:");
        text.Append('\n');
        foreach (var probe in payload.Probes)
        {
            text.Append("  ");
            text.Append(probe.Kind);
            text.Append(probe.Recognized ? " recognized" : " rejected");
            if (probe.Selected)
            {
                text.Append(" (selected)");
            }

            text.Append(": ");
            text.Append(probe.Reason);
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Explains why no volume was selected.</summary>
    /// <param name="result">The detector result.</param>
    /// <returns>A command error that names every probe.</returns>
    internal static string UnrecognizedMessage(FileSystemDetectionResult result)
    {
        var text = new StringBuilder("No DOS 3.3, ProDOS, or Pascal volume was recognized.");
        foreach (var attempt in result.Attempts)
        {
            text.Append(' ');
            text.Append(attempt.Kind);
            text.Append(attempt.Recognized ? " matched (" : " rejected (");
            text.Append(attempt.Reason);
            text.Append(").");
        }

        return text.ToString();
    }

    /// <summary>Builds the <c>disk cat</c> payload.</summary>
    /// <param name="session">The opened volume.</param>
    /// <param name="requestedPath">The directory the user asked for. Empty lists the root.</param>
    /// <param name="listing">The successful directory listing.</param>
    /// <returns>The envelope payload.</returns>
    internal static DiskCatalogPayload DescribeCatalog(VolumeSession session, string? requestedPath, DirectoryListing listing)
    {
        var fileSystem = session.Detection.FileSystem!;
        var entries = new DiskCatalogEntryPayload[listing.Entries.Count];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = listing.Entries[i];
            entries[i] = new DiskCatalogEntryPayload(
                entry.Name,
                entry.DisplayName,
                entry.Path,
                FileTypeLabel(fileSystem.Kind, entry),
                entry.FileType,
                entry.AuxType,
                entry.EofBytes,
                entry.AllocatedUnits,
                entry.IsLocked,
                entry.IsDirectory);
        }

        var path = string.IsNullOrEmpty(requestedPath) ? "/" : requestedPath;
        return new DiskCatalogPayload(
            session.Source,
            session.Location,
            fileSystem.Kind.ToString(),
            AppleCatalogName.EscapePlain(fileSystem.VolumeName),
            path,
            entries);
    }

    /// <summary>Formats <c>disk cat</c> text.</summary>
    /// <param name="payload">The payload to print.</param>
    /// <returns>The human-readable catalog. Display names are already escaped.</returns>
    internal static string FormatCatalog(DiskCatalogPayload payload)
    {
        var text = new StringBuilder();
        text.Append(KindLabel(payload.Kind));
        text.Append(" volume ");
        text.Append(payload.VolumeName);
        text.Append('\n');
        text.Append("Directory: ");
        text.Append(payload.Path);
        text.Append('\n');
        if (payload.Entries.Count == 0)
        {
            text.Append("(empty)");
            text.Append('\n');
            return text.ToString();
        }

        foreach (var entry in payload.Entries)
        {
            text.Append(entry.Locked ? '*' : ' ');
            text.Append(entry.Directory ? 'D' : ' ');
            text.Append(entry.FileType.PadRight(4));
            text.Append(entry.EofBytes.ToString(CultureInfo.InvariantCulture).PadLeft(8));
            text.Append("  ");
            text.Append(entry.DisplayName);
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Formats <c>disk read</c> text.</summary>
    /// <param name="payload">The payload to print.</param>
    /// <param name="hex">Whether to print a hex dump.</param>
    /// <param name="data">The selected view.</param>
    /// <returns>The human-readable file.</returns>
    internal static string FormatFile(DiskFilePayload payload, bool hex, ReadOnlyMemory<byte> data)
    {
        var text = new StringBuilder();
        text.Append(KindLabel(payload.Kind));
        text.Append(" volume ");
        text.Append(payload.VolumeName);
        text.Append('\n');
        text.Append(payload.Path);
        text.Append(' ');
        text.Append(payload.View);
        text.Append(' ');
        text.Append(payload.Length.ToString(CultureInfo.InvariantCulture));
        text.Append(payload.Length == 1 ? " byte" : " bytes");
        text.Append('\n');
        if (hex)
        {
            AppendHexDump(text, data.Span);
        }
        else if (!string.IsNullOrEmpty(payload.Text))
        {
            text.Append(payload.Text);
            if (!payload.Text.EndsWith('\n'))
            {
                text.Append('\n');
            }
        }

        if (payload.SavedTo is not null)
        {
            text.Append("Wrote ");
            text.Append(payload.Length.ToString(CultureInfo.InvariantCulture));
            text.Append(" bytes to ");
            text.Append(payload.SavedTo);
            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Renders file bytes as host text, mapping Apple carriage returns to newlines.</summary>
    /// <param name="data">The selected view.</param>
    /// <returns>Printable text. Other bytes use <c>\xNN</c>.</returns>
    internal static string ToText(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var value = data[i];
            if (value is 0x0D or 0x8D or 0x0A)
            {
                text.Append('\n');
                continue;
            }

            var glyph = (char)(value & 0x7F);
            if (glyph is >= ' ' and <= '~')
            {
                text.Append(glyph);
                continue;
            }

            text.Append("\\x");
            text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>Returns the host label for a file-system kind token.</summary>
    /// <param name="kind">The enum name stored in a payload.</param>
    /// <returns>DOS 3.3, ProDOS, Pascal, or the token itself.</returns>
    internal static string KindLabel(string kind) => kind switch
    {
        nameof(AppleFileSystemKind.Dos33) => "DOS 3.3",
        nameof(AppleFileSystemKind.Prodos) => "ProDOS",
        nameof(AppleFileSystemKind.Pascal) => "Pascal",
        _ => kind,
    };

    private static bool TryBindSource(Verb verb, string? image, List<string> positionals, out string? slotDrive, out string? path, out string? error)
    {
        slotDrive = null;
        path = null;
        error = null;
        if (image is null)
        {
            if (positionals.Count == 0 || !DiskRuntimeHelpers.TryParseSlotDrive(positionals[0], out _, out _, out _))
            {
                error = verb switch
                {
                    Verb.Info => "Usage: disk fsinfo <slot:drive> | disk fsinfo --image <path>",
                    Verb.Catalog => "Usage: disk cat <slot:drive> [path] | disk cat --image <path> [path]",
                    _ => "Usage: disk read <slot:drive> <file> | disk read --image <path> <file>",
                };
                return false;
            }

            slotDrive = positionals[0];
            positionals.RemoveAt(0);
        }
        else if (positionals.Count > 0 && DiskRuntimeHelpers.TryParseSlotDrive(positionals[0], out _, out _, out _))
        {
            error = "Specify a mounted drive or --image, not both.";
            return false;
        }

        if (verb == Verb.Read)
        {
            if (positionals.Count != 1)
            {
                error = "Usage: disk read <slot:drive> <file> [--hex] [--raw|--logical] [--to <name>]";
                return false;
            }

            path = positionals[0];
            return true;
        }

        if (verb == Verb.Info)
        {
            if (positionals.Count != 0)
            {
                error = "Usage: disk fsinfo <slot:drive> | disk fsinfo --image <path>";
                return false;
            }

            return true;
        }

        if (positionals.Count > 1)
        {
            error = "Usage: disk cat <slot:drive> [path] | disk cat --image <path> [path]";
            return false;
        }

        path = positionals.Count == 0 ? null : positionals[0];
        return true;
    }

    private static bool TryOpenImage(ICommandContext context, string rawPath, out DiskImageOpenResult? open, out string location, out string? error)
    {
        open = null;
        location = rawPath;
        if (context is not IDebugContext debug || debug.DiskImageFactory is not DiskImageFactory factory)
        {
            error = "DiskImageFactory not available on debug context.";
            return false;
        }

        var path = rawPath;
        var resolver = debug.PathResolver;
        if (resolver is not null)
        {
            if (!resolver.TryResolve(rawPath, out var resolved) || resolved is null)
            {
                error = $"Cannot resolve path: '{rawPath}'.";
                return false;
            }

            path = resolved;
        }

        location = path;
        if (!File.Exists(path))
        {
            error = $"File not found: '{path}'.";
            return false;
        }

        try
        {
            open = factory.Open(path, forceReadOnly: true);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or InvalidDataException
                                   or NotSupportedException)
        {
            error = $"Cannot open '{path}': {ex.Message}";
            return false;
        }
    }

    private static bool TryOpenMounted(ICommandContext context, string slotDrive, out DiskImageOpenResult? open, out string location, out string? error)
    {
        open = null;
        location = slotDrive;
        if (context is not IDebugContext debug)
        {
            error = "Debug context required for a mounted drive.";
            return false;
        }

        if (!DiskRuntimeHelpers.TryParseSlotDrive(slotDrive, out var slot, out var drive, out error))
        {
            return false;
        }

        if (!debug.MountedDisks.TryGet(slot, drive - 1, out open) || open is null)
        {
            error = $"No tracked image for slot {slot} drive {drive}; mount it with disk insert.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateSource(DiskImageOpenResult open, out FileSystemSource? source, out string? error)
    {
        IBlockReader? blocks = open switch
        {
            Image525AndBlockResult both => new BlockMediaReader(both.BlockMedia),
            ImageBlockResult block => new BlockMediaReader(block.Media),
            _ => null,
        };
        ISectorReader? sectors = open is Image525AndBlockResult sector && sector.SectorImage is not null
            ? new SectorImageSectorReader(sector.SectorImage)
            : null;
        if (blocks is null && sectors is null)
        {
            source = null;
            error = "The image has no sector or block view a file system can read.";
            return false;
        }

        source = new FileSystemSource(blocks, sectors);
        error = null;
        return true;
    }

    private static bool TryMatchValue(string arg, string name, out string? inline)
    {
        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            inline = null;
            return true;
        }

        var prefix = name + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            inline = arg[prefix.Length..];
            return true;
        }

        inline = null;
        return false;
    }

    private static bool TryTake(string[] args, ref int index, string name, string? inline, out string value, out string? error)
    {
        if (inline is not null)
        {
            if (inline.Length == 0)
            {
                value = string.Empty;
                error = $"Missing value for {name}.";
                return false;
            }

            value = inline;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            error = $"Missing value for {name}.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static bool IsMediaFailure(Exception ex) =>
        ex is ArgumentException
            or InvalidOperationException
            or IOException
            or ObjectDisposedException;

    private static string FileTypeLabel(AppleFileSystemKind kind, FileEntry entry) => kind switch
    {
        AppleFileSystemKind.Dos33 => DosLabel(entry.FileType),
        AppleFileSystemKind.Prodos => ProdosLabel(entry.FileType),
        AppleFileSystemKind.Pascal => PascalLabel(entry.FileType),
        _ => entry.FileType.ToString("X2", CultureInfo.InvariantCulture),
    };

    private static string DosLabel(byte fileType)
    {
        var kind = (Dos33FileType)(fileType & Dos33Layout.FileTypeMask);
        return kind switch
        {
            Dos33FileType.Text => "T",
            Dos33FileType.Integer => "I",
            Dos33FileType.Applesoft => "A",
            Dos33FileType.Binary => "B",
            Dos33FileType.S => "S",
            Dos33FileType.Relocatable => "R",
            Dos33FileType.AdditionalApplesoft => "A",
            Dos33FileType.AdditionalBinary => "B",
            _ => fileType.ToString("X2", CultureInfo.InvariantCulture),
        };
    }

    private static string ProdosLabel(byte fileType) => fileType switch
    {
        (byte)ProdosFileType.Text => "TXT",
        (byte)ProdosFileType.Binary => "BIN",
        (byte)ProdosFileType.Directory => "DIR",
        0xFC => "BAS",
        0xFF => "SYS",
        _ => fileType.ToString("X2", CultureInfo.InvariantCulture),
    };

    private static string PascalLabel(byte fileType)
    {
        var kind = (PascalFileKind)fileType;
        return Enum.IsDefined(kind) ? kind.ToString() : fileType.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void AppendHexDump(StringBuilder text, ReadOnlySpan<byte> data)
    {
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            text.Append(offset.ToString("X4", CultureInfo.InvariantCulture));
            text.Append(": ");
            var count = Math.Min(16, data.Length - offset);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }
    }

    /// <summary>Arguments accepted by one file-system command.</summary>
    internal sealed class ParsedRequest
    {
        /// <summary>Initializes a new instance of the <see cref="ParsedRequest"/> class.</summary>
        /// <param name="slotDrive">The mounted <c>slot:drive</c>, or <see langword="null"/> for <c>--image</c>.</param>
        /// <param name="image">The offline image path, or <see langword="null"/> for a drive.</param>
        /// <param name="path">The catalog or file path, or <see langword="null"/>.</param>
        /// <param name="json">Whether <c>--json</c> was set.</param>
        /// <param name="hex">Whether <c>--hex</c> was set.</param>
        /// <param name="raw">Whether <c>--raw</c> was set.</param>
        /// <param name="logical">Whether <c>--logical</c> was set.</param>
        /// <param name="exportTo">The <c>--to</c> path, or <see langword="null"/>.</param>
        public ParsedRequest(string? slotDrive, string? image, string? path, bool json, bool hex, bool raw, bool logical, string? exportTo)
        {
            this.SlotDrive = slotDrive;
            this.Image = image;
            this.Path = path;
            this.Json = json;
            this.Hex = hex;
            this.Raw = raw;
            this.Logical = logical;
            this.ExportTo = exportTo;
        }

        /// <summary>Gets the mounted drive, when the source is not an image.</summary>
        /// <value>The <c>slot:drive</c> token, or <see langword="null"/>.</value>
        public string? SlotDrive { get; }

        /// <summary>Gets the offline image path.</summary>
        /// <value>The path, or <see langword="null"/> for a mounted drive.</value>
        public string? Image { get; }

        /// <summary>Gets the catalog directory or file path.</summary>
        /// <value>The path, or <see langword="null"/> when the command lists the volume root.</value>
        public string? Path { get; }

        /// <summary>Gets a value indicating whether JSON was requested.</summary>
        /// <value><see langword="true"/> when <c>--json</c> was passed.</value>
        public bool Json { get; }

        /// <summary>Gets a value indicating whether a hex dump was requested.</summary>
        /// <value><see langword="true"/> when <c>--hex</c> was passed.</value>
        public bool Hex { get; }

        /// <summary>Gets a value indicating whether the raw view was requested.</summary>
        /// <value><see langword="true"/> when <c>--raw</c> was passed.</value>
        public bool Raw { get; }

        /// <summary>Gets a value indicating whether the logical view was requested explicitly.</summary>
        /// <value><see langword="true"/> when <c>--logical</c> was passed. The logical view is also the default.</value>
        public bool Logical { get; }

        /// <summary>Gets the <c>--to</c> path.</summary>
        /// <value>The relative export path, or <see langword="null"/>.</value>
        public string? ExportTo { get; }

        /// <summary>Gets the file view to read.</summary>
        /// <value><see cref="FileReadView.Raw"/> when <see cref="Raw"/> is set; otherwise logical.</value>
        public FileReadView View => this.Raw ? FileReadView.Raw : FileReadView.Logical;
    }

    /// <summary>A detected volume and the image handle this command owns, if any.</summary>
    internal sealed class VolumeSession : IDisposable
    {
        private readonly DiskImageOpenResult? owned;

        /// <summary>Initializes a new instance of the <see cref="VolumeSession"/> class.</summary>
        /// <param name="source"><c>image</c> or <c>drive</c>.</param>
        /// <param name="location">The image path or <c>slot:drive</c>.</param>
        /// <param name="detection">The detector result.</param>
        /// <param name="owned">An image this session must dispose, or <see langword="null"/> for a mounted drive.</param>
        public VolumeSession(string source, string location, FileSystemDetectionResult detection, DiskImageOpenResult? owned)
        {
            this.Source = source;
            this.Location = location;
            this.Detection = detection;
            this.owned = owned;
        }

        /// <summary>Gets the source kind.</summary>
        /// <value><c>image</c> or <c>drive</c>.</value>
        public string Source { get; }

        /// <summary>Gets the image path or <c>slot:drive</c>.</summary>
        /// <value>The location shown to the user.</value>
        public string Location { get; }

        /// <summary>Gets the detector result.</summary>
        /// <value>Every probe that ran, and the selected volume when one matched.</value>
        public FileSystemDetectionResult Detection { get; }

        /// <summary>Releases an offline image opened for this command.</summary>
        public void Dispose() => this.owned?.Dispose();
    }
}