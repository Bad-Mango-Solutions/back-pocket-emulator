// <copyright file="FileUnitLocation.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// One data block or sector that belongs to a file.
/// </summary>
/// <param name="Path">Volume-relative path, without a leading slash.</param>
/// <param name="Offset">Byte offset of this unit in the file.</param>
/// <param name="Length">Bytes of the file stored in this unit. At most one block or one sector.</param>
/// <param name="FileLength">End-of-file, in bytes. Sparse holes are included. Index blocks are not.</param>
public readonly record struct FileUnitLocation(string Path, long Offset, int Length, long FileLength);