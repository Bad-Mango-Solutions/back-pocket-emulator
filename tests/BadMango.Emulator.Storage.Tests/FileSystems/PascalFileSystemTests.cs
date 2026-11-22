// <copyright file="PascalFileSystemTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Unit.Components;

using Serilog;

/// <summary>
/// Apple Pascal reader tests. Images are built in the test process.
/// </summary>
[TestFixture]
public class PascalFileSystemTests
{
    /// <summary>
    /// A header that is not Pascal is rejected by the constructor and by the probe.
    /// </summary>
    [Test]
    public void Constructor_RejectsANonPascalHeader()
    {
        var arbitrary = new byte[PascalLayout.FirstDataBlock * PascalLayout.BlockSize];
        arbitrary.AsSpan().Fill(0xA5);
        AssertRejected(new MemoryBlockReader(arbitrary, PascalLayout.FirstDataBlock, PascalLayout.BlockSize));

        var nearMiss = PascalFixture.CreateBlank();
        var nearMissImage = nearMiss.Image.ToArray();
        EditDirectory(nearMissImage, directory => WriteU16(directory, PascalLayout.VolumeHeader.NextBlockOffset, PascalLayout.FirstDataBlock - 1));
        Assume.That(PascalLayout.IsVolumeHeader(nearMiss.ReadDirectory(), nearMiss.BlockCount), Is.True);
        Assume.That(PascalLayout.IsVolumeHeader(DirectoryOf(nearMissImage), nearMiss.BlockCount), Is.False);
        AssertRejected(new MemoryBlockReader(nearMissImage, nearMiss.BlockCount, PascalLayout.BlockSize));

        var prodos = ProdosFixture.CreateBlank(16);
        Assume.That(prodos.BlockCount, Is.GreaterThanOrEqualTo(PascalLayout.FirstDataBlock));
        AssertRejected(prodos.CreateBlockReader());
    }

    /// <summary>
    /// The recorded block count has to equal the image. A larger or smaller container is not this volume.
    /// </summary>
    [Test]
    public void BlockCount_MustMatchTheVolumeHeader()
    {
        var blank = PascalFixture.CreateBlank(8);
        Assume.That(blank.BlockCount, Is.EqualTo(8));
        var larger = new byte[(blank.BlockCount + 1) * PascalLayout.BlockSize];
        blank.Image.Span.CopyTo(larger);
        var smaller = blank.Image.Slice(0, (blank.BlockCount - 1) * PascalLayout.BlockSize).ToArray();

        AssertRejected(new MemoryBlockReader(larger, blank.BlockCount + 1, PascalLayout.BlockSize));
        AssertRejected(new MemoryBlockReader(smaller, blank.BlockCount - 1, PascalLayout.BlockSize));
    }

    /// <summary>
    /// A short image and a non-512 block size are rejected before any block is read.
    /// </summary>
    [Test]
    public void Probe_ShortOrWrongSizedImage_RejectsWithoutReading()
    {
        var shortImage = new ScriptedBlockReader(PascalLayout.FirstDataBlock - 1);
        var shortAttempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(shortImage, null));
        Assert.That(shortAttempt.Recognized, Is.False);
        Assert.That(shortAttempt.FileSystem, Is.Null);
        Assert.That(shortAttempt.Reason, Is.EqualTo("The image is shorter than an Apple Pascal directory."));
        Assert.That(shortImage.Reads, Is.EqualTo(0));
        var shortError = Assert.Throws<ArgumentException>(() => new PascalFileSystem(Logger(), shortImage));
        Assert.That(shortError, Is.TypeOf<ArgumentException>());
        Assert.That(shortImage.Reads, Is.EqualTo(0));

