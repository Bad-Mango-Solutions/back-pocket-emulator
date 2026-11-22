// <copyright file="PascalFileSystemProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Recognizes an Apple Pascal volume and opens a <see cref="PascalFileSystem"/>.
/// </summary>
/// <remarks>
/// <para>
/// Register this probe last in <see cref="AppleFileSystemDetector.RecommendedProbeOrder"/>.
/// The Pascal header is the loosest of the three signatures. Recognition is
/// <see cref="PascalLayout.IsVolumeHeader(System.ReadOnlySpan{byte}, int)"/>. A false result is
/// <see cref="FileSystemProbeAttempt.Reject(AppleFileSystemKind, string)"/>. This type has no
/// <c>catch</c>. Reader exceptions propagate, including <see cref="ArgumentException"/>, because
/// <see cref="ArgumentOutOfRangeException"/> derives from it and must not become a rejection.
/// The constructor's <see cref="ArgumentException"/> is only the guard for a skipped check.
/// </para>
/// <para>
/// A source with no block reader, a block size other than <see cref="PascalLayout.BlockSize"/>,
/// or fewer than <see cref="PascalLayout.FirstDataBlock"/> blocks is rejected without reading a
/// block past the end of the image.
/// </para>
/// </remarks>
public sealed class PascalFileSystemProbe : IAppleFileSystemProbe
{
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PascalFileSystemProbe"/> class.
    /// </summary>
    /// <param name="logger">Logger passed to <see cref="PascalFileSystem"/> and used for rejections.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public PascalFileSystemProbe(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger.ForContext<PascalFileSystemProbe>();
    }

    /// <inheritdoc />
    public AppleFileSystemKind Kind => AppleFileSystemKind.Pascal;

    /// <inheritdoc />
    public FileSystemProbeAttempt TryOpen(FileSystemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var blocks = source.Blocks;
        if (blocks is null)
        {
            return this.Reject("Apple Pascal needs a block reader.");
        }

        if (blocks.BlockSize != PascalLayout.BlockSize)
        {
            return this.Reject($"Apple Pascal blocks are {PascalLayout.BlockSize} bytes.");
        }

        if (blocks.BlockCount < PascalLayout.FirstDataBlock)
        {
            return this.Reject("The image is shorter than an Apple Pascal directory.");
        }

        var directory = new byte[PascalLayout.DirectoryBytes];
        PascalFileSystem.ReadDirectory(blocks, directory);
        if (!PascalLayout.IsVolumeHeader(directory, blocks.BlockCount))
        {
            return this.Reject("The directory does not begin with an Apple Pascal volume header.");
        }

        var fileSystem = new PascalFileSystem(this.logger, blocks);
        return FileSystemProbeAttempt.Match(
            AppleFileSystemKind.Pascal,
            "Apple Pascal volume header.",
            fileSystem);
    }

    private FileSystemProbeAttempt Reject(string reason)
    {
        this.logger.Debug("Apple Pascal probe rejected the image: {Reason}", reason);
        return FileSystemProbeAttempt.Reject(AppleFileSystemKind.Pascal, reason);
    }
}