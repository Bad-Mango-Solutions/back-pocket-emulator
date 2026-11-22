// <copyright file="FileSystemProbeAttempt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// One format the detector tried, and why that format was recognized or rejected.
/// </summary>
public sealed record FileSystemProbeAttempt
{
    private FileSystemProbeAttempt(AppleFileSystemKind kind, bool recognized, string reason, IAppleFileSystem? fileSystem)
    {
        this.Kind = kind;
        this.Recognized = recognized;
        this.Reason = reason;
        this.FileSystem = fileSystem;
    }

    /// <summary>Gets the format that was tried.</summary>
    /// <value>The probe's kind.</value>
    public AppleFileSystemKind Kind { get; }

    /// <summary>Gets a value indicating whether this format opened the volume.</summary>
    /// <value><see langword="true"/> when <see cref="FileSystem"/> is set.</value>
    public bool Recognized { get; }

    /// <summary>Gets why the format was recognized or rejected.</summary>
    /// <value>A non-empty explanation. The detector records one for every probe it runs.</value>
    public string Reason { get; }

    /// <summary>Gets the opened volume.</summary>
    /// <value>The file system when <see cref="Recognized"/> is <see langword="true"/>; otherwise <see langword="null"/>.</value>
    public IAppleFileSystem? FileSystem { get; }

    /// <summary>
    /// Creates an attempt that opened a volume.
    /// </summary>
    /// <param name="kind">The format that matched.</param>
    /// <param name="reason">Why the bytes were accepted. Not empty.</param>
    /// <param name="fileSystem">The opened volume.</param>
    /// <returns>A recognized attempt.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">If <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public static FileSystemProbeAttempt Match(AppleFileSystemKind kind, string reason, IAppleFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(fileSystem);
        return new FileSystemProbeAttempt(kind, true, reason, fileSystem);
    }

    /// <summary>
    /// Creates an attempt that rejected the volume.
    /// </summary>
    /// <param name="kind">The format that was tried.</param>
    /// <param name="reason">Why the bytes were not this format. Not empty.</param>
    /// <returns>A rejected attempt with no file system.</returns>
    /// <exception cref="ArgumentException">If <paramref name="reason"/> is empty.</exception>
    public static FileSystemProbeAttempt Reject(AppleFileSystemKind kind, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new FileSystemProbeAttempt(kind, false, reason, null);
    }
}