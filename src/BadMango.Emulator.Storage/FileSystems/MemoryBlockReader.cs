// <copyright file="MemoryBlockReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// <see cref="IBlockReader"/> over a contiguous block image held in memory.
/// </summary>
/// <remarks>
/// The buffer is read in place and is not copied. The caller keeps it unchanged for the
/// lifetime of this reader. There is no write method.
/// </remarks>
public sealed class MemoryBlockReader : IBlockReader
{
    private readonly ReadOnlyMemory<byte> image;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryBlockReader"/> class.
    /// </summary>
    /// <param name="image">Contiguous blocks. Its length is <paramref name="blockCount"/> × <paramref name="blockSize"/>.</param>
    /// <param name="blockCount">Number of blocks. Positive.</param>
    /// <param name="blockSize">Bytes per block. Positive. 512 for ProDOS and Pascal.</param>
    /// <exception cref="ArgumentOutOfRangeException">If a count is not positive or the buffer length does not match.</exception>
    public MemoryBlockReader(ReadOnlyMemory<byte> image, int blockCount, int blockSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        var required = (long)blockCount * blockSize;
        if (required > int.MaxValue || image.Length != required)
        {
            throw new ArgumentOutOfRangeException(nameof(image), "Image length must equal blockCount × blockSize.");
        }

        this.image = image;
        this.BlockCount = blockCount;
        this.BlockSize = blockSize;
    }

    /// <inheritdoc />
    public int BlockSize { get; }

    /// <inheritdoc />
    public int BlockCount { get; }

    /// <inheritdoc />
    public void ReadBlock(int blockIndex, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(blockIndex, this.BlockCount);
        if (destination.Length != this.BlockSize)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), $"Destination must be {this.BlockSize} bytes.");
        }

        this.image.Span.Slice(blockIndex * this.BlockSize, this.BlockSize).CopyTo(destination);
    }
}