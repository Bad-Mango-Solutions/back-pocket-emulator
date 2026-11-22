// <copyright file="DirectoryListing.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Result of <see cref="IAppleFileSystem.ListDirectory"/>.
/// </summary>
/// <remarks>
/// An empty volume root is a success with an empty <see cref="Entries"/> list.
/// A missing path is a failure, so <c>disk ls</c> can tell it from an empty directory.
/// Entries are in on-disk order. Deleted slots, unused slots, the ProDOS volume header,
/// and ProDOS subdirectory headers are omitted. Compare <see cref="Status"/> and
/// <see cref="Entries"/>. This type is a class because list equality is by reference.
/// </remarks>
public sealed class DirectoryListing
{
    private DirectoryListing(bool succeeded, IReadOnlyList<FileEntry> entries, FileAccessStatus status, string reason)
    {
        this.Succeeded = succeeded;
        this.Entries = entries;
        this.Status = status;
        this.Reason = reason;
    }

    /// <summary>Gets a value indicating whether <see cref="Entries"/> is the directory.</summary>
    /// <value><see langword="true"/> when the path names a directory, including an empty one.</value>
    public bool Succeeded { get; }

    /// <summary>Gets the entries.</summary>
    /// <value>The catalog snapshot, or an empty list when <see cref="Succeeded"/> is <see langword="false"/>.</value>
    public IReadOnlyList<FileEntry> Entries { get; }

    /// <summary>Gets the outcome.</summary>
    /// <value>The status code.</value>
    public FileAccessStatus Status { get; }

    /// <summary>Gets a short explanation suitable for a command message.</summary>
    /// <value><c>ok</c> on success, otherwise why the path was not listed.</value>
    public string Reason { get; }

    /// <summary>
    /// Creates a successful listing.
    /// </summary>
    /// <param name="entries">Catalog entries in on-disk order. The list is snapshotted.</param>
    /// <returns>A success result.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="entries"/> is <see langword="null"/>.</exception>
    public static DirectoryListing Success(IReadOnlyList<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new(true, entries.ToArray(), FileAccessStatus.Success, "ok");
    }

    /// <summary>
    /// Creates a failed listing.
    /// </summary>
    /// <param name="status">Why the list failed. Not <see cref="FileAccessStatus.Success"/>.</param>
    /// <param name="reason">Non-empty explanation.</param>
    /// <returns>A failure result with an empty entry list.</returns>
    /// <exception cref="ArgumentException">If <paramref name="status"/> is success or <paramref name="reason"/> is empty.</exception>
    public static DirectoryListing Failure(FileAccessStatus status, string reason)
    {
        if (status == FileAccessStatus.Success)
        {
            throw new ArgumentException("A failure result cannot use Success.", nameof(status));
        }

        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new(false, Array.Empty<FileEntry>(), status, reason);
    }
}