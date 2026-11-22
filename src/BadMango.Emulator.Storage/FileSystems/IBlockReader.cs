// <copyright file="IBlockReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Read-only 512-byte block source for ProDOS and Pascal readers.
/// </summary>
/// <remarks>
/// Implementations wrap media (<see cref="BlockMediaReader"/>, <see cref="MemoryBlockReader"/>).
/// File-system constructors take this interface. They do not take a
/// <see cref="Func{T, TResult}"/> that returns a block. The interface has no write method.
/// Reads are not required to be thread-safe. Out-of-range indexes and the wrong buffer
/// length throw <see cref="ArgumentOutOfRangeException"/>. A disposed or read-only media
/// failure throws <see cref="ObjectDisposedException"/>, <see cref="IOException"/>, or
/// <see cref="InvalidOperationException"/> and is not swallowed by the file-system reader.
/// </remarks>
public interface IBlockReader
{
    /// <summary>Gets the block size in bytes.</summary>
    /// <value>512 for ProDOS and Pascal.</value>
    int BlockSize { get; }

    /// <summary>Gets the number of blocks in the image.</summary>
    /// <value>The block count the media reports.</value>
    int BlockCount { get; }

    /// <summary>
    /// Reads one block.
    /// </summary>
    /// <param name="blockIndex">Zero-based block index in the range <c>[0, BlockCount)</c>.</param>
    /// <param name="destination">Destination buffer. Its length is <see cref="BlockSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// If <paramref name="blockIndex"/> is out of range or <paramref name="destination"/> has the wrong length.
    /// </exception>
    void ReadBlock(int blockIndex, Span<byte> destination);
}