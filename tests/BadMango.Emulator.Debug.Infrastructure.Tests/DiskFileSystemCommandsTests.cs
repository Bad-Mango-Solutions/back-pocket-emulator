// <copyright file="DiskFileSystemCommandsTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Text.Json;

using Autofac;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Infrastructure.Commands.DeviceCommands;
using BadMango.Emulator.Storage.FileSystems;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Gcr;
using BadMango.Emulator.Storage.Media;
using BadMango.Emulator.Storage.Tests.FileSystems;

using Moq;

using Serilog;

/// <summary>
/// <c>disk fsinfo</c>, <c>disk cat</c>, and <c>disk read</c> against synthetic images.
/// </summary>
[TestFixture]
public sealed class DiskFileSystemCommandsTests
{
    private readonly ILogger logger = Serilog.Core.Logger.None;
    private string tempRoot = null!;
    private string exportRoot = null!;
    private DebugContext context = null!;
    private StringWriter output = null!;
    private IAppleFileSystemDetector detector = null!;
    private DiskFsInfoCommand fsInfo = null!;
    private DiskCatCommand catalog = null!;
    private DiskReadCommand read = null!;

    /// <summary>Builds a temp workspace, a detector, and the three commands.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempRoot = Directory.CreateTempSubdirectory("bms-disk-fs-").FullName;
        this.exportRoot = Path.Join(this.tempRoot, "out");
        Directory.CreateDirectory(this.exportRoot);
        this.output = new StringWriter();
        var dispatcher = new CommandDispatcher();
        this.context = new DebugContext(dispatcher, this.output, new StringWriter());
        this.context.AttachDiskImageFactory(new DiskImageFactory());
        this.detector = new AppleFileSystemDetector(
            this.logger,
            [
                new ProdosFileSystemProbe(this.logger),
                new Dos33FileSystemProbe(this.logger),
                new PascalFileSystemProbe(this.logger),
            ]);
        var renderer = DebugOutputRenderer.PlainText;
        this.fsInfo = new DiskFsInfoCommand(this.detector, renderer, this.logger);
        this.catalog = new DiskCatCommand(this.detector, renderer, this.logger);
        this.read = new DiskReadCommand(this.detector, renderer, this.logger, new DebugExportRoot(this.exportRoot));
        dispatcher.Register(this.fsInfo);
        dispatcher.Register(this.catalog);
        dispatcher.Register(this.read);
    }

    /// <summary>Removes the temp workspace.</summary>
    [TearDown]
    public void TearDown()
    {
        this.output.Dispose();
        this.context.Dispose();
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // The test already finished. A locked temp file is not a product failure.
        }
    }

    /// <summary>The DI module registers probes in the recommended order.</summary>
    [Test]
    public void Module_RegistersProbesInRecommendedOrder()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<AppleFileSystemModule>();
        using var container = builder.Build();
        var registered = container.Resolve<IAppleFileSystemDetector>();
        var image = new byte[PascalLayout.FirstDataBlock * PascalLayout.BlockSize];
        var result = registered.Detect(new FileSystemSource(new MemoryBlockReader(image, PascalLayout.FirstDataBlock, PascalLayout.BlockSize), null));

        Assume.That(result.IsRecognized, Is.False);
        Assert.That(result.Attempts.Select(attempt => attempt.Kind).ToArray(), Is.EqualTo(AppleFileSystemDetector.RecommendedProbeOrder().ToArray()));
    }

    /// <summary>Device commands resolve the file-system handlers and do not add <c>disk ls</c>.</summary>
    [Test]
    public void Module_RegistersFileSystemCommandsWithoutLs()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<DeviceDebugCommandsModule>();
        using var container = builder.Build();
        var names = container.Resolve<IEnumerable<ICommandHandler>>().Select(handler => handler.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("disk-fsinfo"));
            Assert.That(names, Does.Contain("disk-cat"));
            Assert.That(names, Does.Contain("disk-read"));
            Assert.That(names, Does.Not.Contain("disk-ls"));
            Assert.That(names, Does.Not.Contain("read"));
            Assert.That(new ReadCommand().Name, Is.EqualTo("read"));
        });
    }

    /// <summary><c>disk fsinfo --image</c> text and JSON describe a DOS 3.3 volume.</summary>
    [Test]
    public void FsInfo_DosImage_PrintsTextAndJsonEnvelope()
    {
        var fixture = this.DosVolume();
        var path = this.Write(fixture.Image, "dos.do");
        Assume.That(File.Exists(path), Is.True);

        var text = this.fsInfo.Execute(this.context, ["--image", path]);
        var textOutput = this.output.ToString();
        this.ClearOutput();
        var renderer = new DebugOutputRenderer(new EmudbgOptions(JsonOutput: true), () => new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var jsonCommand = new DiskFsInfoCommand(this.detector, renderer, this.logger);
        var json = jsonCommand.Execute(this.context, ["--image", path]);

        Assert.Multiple(() =>
        {
            Assert.That(text.Success, Is.True, text.Message);
            Assert.That(textOutput, Does.Contain("File system: DOS 3.3"));
            Assert.That(textOutput, Does.Contain("Volume: 254"));
            Assert.That(textOutput, Does.Not.Contain("emudbg/v1"));
            Assert.That(json.Success, Is.True, json.Message);
        });

        using var document = JsonDocument.Parse(this.output.ToString());
        var root = document.RootElement;
        var probes = root.GetProperty("payload").GetProperty("probes");
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(root.GetProperty("timestamp").GetString(), Is.EqualTo("2020-01-02T03:04:05+00:00"));
            Assert.That(root.GetProperty("payload").GetProperty("recognized").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("payload").GetProperty("kind").GetString(), Is.EqualTo("Dos33"));
            Assert.That(root.GetProperty("payload").GetProperty("volumeName").GetString(), Is.EqualTo("254"));
            Assert.That(root.GetProperty("payload").GetProperty("source").GetString(), Is.EqualTo("image"));
            Assert.That(probes.GetArrayLength(), Is.EqualTo(3));
            Assert.That(probes[0].GetProperty("kind").GetString(), Is.EqualTo("Prodos"));
            Assert.That(probes[0].GetProperty("recognized").GetBoolean(), Is.False);
            Assert.That(probes[1].GetProperty("kind").GetString(), Is.EqualTo("Dos33"));
            Assert.That(probes[1].GetProperty("selected").GetBoolean(), Is.True);
            Assert.That(probes[2].GetProperty("kind").GetString(), Is.EqualTo("Pascal"));
        });
    }

    /// <summary><c>disk fsinfo</c> reads a drive mounted by the debug console.</summary>
    [Test]
    public void FsInfo_MountedDrive_UsesTheTrackedImage()
    {
        var path = this.Write(this.DosVolume().Image, "mounted.do");
        var open = new DiskImageFactory().Open(path, forceReadOnly: true);
        this.context.MountedDisks.Track(6, 0, open);
        Assume.That(this.context.MountedDisks.TryGet(6, 0, out _), Is.True);

        var result = new DiskCommand().Execute(this.context, ["fsinfo", "6:1"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(this.output.ToString(), Does.Contain("File system: DOS 3.3"));
            Assert.That(this.output.ToString(), Does.Contain("Source: drive"));
        });
    }

    /// <summary>ProDOS and Pascal offline images are recognized.</summary>
    [Test]
    public void FsInfo_ProdosAndPascalImages_ReportTheirKinds()
    {
        var prodos = this.Write(this.ProdosVolume().Image, "prodos.hdv");
        var pascal = this.Write(this.PascalVolume().Image, "pascal.hdv");

        var prodosResult = this.fsInfo.Execute(this.context, ["--image", prodos]);
        var prodosOutput = this.output.ToString();
        this.ClearOutput();
        var pascalResult = this.fsInfo.Execute(this.context, ["--image", pascal]);

        Assert.Multiple(() =>
        {
            Assert.That(prodosResult.Success, Is.True, prodosResult.Message);
            Assert.That(prodosOutput, Does.Contain("File system: ProDOS"));
            Assert.That(prodosOutput, Does.Contain("Volume: BLANK"));
            Assert.That(pascalResult.Success, Is.True, pascalResult.Message);
            Assert.That(this.output.ToString(), Does.Contain("File system: Pascal"));
            Assert.That(this.output.ToString(), Does.Contain("Volume: PASCAL"));
        });
    }

    /// <summary>A blank block image is reported as unrecognized, and cat refuses it.</summary>
    [Test]
    public void UnrecognizedImage_FsInfoReportsProbes_CatFails()
    {
        var path = this.Write(new byte[8 * 512], "blank.hdv");
        var info = this.fsInfo.Execute(this.context, ["--image", path, "--json"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        Assume.That(document.RootElement.GetProperty("payload").GetProperty("recognized").GetBoolean(), Is.False);
        this.ClearOutput();
        var listed = this.catalog.Execute(this.context, ["--image", path]);

        Assert.Multiple(() =>
        {
            Assert.That(info.Success, Is.True, info.Message);
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(listed.Success, Is.False);
            Assert.That(listed.Message, Does.Contain("was recognized"));
            Assert.That(listed.Message, Does.Contain("Pascal rejected"));
        });
    }

    /// <summary>Catalog text and JSON escape inverse and flashing DOS names.</summary>
    [Test]
    public void Cat_DosImage_EscapesInverseAndFlashingNames()
    {
        var fixture = this.DosVolume();
        var path = this.Write(fixture.Image, "names.do");
        var direct = new Dos33FileSystem(fixture.CreateSectorReader(), this.logger).ListDirectory(null);
        var fancy = direct.Entries.Single(entry => entry.Name == "ABC");
        Assume.That(direct.Succeeded, Is.True);
        Assume.That(fancy.DisplayName, Is.EqualTo("\\i{A}\\f{B}C"));
        Assume.That(fancy.IsLocked, Is.True);

        var text = this.catalog.Execute(this.context, ["--image", path]);
        var textOutput = this.output.ToString();
        this.ClearOutput();
        var json = this.catalog.Execute(this.context, ["--image", path, "--json"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        var entries = document.RootElement.GetProperty("payload").GetProperty("entries");
        var match = entries.EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "ABC");

        Assert.Multiple(() =>
        {
            Assert.That(text.Success, Is.True, text.Message);
            Assert.That(textOutput, Does.Contain("\\i{A}\\f{B}C"));
            Assert.That(textOutput, Does.Contain("NOTE"));
            Assert.That(textOutput, Does.Not.Contain("emudbg/v1"));
            Assert.That(json.Success, Is.True, json.Message);
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(match.GetProperty("displayName").GetString(), Is.EqualTo("\\i{A}\\f{B}C"));
            Assert.That(match.GetProperty("locked").GetBoolean(), Is.True);
            Assert.That(entries.GetArrayLength(), Is.EqualTo(3));
        });
    }

    /// <summary><c>disk catalog</c> and <c>disk cat</c> run the same handler.</summary>
    [Test]
    public void CatalogAlias_MatchesCat()
    {
        var path = this.Write(this.DosVolume().Image, "alias.do");
        var parent = new DiskCommand();
        var cat = parent.Execute(this.context, ["cat", "--image", path]);
        var catOutput = this.output.ToString();
        this.ClearOutput();
        var catalogResult = parent.Execute(this.context, ["catalog", "--image", path]);

        Assert.Multiple(() =>
        {
            Assert.That(cat.Success, Is.True, cat.Message);
            Assert.That(catalogResult.Success, Is.True, catalogResult.Message);
            Assert.That(this.output.ToString(), Is.EqualTo(catOutput));
            Assert.That(this.catalog.Aliases, Does.Contain("disk-catalog"));
            Assert.That(this.catalog.Aliases, Does.Contain("diskcatalog"));
        });
    }

    /// <summary>A ProDOS subdirectory path lists that directory and not the volume root.</summary>
    [Test]
    public void Cat_ProdosSubdirectory_ListsThatDirectory()
    {
        var path = this.Write(this.ProdosVolume().Image, "tree.hdv");
        var root = this.catalog.Execute(this.context, ["--image", path]);
        var rootOutput = this.output.ToString();
        this.ClearOutput();
        var sub = this.catalog.Execute(this.context, ["--image", path, "SUB", "--json"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        var names = document.RootElement.GetProperty("payload").GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(root.Success, Is.True, root.Message);
            Assert.That(rootOutput, Does.Contain("SUB"));
            Assert.That(rootOutput, Does.Not.Contain("NOTE"));
            Assert.That(sub.Success, Is.True, sub.Message);
            Assert.That(names, Is.EqualTo(new[] { "NOTE" }));
            Assert.That(document.RootElement.GetProperty("payload").GetProperty("path").GetString(), Is.EqualTo("SUB"));
        });
    }

    /// <summary>Pascal catalog text names the file.</summary>
    [Test]
    public void Cat_PascalImage_ListsTheFile()
    {
        var path = this.Write(this.PascalVolume().Image, "files.hdv");
        var result = this.catalog.Execute(this.context, ["--image", path]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(this.output.ToString(), Does.Contain("NOTE.TEXT"));
            Assert.That(this.output.ToString(), Does.Contain("Pascal volume PASCAL"));
        });
    }

    /// <summary><c>disk read</c> prints text, and raw versus logical bytes differ for a DOS binary.</summary>
    [Test]
    public void Read_DosImage_PrintsTextAndSeparatesRawFromLogical()
    {
        var fixture = this.DosVolume();
        var path = this.Write(fixture.Image, "read.do");
        var code = fixture.Entries.Single(entry => entry.Name == "CODE");
        Assume.That(code.RawBytes.Length, Is.GreaterThan(code.LogicalBytes.Length));
        Assume.That(code.LogicalBytes.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }));

        var text = this.read.Execute(this.context, ["--image", path, "NOTE"]);
        var textOutput = this.output.ToString();
        this.ClearOutput();
        var logical = this.read.Execute(this.context, ["--image", path, "CODE", "--json"]);
        using var logicalJson = JsonDocument.Parse(this.output.ToString());
        this.ClearOutput();
        var raw = this.read.Execute(this.context, ["--image", path, "CODE", "--raw", "--hex", "--json"]);
        using var rawJson = JsonDocument.Parse(this.output.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(text.Success, Is.True, text.Message);
            Assert.That(textOutput, Does.Contain("HI\n"));
            Assert.That(textOutput, Does.Contain("logical"));
            Assert.That(logical.Success, Is.True, logical.Message);
            Assert.That(logicalJson.RootElement.GetProperty("schema").GetString(), Is.EqualTo("emudbg/v1"));
            Assert.That(logicalJson.RootElement.GetProperty("payload").GetProperty("view").GetString(), Is.EqualTo("logical"));
            Assert.That(logicalJson.RootElement.GetProperty("payload").GetProperty("data").GetString(), Is.EqualTo(Convert.ToHexStringLower(code.LogicalBytes.Span)));
            Assert.That(raw.Success, Is.True, raw.Message);
            Assert.That(rawJson.RootElement.GetProperty("payload").GetProperty("view").GetString(), Is.EqualTo("raw"));
            Assert.That(rawJson.RootElement.GetProperty("payload").GetProperty("presentation").GetString(), Is.EqualTo("hex"));
            Assert.That(rawJson.RootElement.GetProperty("payload").GetProperty("data").GetString(), Is.EqualTo(Convert.ToHexStringLower(code.RawBytes.Span)));
            Assert.That(rawJson.RootElement.GetProperty("payload").TryGetProperty("text", out _), Is.False);
        });
    }

    /// <summary>ProDOS and Pascal logical reads match the raw bytes.</summary>
    [Test]
    public void Read_ProdosAndPascal_LogicalMatchesRaw()
    {
        var prodos = this.Write(this.ProdosVolume().Image, "note.hdv");
        var pascal = this.Write(this.PascalVolume().Image, "note-p.hdv");
        var prodosRead = this.read.Execute(this.context, ["--image", prodos, "SUB/NOTE", "--json"]);
        using var prodosJson = JsonDocument.Parse(this.output.ToString());
        this.ClearOutput();
        var pascalRead = this.read.Execute(this.context, ["--image", pascal, "NOTE.TEXT", "--raw", "--json"]);
        using var pascalJson = JsonDocument.Parse(this.output.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(prodosRead.Success, Is.True, prodosRead.Message);
            Assert.That(prodosJson.RootElement.GetProperty("payload").GetProperty("text").GetString(), Does.Contain("Hi"));
            Assert.That(pascalRead.Success, Is.True, pascalRead.Message);
            Assert.That(pascalJson.RootElement.GetProperty("payload").GetProperty("data").GetString(), Does.StartWith(Convert.ToHexStringLower("Hi"u8)));
            Assert.That(pascalJson.RootElement.GetProperty("payload").GetProperty("view").GetString(), Is.EqualTo("raw"));
        });
    }

    /// <summary><c>--to</c> writes inside the output root and refuses paths outside it.</summary>
    [Test]
    public void Read_To_StaysInsideTheOutputRoot()
    {
        var path = this.Write(this.DosVolume().Image, "export.do");
        var inside = this.read.Execute(this.context, ["--image", path, "CODE", "--logical", "--to", "nested/code.bin"]);
        var written = File.ReadAllBytes(Path.Join(this.exportRoot, "nested", "code.bin"));
        var outside = this.read.Execute(this.context, ["--image", path, "CODE", "--to", "../code.bin"]);
        var absolute = this.read.Execute(this.context, ["--image", path, "CODE", "--to", "/tmp/code.bin"]);
        var unconfigured = new DiskReadCommand(this.detector, DebugOutputRenderer.PlainText, this.logger, new DebugExportRoot(null));
        var missingRoot = unconfigured.Execute(this.context, ["--image", path, "CODE", "--to", "code.bin"]);

        Assert.Multiple(() =>
        {
            Assert.That(inside.Success, Is.True, inside.Message);
            Assert.That(written, Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }));
            Assert.That(this.output.ToString(), Does.Contain("nested/code.bin"));
            Assert.That(outside.Success, Is.False);
            Assert.That(outside.Message, Does.Contain("outside"));
            Assert.That(File.Exists(Path.Join(this.tempRoot, "code.bin")), Is.False);
            Assert.That(absolute.Success, Is.False);
            Assert.That(absolute.Message, Does.Contain("relative"));
            Assert.That(missingRoot.Success, Is.False);
            Assert.That(missingRoot.Message, Does.Contain("output root"));
        });
    }

    /// <summary>A missing file is an error, and <c>disk ls</c> is not a command.</summary>
    [Test]
    public void Read_UnknownFile_AndDiskLs_Fail()
    {
        var path = this.Write(this.DosVolume().Image, "missing.do");
        var missing = this.read.Execute(this.context, ["--image", path, "NOPE"]);
        var both = this.read.Execute(this.context, ["--image", path, "NOTE", "--raw", "--logical"]);
        var ls = new DiskCommand().Execute(this.context, ["ls"]);
        var memory = new ReadCommand();

        Assert.Multiple(() =>
        {
            Assert.That(missing.Success, Is.False);
            Assert.That(missing.Message, Does.Contain("NOPE"));
            Assert.That(both.Success, Is.False);
            Assert.That(both.Message, Does.Contain("--raw"));
            Assert.That(ls.Success, Is.False);
            Assert.That(ls.Message, Does.Contain("Unknown"));
            Assert.That(ls.Message, Does.Not.Contain("disk-ls"));
            Assert.That(memory.Name, Is.EqualTo("read"));
            Assert.That(memory.Name, Is.Not.EqualTo(this.read.Name));
            Assert.That(this.read.Name, Is.EqualTo("disk-read"));
        });
    }

    /// <summary>A mounted drive can read the same file as <c>--image</c>.</summary>
    [Test]
    public void Read_MountedDrive_MatchesTheOfflineImage()
    {
        var imagePath = this.Write(this.DosVolume().Image, "slot.do");
        this.context.MountedDisks.Track(6, 0, new DiskImageFactory().Open(imagePath, forceReadOnly: true));
        var mounted = new DiskCommand().Execute(this.context, ["read", "6:1", "NOTE", "--json"]);
        using var mountedJson = JsonDocument.Parse(this.output.ToString());
        var mountedData = mountedJson.RootElement.GetProperty("payload").GetProperty("data").GetString();
        this.ClearOutput();
        var offline = this.read.Execute(this.context, ["--image", imagePath, "NOTE", "--json"]);
        using var offlineJson = JsonDocument.Parse(this.output.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(mounted.Success, Is.True, mounted.Message);
            Assert.That(offline.Success, Is.True, offline.Message);
            Assert.That(mountedJson.RootElement.GetProperty("payload").GetProperty("source").GetString(), Is.EqualTo("drive"));
            Assert.That(offlineJson.RootElement.GetProperty("payload").GetProperty("data").GetString(), Is.EqualTo(mountedData));
        });
    }

    /// <summary>A mounted nibble image is not a catalog, and the drive file stays open.</summary>
    [Test]
    public void Cat_MountedNib_LeavesTheDriveReadable()
    {
        var path = Path.Join(this.tempRoot, "blank.nib");
        File.WriteAllBytes(path, new byte[35 * GcrEncoder.StandardTrackLength]);
        var open = new DiskImageFactory().Open(path, forceReadOnly: true);
        Assume.That(open, Is.InstanceOf<Image525Result>());
        this.context.MountedDisks.Track(6, 0, open);
        var result = this.catalog.Execute(this.context, ["6:1"]);
        var track = new byte[((Image525Result)open).Media.OptimalTrackLength];

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("no sector or block view"));
            Assert.DoesNotThrow(() => ((Image525Result)open).Media.ReadTrack(0, track));
        });
    }

    /// <summary>ProDOS and Pascal volumes can be catalogued from a mounted drive.</summary>
    [Test]
    public void CatAndRead_MountedProdosAndPascal()
    {
        var prodos = this.Write(this.ProdosVolume().Image, "mounted-pro.hdv");
        var pascal = this.Write(this.PascalVolume().Image, "mounted-pas.hdv");
        this.context.MountedDisks.Track(6, 0, new DiskImageFactory().Open(prodos, forceReadOnly: true));
        this.context.MountedDisks.Track(6, 1, new DiskImageFactory().Open(pascal, forceReadOnly: true));

        var prodosCat = new DiskCommand().Execute(this.context, ["cat", "6:1", "SUB"]);
        var prodosOutput = this.output.ToString();
        this.ClearOutput();
        var pascalRead = new DiskCommand().Execute(this.context, ["read", "6:2", "NOTE.TEXT"]);

        Assert.Multiple(() =>
        {
            Assert.That(prodosCat.Success, Is.True, prodosCat.Message);
            Assert.That(prodosOutput, Does.Contain("NOTE"));
            Assert.That(prodosOutput, Does.Contain("ProDOS"));
            Assert.That(pascalRead.Success, Is.True, pascalRead.Message);
            Assert.That(this.output.ToString(), Does.Contain("Hi"));
            Assert.That(this.output.ToString(), Does.Contain("Pascal"));
        });
    }

    /// <summary>A dirty mounted drive is flushed before the catalog is read.</summary>
    [Test]
    public void Cat_MountedDrive_FlushesDirtyTracksFirst()
    {
        var path = this.Write(this.DosVolume().Image, "dirty.do");
        this.context.MountedDisks.Track(6, 0, new DiskImageFactory().Open(path, forceReadOnly: true));
        var card = new Mock<ISlotCard>();
        var controller = card.As<IDiskController>();
        card.SetupGet(item => item.Name).Returns("Disk II Controller");
        controller.SetupGet(item => item.DriveCount).Returns(1);
        controller.Setup(item => item.GetDriveSnapshot(0)).Returns(new DriveSnapshot(
            Selected: true,
            MotorOn: false,
            PhaseLatch: 0,
            QuarterTrack: 0,
            WriteProtect: false,
            HasMedia: true,
            MountedImagePath: path,
            UnflushedChanges: true));
        var slots = new Mock<ISlotManager>();
        slots.Setup(item => item.GetCard(6)).Returns(card.Object);
        var machine = new Mock<IMachine>();
        machine.Setup(item => item.GetComponent<ISlotManager>()).Returns(slots.Object);
        this.context.AttachMachine(machine.Object);

        var result = this.catalog.Execute(this.context, ["6:1"]);

        Assert.That(result.Success, Is.True, result.Message);
        controller.Verify(item => item.Flush(0), Times.Once);
    }

    /// <summary>A write-protected drive is not flushed, even when the snapshot reports dirty tracks.</summary>
    [Test]
    public void Cat_WriteProtectedDrive_DoesNotFlush()
    {
        var path = this.Write(this.DosVolume().Image, "protected.do");
        this.context.MountedDisks.Track(6, 0, new DiskImageFactory().Open(path, forceReadOnly: true));
        var card = new Mock<ISlotCard>();
        var controller = card.As<IDiskController>();
        card.SetupGet(item => item.Name).Returns("Disk II Controller");
        controller.SetupGet(item => item.DriveCount).Returns(1);
        controller.Setup(item => item.GetDriveSnapshot(0)).Returns(new DriveSnapshot(
            Selected: true,
            MotorOn: false,
            PhaseLatch: 0,
            QuarterTrack: 0,
            WriteProtect: true,
            HasMedia: true,
            MountedImagePath: path,
            UnflushedChanges: true));
        var slots = new Mock<ISlotManager>();
        slots.Setup(item => item.GetCard(6)).Returns(card.Object);
        var machine = new Mock<IMachine>();
        machine.Setup(item => item.GetComponent<ISlotManager>()).Returns(slots.Object);
        this.context.AttachMachine(machine.Object);

        var result = this.catalog.Execute(this.context, ["6:1"]);

        Assert.That(result.Success, Is.True, result.Message);
        controller.Verify(item => item.Flush(It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// DOS keeps stored case. <c>disk read</c> hits an exact lowercase name, falls back
    /// when the fold is unique, and reports both candidates when it is not.
    /// </summary>
    [Test]
    public void Read_DosCase_ExactFallbackAndAmbiguous()
    {
        var lowerOnly = this.Write(this.CaseVolume(upper: false, lower: true).Image, "lower.do");
        var upperOnly = this.Write(this.CaseVolume(upper: true, lower: false).Image, "upper.do");
        var bothPath = this.Write(this.CaseVolume(upper: true, lower: true).Image, "both.do");
        var exact = this.read.Execute(this.context, ["--image", lowerOnly, "hello", "--hex"]);
        var exactOutput = this.output.ToString();
        this.ClearOutput();
        var fallback = this.read.Execute(this.context, ["--image", upperOnly, "Hello", "--hex"]);
        var fallbackOutput = this.output.ToString();
        this.ClearOutput();
        var ambiguous = this.read.Execute(this.context, ["--image", bothPath, "Hello"]);

        Assert.Multiple(() =>
        {
            Assert.That(exact.Success, Is.True, exact.Message);
            Assert.That(exactOutput, Does.Contain("22"));
            Assert.That(exactOutput, Does.Contain("hello"));
            Assert.That(fallback.Success, Is.True, fallback.Message);
            Assert.That(fallbackOutput, Does.Contain("11"));
            Assert.That(fallbackOutput, Does.Contain("HELLO"));
            Assert.That(ambiguous.Success, Is.False);
            Assert.That(ambiguous.Message, Does.Contain("HELLO"));
            Assert.That(ambiguous.Message, Does.Contain("hello"));
            Assert.That(DiskFileSystemSupport.ToText(new byte[] { 0xDC }), Is.EqualTo("\\\\"));
        });
    }

    /// <summary><c>--</c> lets a file name start with a dash.</summary>
    [Test]
    public void Read_DoubleDash_AllowsALeadingDash()
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddCatalogName(new byte[] { 0xAD, 0xC8, 0xC5, 0xCC, 0xCC, 0xCF }, Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x41 } });
        var path = this.Write(builder.Build().Image, "dash.do");
        var rejected = this.read.Execute(this.context, ["--image", path, "-HELLO"]);
        var accepted = this.read.Execute(this.context, ["--image", path, "--", "-HELLO"]);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Message, Does.Contain("Unknown option"));
            Assert.That(accepted.Success, Is.True, accepted.Message);
        });
    }

    /// <summary><c>--to</c> refuses a symlink that leaves the output root.</summary>
    [Test]
    public void Read_To_RefusesSymlinkEscape()
    {
        var outside = Directory.CreateTempSubdirectory("bms-disk-out-");
        try
        {
            var directoryLink = Path.Join(this.exportRoot, "link");
            var directoryLinked = TryLink(directoryLink, outside.FullName, file: false);
            Assume.That(directoryLinked, Is.True, "This platform did not create a directory symlink.");
            var path = this.Write(this.DosVolume().Image, "link.do");
            var throughDirectory = this.read.Execute(this.context, ["--image", path, "CODE", "--to", "link/x.bin"]);
            var outsideFile = Path.Join(outside.FullName, "target.bin");
            File.WriteAllBytes(outsideFile, new byte[] { 1 });
            var fileLink = Path.Join(this.exportRoot, "file.bin");
            var fileLinked = TryLink(fileLink, outsideFile, file: true);
            Assume.That(fileLinked, Is.True, "This platform did not create a file symlink.");
            var throughFile = this.read.Execute(this.context, ["--image", path, "CODE", "--to", "file.bin"]);

            Assert.Multiple(() =>
            {
                Assert.That(throughDirectory.Success, Is.False);
                Assert.That(throughDirectory.Message, Does.Contain("outside"));
                Assert.That(File.Exists(Path.Join(outside.FullName, "x.bin")), Is.False);
                Assert.That(throughFile.Success, Is.False);
                Assert.That(throughFile.Message, Does.Contain("outside"));
                Assert.That(File.ReadAllBytes(outsideFile), Is.EqualTo(new byte[] { 1 }));
            });
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    private static bool TryLink(string link, string target, bool file)
    {
        try
        {
            if (file)
            {
                File.CreateSymbolicLink(link, target);
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private Dos33Fixture DosVolume()
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddFile("NOTE", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0xC8, 0xC9, 0x8D } });
        builder.AddFile("CODE", Dos33FileType.Binary, false, false, new byte[]?[] { new byte[] { 0x00, 0x08, 0x05, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 } });
        builder.AddCatalogName(new byte[] { 0x01, 0x42, 0xC3 }, Dos33FileType.Text, true, false, new byte[]?[] { new byte[] { 0xC1 } });
        return builder.Build();
    }

    private ProdosFixture ProdosVolume()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "BLANK");
        builder.AddSubdirectory("SUB", new[] { new ProdosFixture.SeedlingSpec("NOTE", ProdosFileType.Text, 0, "Hi"u8.ToArray(), false) });
        return builder.Build();
    }

    private PascalFixture PascalVolume()
    {
        var builder = new PascalImageBuilder(8, "PASCAL");
        builder.AddFile("NOTE.TEXT", PascalFileKind.Text, "Hi"u8.ToArray());
        return builder.Build();
    }

    private Dos33Fixture CaseVolume(bool upper, bool lower)
    {
        var builder = new Dos33ImageBuilder(35, 254);
        if (upper)
        {
            builder.AddFile("HELLO", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x11 } });
        }

        if (lower)
        {
            builder.AddCatalogName(new byte[] { 0xE8, 0xE5, 0xEC, 0xEC, 0xEF }, Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0x22 } });
        }

        return builder.Build();
    }

    private string Write(ReadOnlyMemory<byte> image, string name)
    {
        var path = Path.Join(this.tempRoot, name);
        File.WriteAllBytes(path, image.ToArray());
        return path;
    }

    private void ClearOutput() => this.output.GetStringBuilder().Clear();
}