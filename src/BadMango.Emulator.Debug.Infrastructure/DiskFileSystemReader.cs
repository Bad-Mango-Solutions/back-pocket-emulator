// <copyright file="DiskFileSystemReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;
using BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Lists and reads DOS 3.3, ProDOS, and Pascal files on a mounted drive or an offline image.
/// </summary>
/// <remarks>
/// <para>
/// The open, flush, and dispose path is <see cref="DiskFileSystemSupport"/>. A mounted drive with
/// unflushed tracks is flushed before the catalog or file is read. An offline image is disposed
/// when the call returns. A drive's mounted image is not disposed.
/// </para>
/// <para>
/// Name lookup is the file-system reader's rule: an exact match wins, and a case-insensitive match
/// is used only when it is unique. Display names keep the <c>\c{X}</c>, <c>\i{X}</c>, and
/// <c>\f{X}</c> catalog escaping.
/// </para>
/// </remarks>
public sealed class DiskFileSystemReader
{
    private readonly IAppleFileSystemDetector detector;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskFileSystemReader"/> class.
    /// </summary>
    /// <param name="detector">The shared file-system detector.</param>
    /// <param name="logger">Logger for media failures.</param>
    public DiskFileSystemReader(IAppleFileSystemDetector detector, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(logger);
        this.detector = detector;
        this.logger = logger;
    }

    /// <summary>
    /// Renders file bytes as host text, mapping Apple carriage returns to newlines.
    /// </summary>
    /// <param name="data">The selected view.</param>
    /// <returns>Printable text. Other bytes use <c>\xNN</c>.</returns>
    public static string ToHostText(ReadOnlySpan<byte> data) => DiskFileSystemSupport.ToText(data);

    /// <summary>
    /// Lists a directory on a mounted drive or an offline image.
    /// </summary>
    /// <param name="context">The debug context that owns mounted images and the disk factory.</param>
    /// <param name="imagePath">An offline image path, or <see langword="null"/> for a mounted drive.</param>
    /// <param name="slotDrive">A <c>slot:drive</c> token, or <see langword="null"/> for an image.</param>
    /// <param name="directory">The directory to list. <see langword="null"/>, empty, or <c>/</c> lists the volume root.</param>
    /// <param name="catalog">The catalog when the method returns <see langword="true"/>.</param>
    /// <param name="error">The error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="catalog"/> is the directory.</returns>
    public bool TryList(
        ICommandContext context,
        string? imagePath,
        string? slotDrive,
        string? directory,
        out DiskCatalogPayload? catalog,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryBindSource(imagePath, slotDrive, out error))
        {
            catalog = null;
            return false;
        }

        var request = new DiskFileSystemSupport.ParsedRequest(slotDrive, imagePath, directory, json: false, hex: false, raw: false, logical: false, exportTo: null);
        if (!DiskFileSystemSupport.TryOpen(context, this.detector, this.logger, request, out DiskFileSystemSupport.VolumeSession? session, out error) || session is null)
        {
            catalog = null;
            return false;
        }

        using (session)
        {
            IAppleFileSystem? fileSystem = session.Detection.FileSystem;
            if (fileSystem is null)
            {
                catalog = null;
                error = DiskFileSystemSupport.UnrecognizedMessage(session.Detection);
                return false;
            }

            DirectoryListing listing;
            try
            {
                listing = fileSystem.ListDirectory(directory);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or IOException
                                       or ObjectDisposedException)
            {
                this.logger.Warning(ex, "Catalog read failed for {Location}", session.Location);
                catalog = null;
                error = $"Cannot read the file system: {ex.Message}";
                return false;
            }

            if (!listing.Succeeded)
            {
                string where = string.IsNullOrEmpty(directory) ? "/" : directory;
                catalog = null;
                error = $"Cannot list '{where}': {listing.Reason}";
                return false;
            }

            catalog = DiskFileSystemSupport.DescribeCatalog(session, directory, listing);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Reads one file and returns a copy of the selected view.
    /// </summary>
    /// <param name="context">The debug context that owns mounted images and the disk factory.</param>
    /// <param name="imagePath">An offline image path, or <see langword="null"/> for a mounted drive.</param>
    /// <param name="slotDrive">A <c>slot:drive</c> token, or <see langword="null"/> for an image.</param>
    /// <param name="fileName">The volume-relative file name.</param>
    /// <param name="raw"><see langword="true"/> for on-disk bytes. <see langword="false"/> uses the logical view.</param>
    /// <param name="file">The copied file when the method returns <see langword="true"/>.</param>
    /// <param name="error">The error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="file"/> holds the selected view.</returns>
    public bool TryRead(
        ICommandContext context,
        string? imagePath,
        string? slotDrive,
        string fileName,
        bool raw,
        out DiskVolumeFile? file,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!TryBindSource(imagePath, slotDrive, out error))
        {
            file = null;
            return false;
        }

        var request = new DiskFileSystemSupport.ParsedRequest(slotDrive, imagePath, fileName, json: false, hex: false, raw: raw, logical: !raw, exportTo: null);
        if (!DiskFileSystemSupport.TryOpen(context, this.detector, this.logger, request, out DiskFileSystemSupport.VolumeSession? session, out error) || session is null)
        {
            file = null;
            return false;
        }

        using (session)
        {
            IAppleFileSystem? fileSystem = session.Detection.FileSystem;
            if (fileSystem is null)
            {
                file = null;
                error = DiskFileSystemSupport.UnrecognizedMessage(session.Detection);
                return false;
            }

            FileReadResult read;
            try
            {
                read = fileSystem.TryReadFile(fileName, request.View);
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or IOException
                                       or ObjectDisposedException)
            {
                this.logger.Warning(ex, "File read failed for {Location} {Path}", session.Location, fileName);
                file = null;
                error = $"Cannot read the file system: {ex.Message}";
                return false;
            }

            if (!read.Succeeded)
            {
                file = null;
                error = $"Cannot read '{fileName}': {read.Reason}";
                return false;
            }

            file = new DiskVolumeFile(
                session.Source,
                session.Location,
                fileSystem.Kind.ToString(),
                AppleCatalogName.EscapePlain(fileSystem.VolumeName),
                fileName,
                raw ? "raw" : "logical",
                read.Data);
            error = null;
            return true;
        }
    }

    private static bool TryBindSource(string? imagePath, string? slotDrive, out string? error)
    {
        bool hasImage = !string.IsNullOrWhiteSpace(imagePath);
        bool hasDrive = !string.IsNullOrWhiteSpace(slotDrive);
        if (hasImage && hasDrive)
        {
            error = "Specify an image path or a slot and drive, not both.";
            return false;
        }

        if (!hasImage && !hasDrive)
        {
            error = "An image path or a slot and drive is required.";
            return false;
        }

        error = null;
        return true;
    }
}