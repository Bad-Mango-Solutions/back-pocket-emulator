// <copyright file="IAppleFileSystem.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// Read-only view of one DOS 3.3, ProDOS, or Apple Pascal volume.
/// </summary>
/// <remarks>
/// <para>
/// Readers are pure functions over an <see cref="ISectorReader"/> or an <see cref="IBlockReader"/>.
/// They do not mount a drive, do not take <see cref="Func{T, TResult}"/> callbacks, and do not
/// write. There is no create, rename, delete, or save method. The reader does not own the
/// media; the caller disposes it.
/// </para>
/// <para>
/// <see cref="VolumeName"/> is the ProDOS or Pascal display name, or the DOS 3.3 volume
/// number formatted as an invariant decimal with no prefix and no leading zeros
/// (<c>254</c>, not <c>V254</c>). DOS has no text volume name. Names are the on-disk
/// bytes when the ProDOS lowercase bit field is clear. Lookups ignore case.
/// </para>
/// <para>
/// Paths follow <see cref="AppleFileSystemPaths"/>. ProDOS is hierarchical.
/// DOS 3.3 and Pascal are flat: everything after one optional leading <c>/</c> is a
/// single file name, so a name that contains <c>/</c> stays reachable.
/// <see cref="ListDirectory"/> of <see langword="null"/>, <c>""</c>, or <c>/</c> lists the
/// volume root. A ProDOS subdirectory path lists that directory. DOS and Pascal have no
/// subdirectories. The volume itself is not a <see cref="FileEntry"/>.
/// <see cref="GetFileInfo"/> of the root returns <see langword="null"/>.
/// <see cref="TryReadFile"/> of a directory returns <see cref="FileAccessStatus.IsDirectory"/>.
/// </para>
/// <para>
/// DOS catalogs read the type and lock bit from entry offset 2, not offset 0. Track
/// <c>$FF</c> is deleted and track <c>$00</c> is unused; neither is listed. File bytes
/// come from that file's track/sector list. A <c>$00 $00</c> pair before the last occupied
/// pair is a sparse hole (256 zero bytes), not the end of the file. A loop in the catalog
/// chain or the track/sector list is <see cref="FileAccessStatus.CorruptStructure"/>.
/// </para>
/// <para>
/// ProDOS reads follow the directory's next-block pointer. Seedling, sapling, and tree
/// blocks are addressed with <see cref="ProdosLayout.ReadIndexPointer"/> (low half, then
/// high half). A zero pointer is a sparse hole, not end-of-file. Output is trimmed to the
/// EOF field. Storage types <c>$5</c> and <c>$4</c> return
/// <see cref="FileAccessStatus.UnsupportedStorage"/> and no bytes.
/// </para>
/// <para>
/// The Pascal probe calls <see cref="PascalLayout.IsVolumeHeader"/> first and returns
/// <see cref="FileSystemProbeAttempt.Reject"/> with no catch. The constructor throws
/// <see cref="ArgumentException"/> only as a guard when that check was skipped.
/// <see cref="ArgumentOutOfRangeException"/> from a block read propagates.
/// Directory records are
/// <see cref="PascalLayout.EntryLength"/> bytes packed across blocks 2–5.
/// File length is <see cref="PascalLayout.FileLength"/>. The kind is
/// <see cref="PascalLayout.KindMask"/> of the kind word.
/// </para>
/// <para>
/// Media exceptions propagate. Readers do not catch
/// <see cref="ArgumentException"/>, <see cref="ArgumentOutOfRangeException"/>,
/// <see cref="InvalidOperationException"/>, <see cref="IOException"/>, or
/// <see cref="ObjectDisposedException"/> from the sector or block reader.
/// </para>
/// </remarks>
public interface IAppleFileSystem
{
    /// <summary>
    /// Gets the volume name.
    /// </summary>
    /// <value>
    /// The ProDOS or Pascal name, or the DOS 3.3 volume number as an invariant decimal.
    /// </value>
    string VolumeName { get; }

    /// <summary>
    /// Gets the volume type.
    /// </summary>
    /// <value>The file system the reader opened.</value>
    AppleFileSystemKind Kind { get; }

    /// <summary>
    /// Lists one directory.
    /// </summary>
    /// <param name="path">
    /// Volume-relative directory path. <see langword="null"/>, empty, and <c>/</c> list the root.
    /// </param>
    /// <returns>
    /// The entries in on-disk order, or a failure when the path is missing, not a directory, or illegal.
    /// </returns>
    DirectoryListing ListDirectory(string? path);

    /// <summary>
    /// Reads one file.
    /// </summary>
    /// <param name="path">Volume-relative file path. Not <see langword="null"/>.</param>
    /// <param name="view">Raw bytes, or the DOS logical view with the A/I/B header removed.</param>
    /// <returns>
    /// The trimmed bytes, or a failure for a missing path, a directory, unsupported storage, or a corrupt list.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    FileReadResult TryReadFile(string path, FileReadView view);

    /// <summary>
    /// Gets one catalog entry.
    /// </summary>
    /// <param name="path">Volume-relative path of a file or subdirectory. Not <see langword="null"/>.</param>
    /// <returns>The entry, or <see langword="null"/> when the path is missing, illegal, or the volume root.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    FileEntry? GetFileInfo(string path);
}