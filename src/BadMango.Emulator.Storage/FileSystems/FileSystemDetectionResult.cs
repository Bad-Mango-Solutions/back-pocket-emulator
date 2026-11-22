// <copyright file="FileSystemDetectionResult.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Every format a detector run tried, and the volume it selected.
/// </summary>
/// <remarks>
/// <see cref="Attempts"/> is in probe order and includes the recognized format and every
/// rejection. <see cref="FileSystem"/> is the first recognized volume, or
/// <see langword="null"/> when every probe rejected the image. Later matches stay in
/// <see cref="Attempts"/> and are not selected.
/// </remarks>
public sealed class FileSystemDetectionResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemDetectionResult"/> class.
    /// </summary>
    /// <param name="fileSystem">The selected volume, or <see langword="null"/>.</param>
    /// <param name="attempts">Every probe that ran, in order. The list is snapshotted.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="attempts"/> is <see langword="null"/>.</exception>
    public FileSystemDetectionResult(IAppleFileSystem? fileSystem, IReadOnlyList<FileSystemProbeAttempt> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        this.FileSystem = fileSystem;
        this.Attempts = attempts.ToArray();
    }

    /// <summary>Gets the selected volume.</summary>
    /// <value>The first recognized file system, or <see langword="null"/>.</value>
    public IAppleFileSystem? FileSystem { get; }

    /// <summary>Gets every format that was tried and why.</summary>
    /// <value>The attempts, in probe order.</value>
    public IReadOnlyList<FileSystemProbeAttempt> Attempts { get; }

    /// <summary>Gets a value indicating whether a volume was selected.</summary>
    /// <value><see langword="true"/> when <see cref="FileSystem"/> is not <see langword="null"/>.</value>
    public bool IsRecognized => this.FileSystem is not null;
}