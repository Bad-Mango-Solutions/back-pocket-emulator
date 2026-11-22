// <copyright file="FileReadResult.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Result of <see cref="IAppleFileSystem.TryReadFile"/>.
/// </summary>
/// <remarks>
/// A successful read of an empty file has <see cref="Succeeded"/> set and an empty
/// <see cref="Data"/> buffer. A failed read has an empty buffer and a <see cref="Reason"/>
/// that <c>disk cat</c> can print. Unsupported ProDOS storage never puts placeholder
/// text in <see cref="Data"/>. Compare <see cref="Status"/> and the bytes in
/// <see cref="Data"/>. This type is a class because <see cref="ReadOnlyMemory{T}"/>
/// equality does not compare file contents.
/// </remarks>
public sealed class FileReadResult
{
    private FileReadResult(bool succeeded, ReadOnlyMemory<byte> data, FileAccessStatus status, string reason)
    {
        this.Succeeded = succeeded;
        this.Data = data;
        this.Status = status;
        this.Reason = reason;
    }

    /// <summary>Gets a value indicating whether <see cref="Data"/> is the file.</summary>
    /// <value><see langword="true"/> when <see cref="Status"/> is <see cref="FileAccessStatus.Success"/>.</value>
    public bool Succeeded { get; }

    /// <summary>Gets the file bytes.</summary>
    /// <value>The trimmed view, or empty when <see cref="Succeeded"/> is <see langword="false"/>.</value>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the outcome.</summary>
    /// <value>The status code.</value>
    public FileAccessStatus Status { get; }

    /// <summary>Gets a short explanation suitable for a command message.</summary>
    /// <value><c>ok</c> on success, otherwise the reason the read did not return bytes.</value>
    public string Reason { get; }

    /// <summary>
    /// Creates a successful read.
    /// </summary>
    /// <param name="data">Trimmed file bytes. Empty is a successful empty file.</param>
    /// <returns>A success result.</returns>
    public static FileReadResult Success(ReadOnlyMemory<byte> data) =>
        new(true, data, FileAccessStatus.Success, "ok");

    /// <summary>
    /// Creates a failed read that carries no file bytes.
    /// </summary>
    /// <param name="status">Why the read failed. Not <see cref="FileAccessStatus.Success"/>.</param>
    /// <param name="reason">Non-empty explanation.</param>
    /// <returns>A failure result with an empty buffer.</returns>
    /// <exception cref="ArgumentException">If <paramref name="status"/> is success or <paramref name="reason"/> is empty.</exception>
    public static FileReadResult Failure(FileAccessStatus status, string reason)
    {
        if (status == FileAccessStatus.Success)
        {
            throw new ArgumentException("A failure result cannot use Success.", nameof(status));
        }

        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new(false, ReadOnlyMemory<byte>.Empty, status, reason);
    }
}