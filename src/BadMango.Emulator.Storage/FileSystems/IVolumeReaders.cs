// <copyright file="IVolumeReaders.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Decoded block and sector views of one mounted sector image.
/// </summary>
/// <remarks>
/// The readers return image bytes, not nibbles. A Disk II stream that has not been
/// decoded does not implement this. Map builds use these readers and do not re-enter
/// the controller's nibble path.
/// </remarks>
public interface IVolumeReaders
{
    /// <summary>Gets a ProDOS block view of the image.</summary>
    /// <value>The block reader. The caller does not dispose it.</value>
    IBlockReader Blocks { get; }

    /// <summary>Gets a DOS logical sector view of the image.</summary>
    /// <value>The sector reader. The caller does not dispose it.</value>
    ISectorReader Sectors { get; }
}