// <copyright file="AppleFileSystemContractTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

/// <summary>
/// Path rules, file-entry immutability, and the layout predicates the readers share.
/// </summary>
[TestFixture]
public class AppleFileSystemContractTests
{
    /// <summary>
    /// A <c>with</c> copy does not change the original entry.
    /// </summary>
    [Test]
    public void FileEntry_IsImmutable()
    {
        var entry = new FileEntry("NOTE", "NOTE", Dos33Layout.FileTypeText, 0, 256, 2, 256, false, false, FileStorageKind.DosTrackSectorList);

        var copy = entry with { IsLocked = true };

        Assert.That(entry.IsLocked, Is.False);
        Assert.That(copy.IsLocked, Is.True);
        Assert.That(copy, Is.Not.EqualTo(entry));
    }

    /// <summary>
    /// Empty names and negative sizes are rejected.
    /// </summary>
    [Test]
    public void FileEntry_RejectsEmptyNamesAndNegativeSizes()
    {
        Assert.Throws<ArgumentException>(() => new FileEntry(string.Empty, "A", 0, 0, 0, 0, 256, false, false, FileStorageKind.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileEntry("A", "A", 0, 0, -1, 0, 256, false, false, FileStorageKind.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileEntry("A", "A", 0, 0, 0, 0, 0, false, false, FileStorageKind.None));
    }

    /// <summary>
    /// Root paths, absolute volume prefixes, and DOS names that look like a volume number.
    /// </summary>
    [Test]
    public void Paths_StripOnlyAnAbsoluteVolumePrefix()
    {
        var root = AppleFileSystemPaths.TryParse(null, "SAMPLE", out var rootSegments, out _);
        Assume.That(root, Is.True);
        Assert.That(rootSegments, Is.Empty);

        Assert.That(Parse("/", "SAMPLE"), Is.Empty);
        Assert.That(Parse("///", "SAMPLE"), Is.Empty);
        Assert.That(Parse("NOTE", "SAMPLE"), Is.EqualTo(new[] { "NOTE" }));
        Assert.That(Parse("/SAMPLE/NOTE", "SAMPLE"), Is.EqualTo(new[] { "NOTE" }));
        Assert.That(Parse("/sample/Note", "SAMPLE"), Is.EqualTo(new[] { "Note" }));
        Assert.That(Parse("/SAMPLE", "SAMPLE"), Is.Empty);
        Assert.That(Parse("SAMPLE/NOTE", "SAMPLE"), Is.EqualTo(new[] { "SAMPLE", "NOTE" }));
        Assert.That(Parse("254", "254"), Is.EqualTo(new[] { "254" }));
        Assert.That(Parse("/254/HELLO", "254"), Is.EqualTo(new[] { "HELLO" }));
        Assert.That(Parse("SUB/", "SAMPLE"), Is.EqualTo(new[] { "SUB" }));
        Assert.That(AppleFileSystemPaths.Child(string.Empty, "NOTE"), Is.EqualTo("NOTE"));
        Assert.That(AppleFileSystemPaths.Child("SUB", "NOTE"), Is.EqualTo("SUB/NOTE"));
    }

    /// <summary>
    /// Empty segments and parent references are illegal paths.
    /// </summary>
    [Test]
    public void Paths_RejectEmptySegmentsAndParentReferences()
    {
        Assert.That(AppleFileSystemPaths.TryParse("A//B", "SAMPLE", out _, out var empty), Is.False);
        Assert.That(empty, Does.Contain("empty"));
        Assert.That(AppleFileSystemPaths.TryParse("..", "SAMPLE", out _, out _), Is.False);
        Assert.That(AppleFileSystemPaths.TryParse("A/../B", "SAMPLE", out _, out _), Is.False);
        Assert.That(AppleFileSystemPaths.TryParse("A\0B", "SAMPLE", out _, out _), Is.False);
        Assert.Throws<ArgumentException>(() => AppleFileSystemPaths.TryParse("A", string.Empty, out _, out _));
    }

    /// <summary>
    /// DOS logical-view header lengths are fixed for A, I, and B files.
    /// </summary>
    [Test]
    public void DosLogicalHeaderLength_StripsBinaryAndBasicOnly()
    {
        Assert.That(Dos33Layout.LogicalHeaderLength(Dos33Layout.FileTypeBinary), Is.EqualTo(4));
        Assert.That(Dos33Layout.LogicalHeaderLength(Dos33Layout.FileTypeApplesoft), Is.EqualTo(2));
        Assert.That(Dos33Layout.LogicalHeaderLength(Dos33Layout.FileTypeInteger), Is.EqualTo(2));
        Assert.That(Dos33Layout.LogicalHeaderLength((byte)(Dos33Layout.FileTypeBinary | Dos33Layout.LockedMask)), Is.EqualTo(4));
        Assert.That(Dos33Layout.LogicalHeaderLength(Dos33Layout.FileTypeText), Is.EqualTo(0));
    }

    /// <summary>
    /// Index pointers come from the low half and the high half, not from adjacent pairs.
    /// </summary>
    [Test]
    public void ProdosIndexPointer_UsesLowHalfThenHighHalf()
    {
        var block = new byte[512];
        block[2] = 4;
        block[258] = 1;
        Assume.That(block.Length, Is.EqualTo(512));

        Assert.That(ProdosLayout.ReadIndexPointer(block, 2), Is.EqualTo(260));
        Assert.That(ProdosLayout.ReadIndexPointer(block, 1), Is.EqualTo(0));
        Assert.That(block[5], Is.EqualTo(0));
    }

    /// <summary>
    /// Failure results cannot claim success, and they carry no bytes.
    /// </summary>
    [Test]
    public void FileReadResult_FailureCarriesNoBytes()
    {
        var result = FileReadResult.Failure(FileAccessStatus.UnsupportedStorage, "Storage type $5 is not read.");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Data.Length, Is.EqualTo(0));
        Assert.That(result.Reason, Does.Contain("$5"));
        Assert.Throws<ArgumentException>(() => FileReadResult.Failure(FileAccessStatus.Success, "no"));
        var empty = FileReadResult.Success(ReadOnlyMemory<byte>.Empty);
        Assert.That(empty.Succeeded, Is.True);
    }

    private static IReadOnlyList<string> Parse(string path, string volume)
    {
        var parsed = AppleFileSystemPaths.TryParse(path, volume, out var segments, out _);
        Assume.That(parsed, Is.True);
        return segments;
    }
}