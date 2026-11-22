// <copyright file="IAppleFileSystemDetector.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Recognizes a DOS 3.3, ProDOS, or Pascal volume from media readers.
/// </summary>
/// <remarks>
/// Commands resolve this interface from DI. They do not construct a detector per call.
/// The implementation is <see cref="AppleFileSystemDetector"/>.
/// </remarks>
public interface IAppleFileSystemDetector
{
    /// <summary>
    /// Runs every registered probe.
    /// </summary>
    /// <param name="source">The block view, the sector view, or both.</param>
    /// <returns>The first recognized volume and a reason for every format that was tried.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">If a probe returns an attempt that disagrees with its kind or its recognized flag.</exception>
    /// <exception cref="ArgumentException">If a probe's reader rejects its arguments. Not caught.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If a probe reads past the media. Not caught.</exception>
    /// <exception cref="IOException">If a probe's media read fails. Not caught.</exception>
    /// <exception cref="ObjectDisposedException">If a probe's media has been disposed. Not caught.</exception>
    FileSystemDetectionResult Detect(FileSystemSource source);
}