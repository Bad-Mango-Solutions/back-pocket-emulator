// <copyright file="FileLoadProgress.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One file's load progress. The MLI tracker and pre-MLI block progress (#700) share this row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FormatLine"/> is the text every surface prints:
/// <c>BASIC.SYSTEM 6656/10240 (65%)</c> when <see cref="Size"/> is known, and
/// <c>BASIC.SYSTEM 6656</c> when it is not. The label is the last path segment.
/// An empty path is <c>unknown</c>, which is also how an unmapped read is reported.
/// </para>
/// <para>
/// <see cref="BytesRead"/> is the running total of bytes transferred.
/// <see cref="Position"/> is the file mark, which a seek can move without adding bytes.
/// <see cref="RefNum"/> is the ProDOS ref when the row came from MLI; a pre-MLI row leaves it null
/// and is matched by <see cref="Path"/>. <see cref="Active"/> is false once that load has finished.
/// </para>
/// </remarks>
public readonly record struct FileLoadProgress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoadProgress"/> struct.
    /// </summary>
    /// <param name="path">The full path, or empty when the name is not known. Not null.</param>
    /// <param name="size">The file size in bytes, or <see langword="null"/> when it has not been seen.</param>
    /// <param name="bytesRead">Bytes transferred so far.</param>
    /// <param name="position">The file mark, in bytes from the start.</param>
    /// <param name="openedAtCycle">The cycle when this row was first reported.</param>
    /// <param name="refNum">The ProDOS ref_num, or <see langword="null"/> for a row that is not an MLI open.</param>
    /// <param name="active"><see langword="true"/> while this file is the one still being loaded.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="path"/> is <see langword="null"/>.</exception>
    public FileLoadProgress(
        string path,
        uint? size,
        uint bytesRead,
        uint position,
        ulong openedAtCycle,
        byte? refNum,
        bool active)
    {
        ArgumentNullException.ThrowIfNull(path);
        this.Path = path;
        this.Size = size;
        this.BytesRead = bytesRead;
        this.Position = position;
        this.OpenedAtCycle = openedAtCycle;
        this.RefNum = refNum;
        this.Active = active;
    }

    /// <summary>Gets the full path.</summary>
    /// <value>The path passed when the row was reported. Empty when the name is not known.</value>
    public string Path { get; }

    /// <summary>Gets the file size.</summary>
    /// <value>The size in bytes, or <see langword="null"/> when the guest has not revealed it.</value>
    public uint? Size { get; }

    /// <summary>Gets how many bytes have been transferred.</summary>
    /// <value>The running total. A seek does not change it.</value>
    public uint BytesRead { get; }

    /// <summary>Gets the file mark.</summary>
    /// <value>The position in bytes from the start of the file.</value>
    public uint Position { get; }

    /// <summary>Gets the cycle when the row was first reported.</summary>
    /// <value>The machine cycle.</value>
    public ulong OpenedAtCycle { get; }

    /// <summary>Gets the ProDOS ref_num.</summary>
    /// <value><see langword="null"/> for a pre-MLI row keyed only by <see cref="Path"/>.</value>
    public byte? RefNum { get; }

    /// <summary>Gets whether this file is still being loaded.</summary>
    /// <value><see langword="false"/> after the load finishes. The row stays in the timeline.</value>
    public bool Active { get; }

    /// <summary>Gets the name shown on a progress line.</summary>
    /// <value>
    /// The last segment of <see cref="Path"/>, or <c>unknown</c> when the path is empty.
    /// </value>
    public string Label
    {
        get
        {
            var path = this.Path;
            if (string.IsNullOrEmpty(path))
            {
                return "unknown";
            }

            var end = path.Length;
            while (end > 0 && path[end - 1] == '/')
            {
                end--;
            }

            if (end == 0)
            {
                return "unknown";
            }

            var slash = path.LastIndexOf('/', end - 1);
            return slash < 0 ? path[..end] : path[(slash + 1)..end];
        }
    }

    /// <summary>
    /// Formats the shared progress line.
    /// </summary>
    /// <returns>
    /// <c>BASIC.SYSTEM 6656/10240 (65%)</c> when <see cref="Size"/> is known, otherwise
    /// <c>BASIC.SYSTEM 6656</c>. The percent is capped at 100.
    /// </returns>
    public string FormatLine()
    {
        var label = this.Label;
        if (this.Size is not uint size)
        {
            return $"{label} {this.BytesRead}";
        }

        uint percent = size == 0 || this.BytesRead >= size
            ? 100u
            : (uint)((ulong)this.BytesRead * 100ul / size);
        return $"{label} {this.BytesRead}/{size} ({percent}%)";
    }
}