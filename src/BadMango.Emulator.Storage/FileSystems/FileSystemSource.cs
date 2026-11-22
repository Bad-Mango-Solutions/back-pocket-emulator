// <copyright file="FileSystemSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// The readers a probe may use while recognizing a volume.
/// </summary>
/// <remarks>
/// A 140K sector image supplies both views. A hard-disk image supplies
/// <see cref="Blocks"/> only. A probe that needs a missing view returns a rejected
/// <see cref="FileSystemProbeAttempt"/> instead of throwing. The source does not own
/// the readers and does not dispose them.
/// </remarks>
public sealed class FileSystemSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemSource"/> class.
    /// </summary>
    /// <param name="blocks">Block view, or <see langword="null"/> when the image has none.</param>
    /// <param name="sectors">DOS logical sector view, or <see langword="null"/> when the image has none.</param>
    /// <exception cref="ArgumentException">If both readers are <see langword="null"/>.</exception>
    public FileSystemSource(IBlockReader? blocks, ISectorReader? sectors)
    {
        if (blocks is null && sectors is null)
        {
            throw new ArgumentException("A file-system source needs a block reader, a sector reader, or both.");
        }

        this.Blocks = blocks;
        this.Sectors = sectors;
    }

    /// <summary>Gets the block view.</summary>
    /// <value>The block reader, or <see langword="null"/>.</value>
    public IBlockReader? Blocks { get; }

    /// <summary>Gets the DOS logical sector view.</summary>
    /// <value>The sector reader, or <see langword="null"/>.</value>
    public ISectorReader? Sectors { get; }
}