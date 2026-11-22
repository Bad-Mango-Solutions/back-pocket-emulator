// <copyright file="ProdosFileSystemProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Recognizes a ProDOS volume directory and opens a <see cref="ProdosFileSystem"/>.
/// </summary>
/// <remarks>
/// The probe calls <see cref="ProdosLayout.IsVolumeDirectoryKeyBlock"/> and returns
/// <see cref="FileSystemProbeAttempt.Reject"/> when block 2 is not a volume key block.
/// It does not catch. A block read that throws leaves the detector. The constructor's
/// <see cref="ArgumentException"/> is the guard for a caller that skips this check.
/// </remarks>
public sealed class ProdosFileSystemProbe : IAppleFileSystemProbe
{
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProdosFileSystemProbe"/> class.
    /// </summary>
    /// <param name="logger">Logger passed to a volume this probe opens.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public ProdosFileSystemProbe(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <inheritdoc />
    public AppleFileSystemKind Kind => AppleFileSystemKind.Prodos;

    /// <inheritdoc />
    public FileSystemProbeAttempt TryOpen(FileSystemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var blocks = source.Blocks;
        if (blocks is null)
        {
            return FileSystemProbeAttempt.Reject(this.Kind, "ProDOS needs a block reader.");
        }

        if (blocks.BlockSize != ProdosLayout.BlockSize)
        {
            return FileSystemProbeAttempt.Reject(this.Kind, "ProDOS blocks are 512 bytes.");
        }

        if (blocks.BlockCount <= ProdosLayout.VolumeKeyBlock)
        {
            return FileSystemProbeAttempt.Reject(this.Kind, "The image does not contain a ProDOS volume key block.");
        }

        var key = new byte[ProdosLayout.BlockSize];
        blocks.ReadBlock(ProdosLayout.VolumeKeyBlock, key);
        if (!ProdosLayout.IsVolumeDirectoryKeyBlock(key, blocks.BlockCount))
        {
            return FileSystemProbeAttempt.Reject(this.Kind, "Block 2 is not a ProDOS volume directory.");
        }

        return FileSystemProbeAttempt.Match(
            this.Kind,
            "Block 2 has a ProDOS volume header.",
            new ProdosFileSystem(blocks, this.logger));
    }
}