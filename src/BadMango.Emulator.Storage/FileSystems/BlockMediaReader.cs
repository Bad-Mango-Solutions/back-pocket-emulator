// <copyright file="BlockMediaReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using BadMango.Emulator.Storage.Media;

/// <summary>
/// <see cref="IBlockReader"/> over an <see cref="IBlockMedia"/> instance.
/// </summary>
/// <remarks>
/// The wrapper does not own <see cref="IBlockMedia"/> and does not dispose it.
/// It does not expose <see cref="IBlockMedia.WriteBlock"/>. Read failures from the media
/// propagate unchanged.
/// </remarks>
public sealed class BlockMediaReader : IBlockReader
{
    private readonly IBlockMedia media;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockMediaReader"/> class.
    /// </summary>
    /// <param name="media">The block media to read.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="media"/> is <see langword="null"/>.</exception>
    public BlockMediaReader(IBlockMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        this.media = media;
    }

    /// <inheritdoc />
    public int BlockSize => this.media.BlockSize;

    /// <inheritdoc />
    public int BlockCount => this.media.BlockCount;

    /// <inheritdoc />
    public void ReadBlock(int blockIndex, Span<byte> destination) => this.media.ReadBlock(blockIndex, destination);
}