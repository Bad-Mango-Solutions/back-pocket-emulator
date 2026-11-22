// <copyright file="IAppleFileSystemProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// One format the <see cref="AppleFileSystemDetector"/> asks to recognize a volume.
/// </summary>
/// <remarks>
/// <para>
/// DOS 3.3, ProDOS, and Pascal each supply a probe. The probe returns a rejected
/// <see cref="FileSystemProbeAttempt"/> when the bytes are not that format. It uses
/// <see cref="Dos33Layout.IsVtoc"/>, <see cref="ProdosLayout.IsVolumeDirectoryKeyBlock"/>,
/// or <see cref="PascalLayout.IsVolumeHeader"/> for that decision. A rejection is not
/// an exception.
/// </para>
/// <para>
/// Media failures propagate. The detector does not catch them. Readers throw
/// <see cref="ArgumentException"/>, <see cref="ArgumentNullException"/>,
/// <see cref="ArgumentOutOfRangeException"/>, <see cref="InvalidOperationException"/>,
/// <see cref="IOException"/>, and <see cref="ObjectDisposedException"/> for a bad index,
/// a wrong buffer, a disposed image, or an I/O failure. The Pascal constructor throws
/// <see cref="ArgumentException"/> when the header is not Pascal. The Pascal probe catches
/// that <see cref="ArgumentException"/> and returns <see cref="FileSystemProbeAttempt.Reject"/>.
/// It does not catch <see cref="ArgumentNullException"/> or any other failure.
/// Register probes in <see cref="AppleFileSystemDetector.RecommendedProbeOrder"/>.
/// </para>
/// </remarks>
public interface IAppleFileSystemProbe
{
    /// <summary>Gets the format this probe recognizes.</summary>
    /// <value>The file-system kind.</value>
    AppleFileSystemKind Kind { get; }

    /// <summary>
    /// Tries to open a volume of <see cref="Kind"/>.
    /// </summary>
    /// <param name="source">Block view, sector view, or both.</param>
    /// <returns>
    /// A recognized attempt whose <see cref="FileSystemProbeAttempt.FileSystem"/> is set,
    /// or a rejected attempt that explains why the bytes are not this format.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If a reader rejects its arguments.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If a reader is asked for a block or sector it does not have.</exception>
    /// <exception cref="InvalidOperationException">If the media refuses the read.</exception>
    /// <exception cref="IOException">If the backing store cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">If the media has been disposed.</exception>
    FileSystemProbeAttempt TryOpen(FileSystemSource source);
}