        var wrongSize = new ScriptedBlockReader(blockCount: 20, blockSize: 256);
        var wrongAttempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(wrongSize, null));
        Assert.That(wrongAttempt.Recognized, Is.False);
        Assert.That(wrongAttempt.Reason, Is.EqualTo($"Apple Pascal blocks are {PascalLayout.BlockSize} bytes."));
        Assert.That(wrongSize.Reads, Is.EqualTo(0));
        var wrongError = Assert.Throws<ArgumentException>(() => new PascalFileSystem(Logger(), wrongSize));
        Assert.That(wrongError, Is.TypeOf<ArgumentException>());
        Assert.That(wrongSize.Reads, Is.EqualTo(0));
    }

    /// <summary>
    /// A sector-only source is not a Pascal volume.
    /// </summary>
    [Test]
    public void Probe_WithoutABlockReader_Rejects()
    {
        var sectors = new MemorySectorReader(new byte[256], 1, 1, 256);
        var attempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(null, sectors));

        Assert.That(attempt.Recognized, Is.False);
        Assert.That(attempt.FileSystem, Is.Null);
        Assert.That(attempt.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        Assert.That(attempt.Reason, Is.EqualTo("Apple Pascal needs a block reader."));
    }

    /// <summary>
    /// Block-read failures propagate. They are not rewritten as a rejected probe.
    /// </summary>
    [Test]
    public void Probe_WhenBlockReadThrows_PropagatesTheException()
    {
        var errors = new Exception[]
        {
            new ArgumentException("bad argument"),
            new ArgumentOutOfRangeException("block", "out of range"),
            new InvalidOperationException("refused"),
            new IOException("unreadable"),
            new ObjectDisposedException("image"),
        };

        foreach (var error in errors)
        {
            var reader = new ScriptedBlockReader(PascalLayout.FirstDataBlock, error: error);
            var probe = new PascalFileSystemProbe(Logger());

            var thrown = Assert.Throws(error.GetType(), () => probe.TryOpen(new FileSystemSource(reader, null)));

            Assert.That(thrown, Is.SameAs(error));
            Assert.That(reader.Reads, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// The probe opens from the directory it already read, so the blocks are not read again.
    /// </summary>
    [Test]
    public void Probe_OpensFromTheDirectoryItAlreadyRead()
    {
        var reader = new ScriptedBlockReader(PascalLayout.FirstDataBlock, headerOnce: true);
        Assume.That(reader.BlockCount, Is.EqualTo(PascalLayout.FirstDataBlock));

        var attempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(reader, null));

        Assert.That(attempt.Recognized, Is.True);
        Assert.That(attempt.FileSystem, Is.Not.Null);
        Assert.That(attempt.FileSystem!.VolumeName, Is.EqualTo("BLANK"));
        Assert.That(attempt.FileSystem.ListDirectory(null).Entries, Is.Empty);
        Assert.That(reader.Reads, Is.EqualTo(PascalLayout.DirectoryBlockCount));
    }

    /// <summary>
    /// A synthetic catalog lists the 26-byte records, and both read views return the file bytes.
    /// </summary>
    [Test]
    public void CatalogSample_ListsRecordsAndReadsFileBytes()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        Assume.That(fixture.Entries, Has.Count.EqualTo(2));
        Assume.That(fixture.Entries[0].Name, Is.EqualTo("NOTE.TEXT"));
        Assume.That(fixture.Entries[1].Data.Length, Is.EqualTo(600));
        Assume.That(PascalLayout.IsVolumeHeader(fixture.ReadDirectory(), fixture.BlockCount), Is.True);
        var fs = Open(fixture);

        Assert.That(fs.VolumeName, Is.EqualTo("SAMPLE"));
        Assert.That(fs.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        var listing = fs.ListDirectory(null);
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.Success));
        Assert.That(listing.Entries, Has.Count.EqualTo(2));
        AssertFile(listing.Entries[0], "NOTE.TEXT", PascalFileKind.Text, fixture.Entries[0].Data.Length, 1);
        AssertFile(listing.Entries[1], "UNIT.CODE", PascalFileKind.Code, 600, 2);
        Assert.That(fs.GetFileInfo("NOTE.TEXT"), Is.EqualTo(listing.Entries[0]));

        var code = fs.TryReadFile("UNIT.CODE", FileReadView.Raw);
        var note = fs.TryReadFile("note.text", FileReadView.Logical);
        Assert.That(code.Succeeded, Is.True);
        Assert.That(code.Reason, Is.EqualTo("ok"));
        Assert.That(code.Data.ToArray(), Is.EqualTo(fixture.Entries[1].Data.ToArray()));
        Assert.That(code.Data.Length, Is.EqualTo(600));
        Assert.That(note.Succeeded, Is.True);
        Assert.That(note.Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));
        Assert.That(note.Data.Length, Is.EqualTo(fixture.Entries[0].Data.Length));
        Assert.That(fs.TryReadFile("/UNIT.CODE", FileReadView.Logical).Data.ToArray(), Is.EqualTo(code.Data.ToArray()));
    }

    /// <summary>
    /// An empty volume lists no files. The volume header is not an entry.
    /// </summary>
    [Test]
    public void EmptyVolume_ListsNoFiles()
    {
        var fs = Open(PascalFixture.CreateBlank());

        Assert.That(fs.VolumeName, Is.EqualTo("BLANK"));
        Assert.That(fs.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        var listing = fs.ListDirectory("/");
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries, Is.Empty);
        Assert.That(fs.GetFileInfo("BLANK"), Is.Null);
        Assert.That(fs.GetFileInfo("/BLANK"), Is.Null);
        Assert.That(fs.TryReadFile("BLANK", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        var root = fs.TryReadFile("/BLANK", FileReadView.Logical);
        Assert.That(root.Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(root.Data.Length, Is.EqualTo(0));
        Assert.That(root.Succeeded, Is.False);
    }

    /// <summary>
    /// Flat paths keep a slash inside the name, ignore case, and treat only the volume root as a directory.
    /// </summary>
    [Test]
    public void Paths_AreFlatAndCaseInsensitive()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        var fs = Open(fixture);
        Assume.That(fs.ListDirectory(null).Entries, Has.Count.EqualTo(2));

        Assert.That(fs.ListDirectory(null).Succeeded, Is.True);
        Assert.That(fs.ListDirectory(string.Empty).Entries, Has.Count.EqualTo(2));
        Assert.That(fs.ListDirectory("/").Entries, Has.Count.EqualTo(2));
        Assert.That(fs.ListDirectory("///").Entries, Has.Count.EqualTo(2));
        Assert.That(fs.ListDirectory("/SAMPLE").Entries, Has.Count.EqualTo(2));
        Assert.That(fs.ListDirectory("/sample").Entries, Has.Count.EqualTo(2));
        Assert.That(fs.ListDirectory("SAMPLE").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.ListDirectory("NOTE.TEXT").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.ListDirectory("note.text").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.ListDirectory("MISSING").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.ListDirectory(".").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.ListDirectory("NOTE.TEXT/EXTRA").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.ListDirectory("A\0B").Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.GetFileInfo("/"), Is.Null);
        Assert.That(fs.GetFileInfo(string.Empty), Is.Null);
        Assert.That(fs.GetFileInfo("/SAMPLE"), Is.Null);
        Assert.That(fs.GetFileInfo("missing"), Is.Null);
        Assert.That(fs.GetFileInfo("A\0B"), Is.Null);
        Assert.That(fs.TryReadFile(string.Empty, FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile("/", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile("/sample", FileReadView.Logical).Status, Is.EqualTo(FileAccessStatus.IsDirectory));
        Assert.That(fs.TryReadFile("SAMPLE", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile("MISSING", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile("A\0B", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.InvalidPath));
        Assert.That(fs.TryReadFile("A\0B", FileReadView.Raw).Data.Length, Is.EqualTo(0));
        Assert.Throws<ArgumentNullException>(() => fs.GetFileInfo(null!));
        Assert.Throws<ArgumentNullException>(() => fs.TryReadFile(null!, FileReadView.Raw));
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.TryReadFile("NOTE.TEXT", (FileReadView)99));

        var text = fs.TryReadFile("Note.Text", FileReadView.Raw);
        Assert.That(text.Succeeded, Is.True);
        Assert.That(text.Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));
    }

    /// <summary>
    /// A slash in a Pascal name stays one file name.
    /// </summary>
    [Test]
    public void FlatPath_KeepsASlashInsideTheFileName()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory =>
        {
            var nameAt = PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset;
            directory.AsSpan(nameAt, PascalLayout.DirectoryEntry.NameFieldLength).Clear();
            directory[nameAt] = 3;
            directory[nameAt + 1] = (byte)'A';
            directory[nameAt + 2] = (byte)'/';
            directory[nameAt + 3] = (byte)'B';
        });
        var fs = Open(image, fixture.BlockCount);

        Assert.That(fs.GetFileInfo("A/B")!.Name, Is.EqualTo("A/B"));
        Assert.That(fs.GetFileInfo("/A/B")!.Path, Is.EqualTo("A/B"));
        Assert.That(fs.ListDirectory("A/B").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.ListDirectory("/A/B").Status, Is.EqualTo(FileAccessStatus.NotADirectory));
        Assert.That(fs.ListDirectory("A").Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile("A/B", FileReadView.Raw).Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));
        Assert.That(fs.TryReadFile("/A/B", FileReadView.Logical).Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));
        Assert.That(fs.TryReadFile("NOTE.TEXT", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
        Assert.That(fs.TryReadFile("UNIT.CODE", FileReadView.Raw).Succeeded, Is.True);
    }

    /// <summary>
    /// The file count is the number of records. Later bytes are not extra files.
    /// </summary>
    [Test]
    public void FileCount_LimitsTheDirectory()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        Assume.That(fixture.Entries, Has.Count.EqualTo(2));
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory => WriteU16(directory, PascalLayout.VolumeHeader.FileCountOffset, 1));
        var fs = Open(image, fixture.BlockCount);

        var listing = fs.ListDirectory(null);
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "NOTE.TEXT" }));
        Assert.That(fs.TryReadFile("NOTE.TEXT", FileReadView.Raw).Succeeded, Is.True);
        Assert.That(fs.TryReadFile("UNIT.CODE", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.NotFound));
    }

    /// <summary>
    /// Two records that differ only by case are both listed. Lookup returns the first.
    /// </summary>
    [Test]
    public void DuplicateNames_ListBoth_AndReadTheFirst()
    {
        var builder = new PascalImageBuilder(8, "SAMPLE");
        builder.AddFile("NOTE.TEXT", PascalFileKind.Text, "ONE"u8.ToArray());
        builder.AddFile("NOTE.TEXT", PascalFileKind.Text, "TWO"u8.ToArray());
        var image = builder.Build().Image.ToArray();
        EditDirectory(image, directory =>
        {
            var letter = (2 * PascalLayout.EntryLength) + PascalLayout.DirectoryEntry.NameOffset + 1;
            directory[letter] = (byte)'n';
        });
        var fs = Open(image, 8);

        var listing = fs.ListDirectory(null);
        Assert.That(listing.Succeeded, Is.True);
        Assert.That(listing.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "NOTE.TEXT", "nOTE.TEXT" }));
        Assert.That(fs.GetFileInfo("note.text")!.Name, Is.EqualTo("NOTE.TEXT"));
        Assert.That(fs.TryReadFile("note.text", FileReadView.Raw).Data.ToArray(), Is.EqualTo("ONE"u8.ToArray()));
    }

    /// <summary>
    /// Every kind nibble is the file type. The Filer bit is ignored, and a secure file is still a file.
    /// </summary>
    [Test]
    public void KindWord_UsesTheLowNibble_AndSecureIsAFile()
    {
        var kinds = Enum.GetValues<PascalFileKind>();
        var builder = new PascalImageBuilder(PascalLayout.FirstDataBlock + kinds.Length, "KINDS");
        foreach (var kind in kinds)
        {
            builder.AddFile(kind.ToString().ToUpperInvariant(), kind, new[] { (byte)kind });
        }

        var image = builder.Build().Image.ToArray();
        var textRecord = ((int)PascalFileKind.Text + 1) * PascalLayout.EntryLength;
        EditDirectory(image, directory =>
        {
            var kind = (int)PascalFileKind.Text | PascalLayout.FilerWildcardBit | 0x0FF0;
            WriteU16(directory, textRecord + PascalLayout.DirectoryEntry.KindOffset, kind);
        });
        var fs = Open(image, PascalLayout.FirstDataBlock + kinds.Length);

        foreach (var kind in kinds)
        {
            var info = fs.GetFileInfo(kind.ToString());
            Assert.That(info, Is.Not.Null, kind.ToString());
            Assert.That(info!.FileType, Is.EqualTo((byte)kind));
            Assert.That(info.IsDirectory, Is.False);
            Assert.That(info.IsLocked, Is.False);
            Assert.That(info.StorageKind, Is.EqualTo(FileStorageKind.PascalContiguous));
            var read = fs.TryReadFile(kind.ToString(), FileReadView.Logical);
            Assert.That(read.Succeeded, Is.True);
            Assert.That(read.Data.ToArray(), Is.EqualTo(new[] { (byte)kind }));
        }
    }

    /// <summary>
    /// A full last block is 512 bytes, including the second block of a 1024-byte file.
    /// </summary>
    [Test]
    public void FullLastBlock_Is512Bytes()
    {
        var one = new byte[PascalLayout.BlockSize];
        one[0] = 0x11;
        one[PascalLayout.BlockSize - 1] = 0x22;
        var two = new byte[PascalLayout.BlockSize * 2];
        two[0] = 0x33;
        two[two.Length - 1] = 0x44;
        var builder = new PascalImageBuilder(PascalLayout.FirstDataBlock + 3, "FULL");
        builder.AddFile("ONE", PascalFileKind.Data, one);
        builder.AddFile("TWO", PascalFileKind.Data, two);
        var fixture = builder.Build();
        var directory = fixture.ReadDirectory();
        Assume.That(ReadU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.LastBlockBytesOffset), Is.EqualTo(PascalLayout.BlockSize));
        Assume.That(ReadU16(directory, (2 * PascalLayout.EntryLength) + PascalLayout.DirectoryEntry.LastBlockBytesOffset), Is.EqualTo(PascalLayout.BlockSize));
        var fs = Open(fixture);

        var first = fs.TryReadFile("ONE", FileReadView.Raw);
        var second = fs.TryReadFile("TWO", FileReadView.Logical);
        Assert.That(first.Data.ToArray(), Is.EqualTo(one));
        Assert.That(second.Data.ToArray(), Is.EqualTo(two));
        AssertFile(fs.GetFileInfo("ONE")!, "ONE", PascalFileKind.Data, PascalLayout.BlockSize, 1);
        AssertFile(fs.GetFileInfo("TWO")!, "TWO", PascalFileKind.Data, PascalLayout.BlockSize * 2, 2);
    }

    /// <summary>
    /// A last-block count of zero is a corrupt record. It is not an empty or full block.
    /// </summary>
    [Test]
    public void ZeroLastBlockBytes_DropsTheLastBlock()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        var code = fixture.Entries[1];
        Assume.That(code.Name, Is.EqualTo("UNIT.CODE"));
        Assume.That(code.EndBlock - code.StartBlock, Is.EqualTo(2));
        Assume.That(code.BytesInLastBlock, Is.GreaterThan(0).And.LessThan(PascalLayout.BlockSize));
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory => WriteU16(directory, (2 * PascalLayout.EntryLength) + PascalLayout.DirectoryEntry.LastBlockBytesOffset, 0));
        var fs = Open(image, fixture.BlockCount);

        var listing = fs.ListDirectory(null);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Reason, Does.Contain("last block"));
        var read = fs.TryReadFile("UNIT.CODE", FileReadView.Raw);
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Succeeded, Is.False);
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.GetFileInfo("UNIT.CODE"), Is.Null);
    }

    /// <summary>
    /// A span that does not extend past its start is corrupt and does not read a data block.
    /// </summary>
    [Test]
    public void EmptySpan_ReadsAsAnEmptyFile()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory =>
        {
            var start = ReadU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.FirstBlockOffset);
            Assume.That(start, Is.GreaterThanOrEqualTo(PascalLayout.FirstDataBlock));
            WriteU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NextBlockOffset, start);
        });
        var reader = new DataThrowingReader(new MemoryBlockReader(image, fixture.BlockCount, PascalLayout.BlockSize), new IOException("data"));
        var fs = new PascalFileSystem(Logger(), reader);

        var listing = fs.ListDirectory(null);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Reason, Does.Contain("starts"));
        var read = fs.TryReadFile("NOTE.TEXT", FileReadView.Logical);
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.GetFileInfo("NOTE.TEXT"), Is.Null);
        Assert.That(fs.TryReadFile("UNIT.CODE", FileReadView.Raw).Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(reader.DataReads, Is.EqualTo(0));
    }

    /// <summary>
    /// A file of three blocks returns every byte, including the partial last block.
    /// </summary>
    [Test]
    public void ThreeBlockFile_ReadsEveryBlock()
    {
        var data = new byte[(PascalLayout.BlockSize * 2) + 40];
        data[0] = 0x10;
        data[PascalLayout.BlockSize] = 0x21;
        data[PascalLayout.BlockSize * 2] = 0x32;
        data[data.Length - 1] = 0x43;
        var builder = new PascalImageBuilder(PascalLayout.FirstDataBlock + 3, "WIDE");
        builder.AddFile("LONG", PascalFileKind.Data, data);
        var fixture = builder.Build();
        Assume.That(fixture.Entries[0].EndBlock - fixture.Entries[0].StartBlock, Is.EqualTo(3));
        Assume.That(fixture.Entries[0].BytesInLastBlock, Is.EqualTo(40));
        var fs = Open(fixture);

        var raw = fs.TryReadFile("LONG", FileReadView.Raw);
        var logical = fs.TryReadFile("LONG", FileReadView.Logical);
        Assert.That(raw.Succeeded, Is.True);
        Assert.That(raw.Data.ToArray(), Is.EqualTo(data));
        Assert.That(logical.Data.ToArray(), Is.EqualTo(data));
        AssertFile(fs.GetFileInfo("LONG")!, "LONG", PascalFileKind.Data, data.Length, 3);
    }

    /// <summary>
    /// A space is a legal name byte. The bytes beside that range are not.
    /// </summary>
    [Test]
    public void SpaceInFileName_IsReadable()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        Assume.That(fixture.Entries[0].Name, Is.EqualTo("NOTE.TEXT"));
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory => directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset + 1] = 0x20);
        var fs = Open(image, fixture.BlockCount);

        var info = fs.GetFileInfo(" ote.text");
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Name, Is.EqualTo(" OTE.TEXT"));
        Assert.That(fs.TryReadFile(" OTE.TEXT", FileReadView.Raw).Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));
    }

    /// <summary>
    /// A name length past the 15-character field is a corrupt record.
    /// </summary>
    [Test]
    public void NameLength16_IsCorruptStructure()
    {
        AssertCorrupt(
            directory => directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset] = 16,
            "15");
    }

    /// <summary>
    /// A header that claims more than 77 files is not this Pascal volume.
    /// </summary>
    [Test]
    public void FileCountAboveMaxFiles_IsRejected()
    {
        var fixture = PascalFixture.CreateBlank();
        Assume.That(PascalLayout.IsVolumeHeader(fixture.ReadDirectory(), fixture.BlockCount), Is.True);
        var image = fixture.Image.ToArray();
        EditDirectory(image, directory => WriteU16(directory, PascalLayout.VolumeHeader.FileCountOffset, PascalLayout.MaxFiles + 1));
        Assume.That(PascalLayout.MaxFiles + 1, Is.EqualTo(78));
        Assume.That(PascalLayout.IsVolumeHeader(DirectoryOf(image), fixture.BlockCount), Is.False);

        AssertRejected(new MemoryBlockReader(image, fixture.BlockCount, PascalLayout.BlockSize));
    }

    /// <summary>
    /// Records are packed across the block boundary between directory blocks 2 and 3.
    /// </summary>
    [Test]
    public void Directory_PacksEntriesAcrossBlockBoundaries()
    {
        const int count = 20;
        var builder = new PascalImageBuilder(PascalLayout.FirstDataBlock + count, "CROSS");
        for (var i = 0; i < count; i++)
        {
            builder.AddFile($"F{i:00}", PascalFileKind.Data, new[] { (byte)i });
        }

        var fixture = builder.Build();
        var directory = fixture.ReadDirectory();
        var entryOffset = 19 * PascalLayout.EntryLength;
        Assume.That(entryOffset, Is.LessThan(PascalLayout.BlockSize));
        Assume.That(entryOffset + PascalLayout.DirectoryEntry.LastBlockBytesOffset, Is.GreaterThanOrEqualTo(PascalLayout.BlockSize));
        Assume.That(directory[entryOffset + PascalLayout.DirectoryEntry.NameOffset], Is.EqualTo(3));
        var fs = Open(fixture);

        Assert.That(fs.ListDirectory(null).Entries.Select(entry => entry.Name).Last(), Is.EqualTo("F19"));
        Assert.That(fs.ListDirectory(string.Empty).Entries, Has.Count.EqualTo(count));
        var straddling = fs.GetFileInfo("F18");
        Assert.That(straddling!.EofBytes, Is.EqualTo(1));
        Assert.That(fs.TryReadFile("f18", FileReadView.Raw).Data.ToArray(), Is.EqualTo(new[] { (byte)18 }));
        Assert.That(fs.TryReadFile("F19", FileReadView.Logical).Data.ToArray(), Is.EqualTo(new[] { (byte)19 }));
    }

    /// <summary>
    /// A counted record that is not a file makes the catalog unreadable.
    /// </summary>
    [Test]
    public void MalformedFileRecord_IsCorruptStructure()
    {
        AssertCorrupt(
            directory => directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset] = 0,
            "name");
        AssertCorrupt(
            directory => WriteU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NextBlockOffset, 0),
            "starts");
        AssertCorrupt(
            directory => WriteU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.LastBlockBytesOffset, PascalLayout.BlockSize + 1),
            "last block");
        AssertCorrupt(
            directory => WriteU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NextBlockOffset, 1000),
            "outside");
        AssertCorrupt(
            directory => WriteU16(directory, PascalLayout.VolumeHeader.FileCountOffset, 3),
            "name");
        AssertCorrupt(
            directory => directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset + 1] = 0x1F,
            "0x20");
        AssertCorrupt(
            directory => directory[PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NameOffset + 1] = 0x7F,
            "0x7E");
        AssertCorrupt(
            directory =>
            {
                var end = ReadU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NextBlockOffset);
                Assume.That(end, Is.GreaterThan(PascalLayout.FirstDataBlock));
                WriteU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.FirstBlockOffset, PascalLayout.FirstDataBlock - 1);
            },
            "directory");
        AssertCorrupt(
            directory =>
            {
                var previousEnd = ReadU16(directory, PascalLayout.EntryLength + PascalLayout.DirectoryEntry.NextBlockOffset);
                Assume.That(previousEnd, Is.GreaterThan(PascalLayout.FirstDataBlock));
                WriteU16(directory, (2 * PascalLayout.EntryLength) + PascalLayout.DirectoryEntry.FirstBlockOffset, previousEnd - 1);
            },
            "previous");
    }

    /// <summary>
    /// Failures from a data-block read propagate after the volume has opened.
    /// </summary>
    [Test]
    public void FileRead_WhenBlockReadThrows_PropagatesTheException()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        Assume.That(fixture.Entries[0].StartBlock, Is.GreaterThanOrEqualTo(PascalLayout.FirstDataBlock));
        var errors = new Exception[]
        {
            new ArgumentOutOfRangeException("block", "out of range"),
            new InvalidOperationException("refused"),
            new IOException("unreadable"),
            new ObjectDisposedException("image"),
        };

        foreach (var error in errors)
        {
            var fs = new PascalFileSystem(Logger(), new DataThrowingReader(fixture.CreateBlockReader(), error));
            Assume.That(fs.ListDirectory(null).Succeeded, Is.True);

            var thrown = Assert.Throws(error.GetType(), () => fs.TryReadFile("NOTE.TEXT", FileReadView.Raw));

            Assert.That(thrown, Is.SameAs(error));
        }
    }

    /// <summary>
    /// The detector keeps a Pascal match and records a rejection without swallowing it.
    /// </summary>
    [Test]
    public void Detector_RecordsAPascalMatchAndARejection()
    {
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { new PascalFileSystemProbe(Logger()) });
        var fixture = PascalFixture.CreateCatalogSample();

        var match = detector.Detect(new FileSystemSource(fixture.CreateBlockReader(), null));
        Assert.That(match.IsRecognized, Is.True);
        Assert.That(match.FileSystem!.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        Assert.That(match.FileSystem.VolumeName, Is.EqualTo(fixture.VolumeName));
        Assert.That(match.Attempts, Has.Count.EqualTo(1));
        Assert.That(match.Attempts[0].Recognized, Is.True);
        Assert.That(match.Attempts[0].Reason, Is.EqualTo("Apple Pascal volume header."));

        var image = new byte[PascalLayout.FirstDataBlock * PascalLayout.BlockSize];
        image.AsSpan().Fill(0xA5);
        var miss = detector.Detect(new FileSystemSource(new MemoryBlockReader(image, PascalLayout.FirstDataBlock, PascalLayout.BlockSize), null));
        Assert.That(miss.IsRecognized, Is.False);
        Assert.That(miss.FileSystem, Is.Null);
        Assert.That(miss.Attempts[0].Recognized, Is.False);
        Assert.That(miss.Attempts[0].FileSystem, Is.Null);
        Assert.That(miss.Attempts[0].Reason, Is.EqualTo("The directory does not begin with an Apple Pascal volume header."));
    }

    /// <summary>
    /// The probe opens the sample and can read it. Null arguments are rejected.
    /// </summary>
    [Test]
    public void Probe_RecognizesAPascalVolume_AndRejectsNullArguments()
    {
        var fixture = PascalFixture.CreateCatalogSample();
        Assume.That(PascalLayout.IsVolumeHeader(fixture.ReadDirectory(), fixture.BlockCount), Is.True);
        var attempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(fixture.CreateBlockReader(), null));

        Assert.That(attempt.Recognized, Is.True);
        Assert.That(attempt.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        Assert.That(attempt.FileSystem, Is.Not.Null);
        Assert.That(attempt.FileSystem!.TryReadFile("NOTE.TEXT", FileReadView.Raw).Data.ToArray(), Is.EqualTo(fixture.Entries[0].Data.ToArray()));

        var reader = fixture.CreateBlockReader();
        Assert.Throws<ArgumentNullException>(() => new PascalFileSystem(null!, reader));
        Assert.Throws<ArgumentNullException>(() => new PascalFileSystem(Logger(), null!));
        Assert.Throws<ArgumentNullException>(() => new PascalFileSystemProbe(null!));
        Assert.Throws<ArgumentNullException>(() => new PascalFileSystemProbe(Logger()).TryOpen(null!));
    }

    private static void AssertRejected(IBlockReader reader)
    {
        var error = Assert.Throws<ArgumentException>(() => new PascalFileSystem(Logger(), reader));
        Assert.That(error, Is.TypeOf<ArgumentException>());
        Assert.That(error!.Message, Does.Contain("not an Apple Pascal volume"));

        var attempt = new PascalFileSystemProbe(Logger()).TryOpen(new FileSystemSource(reader, null));
        Assert.That(attempt.Recognized, Is.False);
        Assert.That(attempt.FileSystem, Is.Null);
        Assert.That(attempt.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        Assert.That(attempt.Reason, Is.EqualTo("The directory does not begin with an Apple Pascal volume header."));
    }

    private static void AssertCorrupt(Action<byte[]> edit, string reasonPart)
    {
        var fixture = PascalFixture.CreateCatalogSample();
        var image = fixture.Image.ToArray();
        EditDirectory(image, edit);
        Assume.That(PascalLayout.IsVolumeHeader(DirectoryOf(image), fixture.BlockCount), Is.True);
        var fs = Open(image, fixture.BlockCount);

        var listing = fs.ListDirectory(null);
        Assert.That(listing.Succeeded, Is.False);
        Assert.That(listing.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(listing.Entries, Is.Empty);
        Assert.That(listing.Reason, Does.Contain(reasonPart));
        var read = fs.TryReadFile("NOTE.TEXT", FileReadView.Raw);
        Assert.That(read.Status, Is.EqualTo(FileAccessStatus.CorruptStructure));
        Assert.That(read.Succeeded, Is.False);
        Assert.That(read.Data.Length, Is.EqualTo(0));
        Assert.That(fs.GetFileInfo("NOTE.TEXT"), Is.Null);
        Assert.That(fs.VolumeName, Is.EqualTo("SAMPLE"));
        Assert.That(fs.Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
    }

    private static void AssertFile(FileEntry entry, string name, PascalFileKind kind, int eof, int blocks)
    {
        Assert.That(entry.Path, Is.EqualTo(name));
        Assert.That(entry.Name, Is.EqualTo(name));
        Assert.That(entry.FileType, Is.EqualTo((byte)kind));
        Assert.That(entry.AuxType, Is.EqualTo(0));
        Assert.That(entry.EofBytes, Is.EqualTo(eof));
        Assert.That(entry.AllocatedUnits, Is.EqualTo(blocks));
        Assert.That(entry.UnitSize, Is.EqualTo(PascalLayout.BlockSize));
        Assert.That(entry.IsLocked, Is.False);
        Assert.That(entry.IsDirectory, Is.False);
        Assert.That(entry.StorageKind, Is.EqualTo(FileStorageKind.PascalContiguous));
    }

    private static PascalFileSystem Open(PascalFixture fixture) => Open(fixture.Image, fixture.BlockCount);

    private static PascalFileSystem Open(ReadOnlyMemory<byte> image, int blockCount) =>
        new(Logger(), new MemoryBlockReader(image, blockCount, PascalLayout.BlockSize));

    private static ILogger Logger() => Generator.Log().Object;

    private static byte[] DirectoryOf(byte[] image)
    {
        var directory = new byte[PascalLayout.DirectoryBytes];
        for (var block = 0; block < PascalLayout.DirectoryBlockCount; block++)
        {
            image.AsSpan((PascalLayout.DirectoryStartBlock + block) * PascalLayout.BlockSize, PascalLayout.BlockSize)
                .CopyTo(directory.AsSpan(block * PascalLayout.BlockSize));
        }

        return directory;
    }

    private static void EditDirectory(byte[] image, Action<byte[]> edit)
    {
        var directory = DirectoryOf(image);
        edit(directory);
        for (var block = 0; block < PascalLayout.DirectoryBlockCount; block++)
        {
            directory.AsSpan(block * PascalLayout.BlockSize, PascalLayout.BlockSize)
                .CopyTo(image.AsSpan((PascalLayout.DirectoryStartBlock + block) * PascalLayout.BlockSize));
        }
    }

    private static void WriteU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static int ReadU16(byte[] data, int offset) => data[offset] | (data[offset + 1] << 8);

    private sealed class ScriptedBlockReader : IBlockReader
    {
        private readonly Exception? error;
        private readonly bool headerOnce;
        private int blockTwoReads;

        public ScriptedBlockReader(int blockCount, int blockSize = PascalLayout.BlockSize, Exception? error = null, bool headerOnce = false)
        {
            this.BlockCount = blockCount;
            this.BlockSize = blockSize;
            this.error = error;
            this.headerOnce = headerOnce;
        }

        public int BlockSize { get; }

        public int BlockCount { get; }

        public int Reads { get; private set; }

        public void ReadBlock(int blockIndex, Span<byte> destination)
        {
            this.Reads++;
            if (this.error is not null)
            {
                throw this.error;
            }

            if ((uint)blockIndex >= (uint)this.BlockCount || destination.Length != this.BlockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            destination.Clear();
            if (this.headerOnce && blockIndex == PascalLayout.DirectoryStartBlock && this.blockTwoReads++ == 0)
            {
                destination[PascalLayout.VolumeHeader.NextBlockOffset] = (byte)PascalLayout.FirstDataBlock;
                destination[PascalLayout.VolumeHeader.NameOffset] = 5;
                "BLANK"u8.CopyTo(destination.Slice(PascalLayout.VolumeHeader.NameOffset + 1));
                destination[PascalLayout.VolumeHeader.BlocksOffset] = (byte)this.BlockCount;
                destination[PascalLayout.VolumeHeader.BlocksOffset + 1] = (byte)(this.BlockCount >> 8);
            }
        }
    }

    private sealed class DataThrowingReader : IBlockReader
    {
        private readonly IBlockReader inner;
        private readonly Exception error;

        public DataThrowingReader(IBlockReader inner, Exception error)
        {
            this.inner = inner;
            this.error = error;
        }

        public int BlockSize => this.inner.BlockSize;

        public int BlockCount => this.inner.BlockCount;

        public int DataReads { get; private set; }

        public void ReadBlock(int blockIndex, Span<byte> destination)
        {
            if (blockIndex >= PascalLayout.FirstDataBlock)
            {
                this.DataReads++;
                throw this.error;
            }

            this.inner.ReadBlock(blockIndex, destination);
        }
    }
}