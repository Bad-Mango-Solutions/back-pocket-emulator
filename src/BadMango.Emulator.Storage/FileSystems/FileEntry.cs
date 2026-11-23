// <copyright file="FileEntry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// One file or subdirectory in a read-only Apple II catalog.
/// </summary>
/// <remarks>
/// <para>
/// Instances are immutable. Volume headers and ProDOS subdirectory headers are not entries.
/// Deleted DOS tracks (<c>$FF</c>) and unused slots (<c>$00</c>) are not entries.
/// </para>
/// <para>
/// <see cref="Path"/> is volume-relative, uses <c>/</c>, and has no leading slash.
/// A file in the volume root is just its name. A ProDOS file in a subdirectory is
/// <c>SUB/NAME</c>. <see cref="Name"/> is the last segment. Lookups compare segments
/// with <see cref="StringComparison.OrdinalIgnoreCase"/>. The rules live on
/// <see cref="AppleFileSystemPaths"/>.
/// </para>
/// <para>
/// <see cref="DisplayName"/> is the host form of <see cref="Name"/>. DOS 3.3 inverse and
/// flashing catalog bytes are escaped there (<see cref="AppleCatalogName"/>). ProDOS and
/// Pascal names are already printable, so <see cref="DisplayName"/> matches <see cref="Name"/>.
/// </para>
/// <para>
/// <see cref="FileType"/> is the catalog type with the DOS lock bit removed
/// (<see cref="Dos33Layout.FileTypeMask"/>, <see cref="Dos33FileType"/>). ProDOS file types
/// are <see cref="ProdosFileType"/>, not the <see cref="ProdosStorageType"/> nibble.
/// Pascal file types are <see cref="PascalFileKind"/>, bits 0–3 of the directory word.
/// <see cref="AuxType"/> is the ProDOS auxiliary type. DOS and Pascal catalogs have
/// no auxiliary type, so readers set it to 0. A DOS binary load address lives in the
/// file header, not in this field.
/// </para>
/// <para>
/// <see cref="IsLocked"/> is the DOS lock bit (type byte bit 7 at catalog offset 2),
/// or a ProDOS access byte whose write bit
/// (<see cref="ProdosLayout.WriteAccessBit"/>) is clear. Pascal entries are never locked.
/// </para>
/// <para>
/// <see cref="EofBytes"/> is the logical length <see cref="IAppleFileSystem.TryReadFile"/>
/// returns for <see cref="FileReadView.Raw"/>: the ProDOS EOF field, the Pascal block-span
/// length, or the DOS length through the last occupied track/sector pair (sparse holes
/// count as 256 zero bytes). <see cref="AllocatedUnits"/> is the catalog allocation count.
/// For DOS that count includes the track/sector list sector and excludes holes.
/// For ProDOS it is <c>blocks_used</c>, including index blocks. For Pascal it is
/// <c>end block − start block</c>. <see cref="UnitSize"/> is 256 for DOS and 512 otherwise.
/// </para>
/// </remarks>
public sealed record FileEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileEntry"/> class.
    /// </summary>
    /// <param name="path">Volume-relative path. Not empty.</param>
    /// <param name="name">Final path segment. Not empty.</param>
    /// <param name="fileType">Catalog file-type byte. The DOS lock bit is not included.</param>
    /// <param name="auxType">ProDOS auxiliary type, or 0 when the catalog has none.</param>
    /// <param name="eofBytes">Logical end-of-file in bytes. Not negative.</param>
    /// <param name="allocatedUnits">Catalog allocation count. Not negative.</param>
    /// <param name="unitSize">Bytes per allocated unit (256 or 512). Positive.</param>
    /// <param name="isLocked">Whether the catalog marks the file locked.</param>
    /// <param name="isDirectory">Whether the entry is a ProDOS subdirectory.</param>
    /// <param name="storageKind">How the bytes are stored.</param>
    /// <param name="displayName">
    /// Host form of <paramref name="name"/>. <see langword="null"/> or empty uses <paramref name="name"/>.
    /// DOS inverse and flashing names pass the escaped form from <see cref="AppleCatalogName"/>.
    /// </param>
    /// <exception cref="ArgumentException">If <paramref name="path"/> or <paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If a size is negative or <paramref name="unitSize"/> is not positive.</exception>
    public FileEntry(
        string path,
        string name,
        byte fileType,
        ushort auxType,
        int eofBytes,
        int allocatedUnits,
        int unitSize,
        bool isLocked,
        bool isDirectory,
        FileStorageKind storageKind,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(eofBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(allocatedUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitSize);

        this.Path = path;
        this.Name = name;
        this.DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        this.FileType = fileType;
        this.AuxType = auxType;
        this.EofBytes = eofBytes;
        this.AllocatedUnits = allocatedUnits;
        this.UnitSize = unitSize;
        this.IsLocked = isLocked;
        this.IsDirectory = isDirectory;
        this.StorageKind = storageKind;
    }

    /// <summary>Gets the volume-relative path.</summary>
    /// <value>The path, without a leading slash or a volume prefix.</value>
    public string Path { get; }

    /// <summary>Gets the file or directory name.</summary>
    /// <value>The last segment of <see cref="Path"/>.</value>
    public string Name { get; }

    /// <summary>Gets the host form of <see cref="Name"/>.</summary>
    /// <value>
    /// The name with DOS inverse and flashing characters escaped, or <see cref="Name"/> when
    /// the catalog has no such characters.
    /// </value>
    public string DisplayName { get; }

    /// <summary>Gets the catalog file-type byte.</summary>
    /// <value>The type. The DOS lock bit is reported by <see cref="IsLocked"/> instead.</value>
    public byte FileType { get; }

    /// <summary>Gets the ProDOS auxiliary type.</summary>
    /// <value>The auxiliary type, or 0 for DOS and Pascal.</value>
    public ushort AuxType { get; }

    /// <summary>Gets the logical end-of-file in bytes.</summary>
    /// <value>The raw-view length, including sparse holes and a DOS header.</value>
    public int EofBytes { get; }

    /// <summary>Gets the catalog allocation count.</summary>
    /// <value>DOS sectors (including the track/sector list), ProDOS blocks, or Pascal blocks.</value>
    public int AllocatedUnits { get; }

    /// <summary>Gets the size of one allocated unit in bytes.</summary>
    /// <value>256 for DOS 3.3 and 512 for ProDOS and Pascal.</value>
    public int UnitSize { get; }

    /// <summary>Gets a value indicating whether the catalog marks the entry locked.</summary>
    /// <value><see langword="true"/> when the DOS lock bit is set or the ProDOS write bit is clear.</value>
    public bool IsLocked { get; }

    /// <summary>Gets a value indicating whether the entry is a directory.</summary>
    /// <value><see langword="true"/> for a ProDOS subdirectory entry.</value>
    public bool IsDirectory { get; }

    /// <summary>Gets the on-disk storage kind.</summary>
    /// <value>The storage kind readers use to choose a decoder.</value>
    public FileStorageKind StorageKind { get; }
}