// <copyright file="IFileLoadTracker.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// File-load timeline for one guest-OS session.
/// </summary>
/// <remarks>
/// <para>
/// The MLI tracker fills this from ProDOS calls. Pre-MLI block and sector progress (#700)
/// feeds the same rows through <see cref="Report"/>, so a boot is one timeline.
/// The text of each row is <see cref="FileLoadProgress.FormatLine"/>.
/// The console (<c>os files</c>), the status monitor, and <c>get_file_activity</c> all read this list.
/// </para>
/// <para>
/// A row with <see cref="FileLoadProgress.RefNum"/> replaces the same ref.
/// A row without a ref replaces the same <see cref="FileLoadProgress.Path"/>.
/// The machine does not register an implementation when the boot log is off, so the instruction loop gains nothing.
/// </para>
/// </remarks>
public interface IFileLoadTracker
{
    /// <summary>Raised when <see cref="Files"/> or <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the rows in the order they were first seen.</summary>
    /// <value>The timeline, oldest first. Finished loads stay until the session ends.</value>
    IReadOnlyList<FileLoadProgress> Files { get; }

    /// <summary>Gets the file currently being loaded.</summary>
    /// <value>The active row most recently updated, or <see langword="null"/> when nothing is in progress.</value>
    FileLoadProgress? Current { get; }

    /// <summary>
    /// Inserts or replaces one row.
    /// </summary>
    /// <param name="progress">The row to store. A ref matches an existing ref; a null ref matches the path.</param>
    void Report(FileLoadProgress progress);

    /// <summary>Drops every row. The guest-OS session calls this when the session ends.</summary>
    void Clear();
}