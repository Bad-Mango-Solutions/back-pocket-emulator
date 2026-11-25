// <copyright file="AppleFileSystemDetectorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.Tests.FileSystems;

using BadMango.Unit.Components;

using Serilog;

/// <summary>
/// Detector tests that use fake probes. The DOS, ProDOS, and Pascal readers are not required.
/// </summary>
[TestFixture]
public class AppleFileSystemDetectorTests
{
    /// <summary>
    /// A run records every probe, in order, including the reason for the one that matched.
    /// </summary>
    [Test]
    public void Detect_RecordsEveryAttempt_AndSelectsTheFirstMatch()
    {
        var dos = new FakeProbe(AppleFileSystemKind.Dos33, recognize: false, "VTOC version is not 3.");
        var prodos = new FakeProbe(AppleFileSystemKind.Prodos, recognize: true, "Volume header storage type is $F.");
        var pascal = new FakeProbe(AppleFileSystemKind.Pascal, recognize: false, "Directory end block is not 6.");
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { dos, prodos, pascal });

        var result = detector.Detect(Source());

        Assert.That(result.IsRecognized, Is.True);
        Assert.That(result.FileSystem, Is.SameAs(prodos.Opened));
        Assert.That(result.Attempts, Has.Count.EqualTo(3));
        Assert.That(result.Attempts[0].Kind, Is.EqualTo(AppleFileSystemKind.Dos33));
        Assert.That(result.Attempts[0].Recognized, Is.False);
        Assert.That(result.Attempts[0].Reason, Is.EqualTo("VTOC version is not 3."));
        Assert.That(result.Attempts[1].Reason, Is.EqualTo("Volume header storage type is $F."));
        Assert.That(result.Attempts[1].FileSystem, Is.SameAs(prodos.Opened));
        Assert.That(result.Attempts[2].Kind, Is.EqualTo(AppleFileSystemKind.Pascal));
        Assert.That(dos.Calls, Is.EqualTo(1));
        Assert.That(prodos.Calls, Is.EqualTo(1));
        Assert.That(pascal.Calls, Is.EqualTo(1));
    }

    /// <summary>
    /// When nothing matches, the result still names every rejection.
    /// </summary>
    [Test]
    public void Detect_WhenNothingMatches_NamesEveryRejection()
    {
        var probes = new[]
        {
            new FakeProbe(AppleFileSystemKind.Prodos, false, "No $F storage type."),
            new FakeProbe(AppleFileSystemKind.Dos33, false, "No DOS 3.3 VTOC."),
            new FakeProbe(AppleFileSystemKind.Pascal, false, "No Pascal volume header."),
        };
        var detector = new AppleFileSystemDetector(Logger(), probes);

        var result = detector.Detect(Source());

        Assert.That(result.IsRecognized, Is.False);
        Assert.That(result.FileSystem, Is.Null);
        Assert.That(result.Attempts.Select(attempt => attempt.Reason), Is.EqualTo(new[]
        {
            "No $F storage type.",
            "No DOS 3.3 VTOC.",
            "No Pascal volume header.",
        }));
    }

    /// <summary>
    /// The first recognized probe wins. A later match is recorded and not selected.
    /// </summary>
    [Test]
    public void Detect_WhenTwoProbesMatch_KeepsTheEarlierFileSystem()
    {
        var first = new FakeProbe(AppleFileSystemKind.Prodos, true, "ProDOS header.");
        var second = new FakeProbe(AppleFileSystemKind.Pascal, true, "Pascal header.");
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { first, second });

        var result = detector.Detect(Source());

        Assert.That(result.FileSystem, Is.SameAs(first.Opened));
        Assert.That(result.Attempts[1].Recognized, Is.True);
        Assert.That(result.Attempts[1].FileSystem, Is.Not.SameAs(result.FileSystem));
    }

    /// <summary>
    /// An I/O failure from a probe propagates, and later probes do not run.
    /// </summary>
    [Test]
    public void Detect_WhenProbeThrowsIOException_DoesNotSwallowIt()
    {
        var failing = new FakeProbe(AppleFileSystemKind.Prodos, false, "unused", new IOException("unreadable block"));
        var later = new FakeProbe(AppleFileSystemKind.Dos33, true, "should not run");
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { failing, later });

        var error = Assert.Throws<IOException>(() => detector.Detect(Source()));

        Assert.That(error!.Message, Is.EqualTo("unreadable block"));
        Assert.That(later.Calls, Is.EqualTo(0));
    }

    /// <summary>
    /// A probe-specific exception is not filtered into a rejection reason.
    /// </summary>
    [Test]
    public void Detect_WhenProbeThrowsCustomException_PropagatesThatException()
    {
        var failing = new FakeProbe(AppleFileSystemKind.Pascal, false, "unused", new BoomException());
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { failing });

        Assert.Throws<BoomException>(() => detector.Detect(Source()));
    }

    /// <summary>
    /// A probe that labels its attempt with a different format is a programmer error, not a rejection.
    /// </summary>
    [Test]
    public void Detect_WhenAttemptKindDisagrees_Throws()
    {
        var probe = new FakeProbe(AppleFileSystemKind.Dos33, false, "not dos", attemptKind: AppleFileSystemKind.Pascal);
        var later = new FakeProbe(AppleFileSystemKind.Prodos, true, "should not run");
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { probe, later });

        Assert.Throws<InvalidOperationException>(() => detector.Detect(Source()));
        Assert.That(later.Calls, Is.EqualTo(0));
    }

    /// <summary>
    /// A null attempt is not turned into an empty reason.
    /// </summary>
    [Test]
    public void Detect_WhenProbeReturnsNull_Throws()
    {
        var detector = new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { new NullProbe() });

        Assert.Throws<ArgumentNullException>(() => detector.Detect(Source()));
    }

    /// <summary>
    /// No probes means no attempts and no selected volume.
    /// </summary>
    [Test]
    public void Detect_WithNoProbes_ReturnsAnEmptyResult()
    {
        var detector = new AppleFileSystemDetector(Logger(), Array.Empty<IAppleFileSystemProbe>());

        var result = detector.Detect(Source());

        Assert.That(result.IsRecognized, Is.False);
        Assert.That(result.Attempts, Is.Empty);
    }

    /// <summary>
    /// Constructor arguments are required.
    /// </summary>
    [Test]
    public void Constructor_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new AppleFileSystemDetector(null!, Array.Empty<IAppleFileSystemProbe>()));
        Assert.Throws<ArgumentNullException>(() => new AppleFileSystemDetector(Logger(), null!));
        Assert.Throws<ArgumentNullException>(() => new AppleFileSystemDetector(Logger(), new IAppleFileSystemProbe[] { null! }));
        var detector = new AppleFileSystemDetector(Logger(), Array.Empty<IAppleFileSystemProbe>());
        Assert.Throws<ArgumentNullException>(() => detector.Detect(null!));
    }

    /// <summary>
    /// The recommended order is ProDOS, DOS 3.3, then Pascal, and it is not a shared mutable list.
    /// </summary>
    [Test]
    public void RecommendedProbeOrder_IsProdosThenDosThenPascal()
    {
        var first = AppleFileSystemDetector.RecommendedProbeOrder();
        var second = AppleFileSystemDetector.RecommendedProbeOrder();

        Assert.That(first, Is.EqualTo(new[] { AppleFileSystemKind.Prodos, AppleFileSystemKind.Dos33, AppleFileSystemKind.Pascal }));
        Assert.That(first, Is.Not.SameAs(second));
    }

    private static ILogger Logger() => Generator.Log().Object;

    private static FileSystemSource Source() => new(new MemoryBlockReader(new byte[512], 1, 512), null);

    private sealed class BoomException : Exception
    {
        public BoomException()
            : base("boom")
        {
        }
    }

    private sealed class NullProbe : IAppleFileSystemProbe
    {
        public AppleFileSystemKind Kind => AppleFileSystemKind.Dos33;

        public FileSystemProbeAttempt TryOpen(FileSystemSource source) => null!;
    }

    private sealed class FakeProbe : IAppleFileSystemProbe
    {
        private readonly bool recognize;
        private readonly string reason;
        private readonly Exception? error;
        private readonly AppleFileSystemKind? attemptKind;

        public FakeProbe(AppleFileSystemKind kind, bool recognize, string reason, Exception? error = null, AppleFileSystemKind? attemptKind = null)
        {
            this.Kind = kind;
            this.recognize = recognize;
            this.reason = reason;
            this.error = error;
            this.attemptKind = attemptKind;
        }

        public AppleFileSystemKind Kind { get; }

        public int Calls { get; private set; }

        public IAppleFileSystem? Opened { get; private set; }

        public FileSystemProbeAttempt TryOpen(FileSystemSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            this.Calls++;
            if (this.error is not null)
            {
                throw this.error;
            }

            var kind = this.attemptKind ?? this.Kind;
            if (!this.recognize)
            {
                return FileSystemProbeAttempt.Reject(kind, this.reason);
            }

            var opened = new FakeFileSystem(kind);
            this.Opened = opened;
            return FileSystemProbeAttempt.Match(kind, this.reason, opened);
        }
    }

    private sealed class FakeFileSystem : IAppleFileSystem
    {
        public FakeFileSystem(AppleFileSystemKind kind)
        {
            this.Kind = kind;
            this.VolumeName = kind.ToString();
        }

        public string VolumeName { get; }

        public AppleFileSystemKind Kind { get; }

        public VolumeSpace Allocation => default;

        public DirectoryListing ListDirectory(string? path) => DirectoryListing.Success(Array.Empty<FileEntry>());

        public FileReadResult TryReadFile(string path, FileReadView view)
        {
            ArgumentNullException.ThrowIfNull(path);
            return FileReadResult.Failure(FileAccessStatus.NotFound, "fake");
        }

        public FileEntry? GetFileInfo(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return null;
        }
    }
}