// <copyright file="DiskFileSystemCommandsTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using System.Globalization;
using System.Text.Json;

using Autofac;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
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
            Assert.That(this.output.ToString(), Does.Contain("BLOCKS FREE: 1     BLOCKS USED: 7     TOTAL BLOCKS: 8"));
        });
    }

    /// <summary>
    /// A ProDOS catalog uses named types, dates, aux type, and bitmap totals.
    /// </summary>
    [Test]
    public void Cat_ProdosImage_PrintsCatalogColumnsAndBitmapFooter()
    {
        var builder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "SAMPLE");
        var february = ProdosTimestamp.Encode(18, 2, 13, 9, 9);
        var december = ProdosTimestamp.Encode(23, 12, 30, 2, 43);
        builder.AddSeedling("VIEW.README", ProdosFileType.Applesoft, 0x0801, new byte[512], false);
        builder.SetDates(february, february);
        builder.AddSeedling("BITSY.BOOT", ProdosFileType.Sys, 0, new byte[365], false);
        builder.SetDates(february, february);
        builder.AddSeedling("BASIC.SYSTEM", ProdosFileType.Sys, 0, new byte[21], false);
        builder.SetDates(december, december);
        builder.AddSeedling("CD.EXT", ProdosFileType.Text, 0, new byte[10], false);
        builder.SetFileType(0xF1);
        builder.SetDates(february, february);
        builder.AddSeedling("FASTDSK", ProdosFileType.Binary, 0x8E00, new byte[19], false);
        builder.SetDates(february, february);
        builder.AddSeedling("NODATE", ProdosFileType.Text, 0, new byte[] { 1 }, false);
        builder.AddSeedling("LOCKED", ProdosFileType.Binary, 0, new byte[] { 1 }, true);
        foreach (var named in new (byte Type, string Name)[]
        {
            (0x01, "BAD"),
            (0x19, "ADB"),
            (0x1A, "AWP"),
            (0x1B, "ASP"),
            (0xB3, "S16"),
            (0xEF, "PAS"),
            (0xF0, "CMD"),
            (0xFA, "INT"),
            (0xFB, "IVR"),
            (0xFD, "VAR"),
            (0xFE, "REL"),
        })
        {
            builder.AddSeedling(named.Name, ProdosFileType.Text, 0, new byte[] { 1 }, false);
            builder.SetFileType(named.Type);
        }

        var fixture = builder.Build();
        var path = this.Write(fixture.Image, "catalog.hdv");
        var text = this.catalog.Execute(this.context, ["--image", path]);
        var textOutput = this.output.ToString();
        this.ClearOutput();
        var json = this.catalog.Execute(this.context, ["--image", path, "--json"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        var payload = document.RootElement.GetProperty("payload");
        var readme = payload.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "VIEW.README");
        var nodate = payload.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "NODATE");
        var custom = payload.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "CD.EXT");
        var free = 0;
        var bitmapAt = ProdosLayout.BitmapPointer(fixture.BlockCount) * ProdosLayout.BlockSize;
        var bitmap = fixture.Image.Span.Slice(bitmapAt, ProdosLayout.BitmapBlockCount(fixture.BlockCount) * ProdosLayout.BlockSize);
        for (var block = 0; block < fixture.BlockCount; block++)
        {
            if (ProdosLayout.IsBlockFree(bitmap, block))
            {
                free++;
            }
        }

        var footer = string.Format(
            CultureInfo.InvariantCulture,
            "BLOCKS FREE: {0}     BLOCKS USED: {1}     TOTAL BLOCKS: {2}",
            free,
            fixture.BlockCount - free,
            fixture.BlockCount);
        Assert.Multiple(() =>
        {
            Assert.That(text.Success, Is.True, text.Message);
            Assert.That(textOutput, Does.Contain("IMAGE " + path));
            Assert.That(textOutput, Does.Contain("/SAMPLE"));
            Assert.That(textOutput, Does.Contain(" NAME            TYPE  BLKS  MODIFIED         CREATED          ENDFILE SUBTYPE"));
            Assert.That(textOutput, Does.Contain(" VIEW.README     BAS      1  13-FEB-18  9:09  13-FEB-18  9:09      512 A=$0801"));
            Assert.That(textOutput, Does.Contain(" BITSY.BOOT      SYS      1  13-FEB-18  9:09  13-FEB-18  9:09      365"));
            Assert.That(textOutput, Does.Contain(" BASIC.SYSTEM    SYS      1  30-DEC-23  2:43  30-DEC-23  2:43       21"));
            Assert.That(textOutput, Does.Contain(" CD.EXT          $F1      1  13-FEB-18  9:09  13-FEB-18  9:09       10"));
            Assert.That(textOutput, Does.Contain(" FASTDSK         BIN      1  13-FEB-18  9:09  13-FEB-18  9:09       19 A=$8E00"));
            Assert.That(textOutput, Does.Contain(" NODATE          TXT      1  <NO DATE>        <NO DATE>              1"));
            Assert.That(textOutput, Does.Contain("*LOCKED          BIN      1  <NO DATE>        <NO DATE>              1"));
            Assert.That(textOutput, Does.Contain(" BAD             BAD      1  <NO DATE>        <NO DATE>              1"));
            Assert.That(textOutput, Does.Contain(" ADB             ADB      1"));
            Assert.That(textOutput, Does.Contain(" AWP             AWP      1"));
            Assert.That(textOutput, Does.Contain(" ASP             ASP      1"));
            Assert.That(textOutput, Does.Contain(" S16             S16      1"));
            Assert.That(textOutput, Does.Contain(" PAS             PAS      1"));
            Assert.That(textOutput, Does.Contain(" CMD             CMD      1"));
            Assert.That(textOutput, Does.Contain(" INT             INT      1"));
            Assert.That(textOutput, Does.Contain(" IVR             IVR      1"));
            Assert.That(textOutput, Does.Contain(" VAR             VAR      1"));
            Assert.That(textOutput, Does.Contain(" REL             REL      1"));
            Assert.That(textOutput, Does.Not.Contain("A=$0000"));
            Assert.That(textOutput, Does.Contain(footer));
            Assert.That(textOutput.TrimEnd(), Does.EndWith(footer));
            Assert.That(json.Success, Is.True, json.Message);
            Assert.That(readme.GetProperty("fileType").GetString(), Is.EqualTo("BAS"));
            Assert.That(readme.GetProperty("auxType").GetInt32(), Is.EqualTo(0x0801));
            Assert.That(readme.GetProperty("eofBytes").GetInt32(), Is.EqualTo(512));
            Assert.That(readme.GetProperty("allocatedUnits").GetInt32(), Is.EqualTo(1));
            Assert.That(readme.GetProperty("created").GetProperty("year").GetInt32(), Is.EqualTo(2018));
            Assert.That(readme.GetProperty("created").GetProperty("month").GetInt32(), Is.EqualTo(2));
            Assert.That(readme.GetProperty("created").GetProperty("day").GetInt32(), Is.EqualTo(13));
            Assert.That(readme.GetProperty("created").GetProperty("hour").GetInt32(), Is.EqualTo(9));
            Assert.That(readme.GetProperty("created").GetProperty("minute").GetInt32(), Is.EqualTo(9));
            Assert.That(readme.GetProperty("modified").GetProperty("year").GetInt32(), Is.EqualTo(2018));
            Assert.That(nodate.GetProperty("created").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(nodate.GetProperty("modified").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(custom.GetProperty("fileType").GetString(), Is.EqualTo("$F1"));
            Assert.That(payload.GetProperty("freeUnits").GetInt32(), Is.EqualTo(free));
            Assert.That(payload.GetProperty("usedUnits").GetInt32(), Is.EqualTo(fixture.BlockCount - free));
            Assert.That(payload.GetProperty("totalUnits").GetInt32(), Is.EqualTo(fixture.BlockCount));
            Assert.That(payload.GetProperty("unit").GetString(), Is.EqualTo("block"));
        });
    }

    /// <summary>A DOS 3.3 catalog keeps type letters and takes its footer from the VTOC bitmap.</summary>
    [Test]
    public void Cat_DosImage_PrintsTypeLettersAndSectorFooter()
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddFile("NOTE", Dos33FileType.Text, false, false, new byte[]?[] { new byte[] { 0xC8 } });
        builder.AddFile("CODE", Dos33FileType.Binary, true, false, new byte[]?[] { new byte[] { 0x00, 0x08, 0x01, 0x00, 0x22 } });
        var fixture = builder.Build();
        var path = this.Write(fixture.Image, "catalog.do");
        var text = this.catalog.Execute(this.context, ["--image", path]);
        var textOutput = this.output.ToString();
        this.ClearOutput();
        var json = this.catalog.Execute(this.context, ["--image", path, "--json"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        var payload = document.RootElement.GetProperty("payload");
        var note = payload.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "NOTE");
        var vtocAt = ((Dos33Layout.VtocTrack * Dos33Layout.DefaultSectorsPerTrack) + Dos33Layout.VtocSector) * Dos33Layout.SectorSize;
        var vtoc = fixture.Image.Span.Slice(vtocAt, Dos33Layout.SectorSize);
        var total = fixture.TrackCount * Dos33Layout.DefaultSectorsPerTrack;
        var free = 0;
        for (var track = 0; track < fixture.TrackCount; track++)
        {
            for (var sector = 0; sector < Dos33Layout.DefaultSectorsPerTrack; sector++)
            {
                if (Dos33Layout.IsSectorFree(vtoc, track, sector))
                {
                    free++;
                }
            }
        }

        var footer = string.Format(
            CultureInfo.InvariantCulture,
            "SECTORS FREE: {0}     SECTORS USED: {1}     TOTAL SECTORS: {2}",
            free,
            total - free,
            total);
        Assert.Multiple(() =>
        {
            Assert.That(text.Success, Is.True, text.Message);
            Assert.That(textOutput, Does.Contain("DISK VOLUME 254"));
            Assert.That(textOutput, Does.Contain(" T "));
            Assert.That(textOutput, Does.Contain("NOTE"));
            Assert.That(textOutput, Does.Contain("*B "));
            Assert.That(textOutput, Does.Contain("CODE"));
            Assert.That(textOutput, Does.Not.Contain("MODIFIED"));
            Assert.That(textOutput, Does.Contain(footer));
            Assert.That(json.Success, Is.True, json.Message);
            Assert.That(note.GetProperty("fileType").GetString(), Is.EqualTo("T"));
            Assert.That(note.GetProperty("created").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(payload.GetProperty("freeUnits").GetInt32(), Is.EqualTo(free));
            Assert.That(payload.GetProperty("usedUnits").GetInt32(), Is.EqualTo(total - free));
            Assert.That(payload.GetProperty("totalUnits").GetInt32(), Is.EqualTo(total));
            Assert.That(payload.GetProperty("unit").GetString(), Is.EqualTo("sector"));
        });
    }

    /// <summary>A mounted-drive banner names the slot and drive.</summary>
    [Test]
    public void FormatCatalog_ProdosDrive_UsesSlotAndDrive()
    {
        var created = new DiskCatalogTimestamp(2018, 2, 13, 9, 9);
        var payload = new DiskCatalogPayload(
            "drive",
            "6:1",
            nameof(AppleFileSystemKind.Prodos),
            "PRODOS.2.4.3",
            "/",
            [
                new DiskCatalogEntryPayload(
                    "VIEW.README",
                    "VIEW.README",
                    "VIEW.README",
                    "BAS",
                    0xFC,
                    0x0801,
                    512,
                    1,
                    false,
                    false,
                    created,
                    created),
            ],
            123,
            157,
            280,
            "block");

        var text = DiskFileSystemSupport.FormatCatalog(payload);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("CATALOG DISK"));
            Assert.That(text, Does.Contain("SLOT 6  DRIVE 1"));
            Assert.That(text, Does.Contain("/PRODOS.2.4.3"));
            Assert.That(text, Does.Contain(" VIEW.README     BAS      1  13-FEB-18  9:09  13-FEB-18  9:09      512 A=$0801"));
            Assert.That(text, Does.Contain("BLOCKS FREE: 123     BLOCKS USED: 157     TOTAL BLOCKS: 280"));
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

    /// <summary>
    /// Logical DOS 3.3 text stops at the first <c>$00</c> for text, hex, JSON, and <c>--to</c>.
    /// <c>--raw</c> keeps the last-sector padding, and a sector with no <c>$00</c> stays whole.
    /// </summary>
    [Test]
    public void Read_DosText_StopsAtFirstZero()
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddFile(
            "NOTE",
            Dos33FileType.Text,
            false,
            false,
            new byte[]?[] { new byte[] { 0xC8, 0xC9, 0x8D, 0x00, 0xD4, 0xC8, 0xC5, 0xD2, 0xC5 } });
        var full = new byte[Dos33Layout.SectorSize];
        Array.Fill(full, (byte)0xC1);
        builder.AddFile("FULL", Dos33FileType.Text, false, false, new byte[]?[] { full });
        var path = this.Write(builder.Build().Image, "text.do");
        var presented = new byte[] { 0xC8, 0xC9, 0x8D };

        var printed = this.read.Execute(this.context, ["--image", path, "NOTE"]);
        var printedOutput = this.output.ToString();
        this.ClearOutput();
        var hex = this.read.Execute(this.context, ["--image", path, "NOTE", "--hex"]);
        var hexOutput = this.output.ToString();
        this.ClearOutput();
        var json = this.read.Execute(this.context, ["--image", path, "NOTE", "--json", "--to", "note.bin"]);
        using var document = JsonDocument.Parse(this.output.ToString());
        var exported = File.ReadAllBytes(Path.Join(this.exportRoot, "note.bin"));
        this.ClearOutput();
        var raw = this.read.Execute(this.context, ["--image", path, "NOTE", "--raw", "--json", "--to", "note.raw"]);
        using var rawDocument = JsonDocument.Parse(this.output.ToString());
        var rawExported = File.ReadAllBytes(Path.Join(this.exportRoot, "note.raw"));
        this.ClearOutput();
        var fullRead = this.read.Execute(this.context, ["--image", path, "FULL", "--json"]);
        using var fullDocument = JsonDocument.Parse(this.output.ToString());
        var payload = document.RootElement.GetProperty("payload");
        var rawPayload = rawDocument.RootElement.GetProperty("payload");
        var fullPayload = fullDocument.RootElement.GetProperty("payload");

        Assert.Multiple(() =>
        {
            Assert.That(printed.Success, Is.True, printed.Message);
            Assert.That(printedOutput, Does.Contain("HI\n"));
            Assert.That(printedOutput, Does.Contain("logical 3 bytes"));
            Assert.That(printedOutput, Does.Not.Contain("\\x00"));
            Assert.That(printedOutput, Does.Not.Contain("THERE"));
            Assert.That(hex.Success, Is.True, hex.Message);
            Assert.That(hexOutput, Does.Contain("0000: C8 C9 8D\n"));
            Assert.That(hexOutput, Does.Not.Contain("0010:"));
            Assert.That(hexOutput, Does.Not.Contain("D4"));
            Assert.That(json.Success, Is.True, json.Message);
            Assert.That(payload.GetProperty("view").GetString(), Is.EqualTo("logical"));
            Assert.That(payload.GetProperty("length").GetInt32(), Is.EqualTo(presented.Length));
            Assert.That(payload.GetProperty("data").GetString(), Is.EqualTo(Convert.ToHexStringLower(presented)));
            Assert.That(payload.GetProperty("text").GetString(), Is.EqualTo("HI\n"));
            Assert.That(exported, Is.EqualTo(presented));
            Assert.That(raw.Success, Is.True, raw.Message);
            Assert.That(rawPayload.GetProperty("view").GetString(), Is.EqualTo("raw"));
            Assert.That(rawPayload.GetProperty("length").GetInt32(), Is.EqualTo(Dos33Layout.SectorSize));
            Assert.That(rawPayload.GetProperty("data").GetString(), Does.StartWith("c8c98d00d4c8c5d2c5"));
            Assert.That(rawExported, Has.Length.EqualTo(Dos33Layout.SectorSize));
            Assert.That(rawExported[3], Is.EqualTo(0));
            Assert.That(rawExported[^1], Is.EqualTo(0));
            Assert.That(fullRead.Success, Is.True, fullRead.Message);
            Assert.That(fullPayload.GetProperty("length").GetInt32(), Is.EqualTo(Dos33Layout.SectorSize));
            Assert.That(this.read.DetailedDescription, Does.Contain("first $00").And.Contain("last-sector padding"));
            Assert.That(this.read.Options.Single(option => option.Name == "--raw").Description, Does.Contain("padding"));
            Assert.That(this.read.Options.Single(option => option.Name == "--logical").Description, Does.Contain("$00"));
        });
    }

    /// <summary>
    /// Logical DOS binary, Applesoft, and Integer reads match the header length word.
    /// <c>--raw</c> keeps the header and the last-sector pad.
    /// </summary>
    [Test]
    public void Read_DosHeaderedFiles_MatchTheLengthWord()
    {
        var builder = new Dos33ImageBuilder(35, 254);
        builder.AddFile("BIN", Dos33FileType.Binary, false, false, new byte[]?[] { PaddedSector(new byte[] { 0x00, 0x08, 0x02, 0x00, 0x11, 0x22 }, 0xFF) });
        builder.AddFile("BAS", Dos33FileType.Applesoft, false, false, new byte[]?[] { PaddedSector(new byte[] { 0x03, 0x00, 0x01, 0x02, 0x03 }, 0xEE) });
        builder.AddFile("INT", Dos33FileType.Integer, false, false, new byte[]?[] { PaddedSector(new byte[] { 0x01, 0x00, 0x42 }, 0xDD) });
        var path = this.Write(builder.Build().Image, "headers.do");

        var binary = this.ReadFileBytes(path, "BIN", raw: false);
        var binaryRaw = this.ReadFileBytes(path, "BIN", raw: true);
        var basic = this.ReadFileBytes(path, "BAS", raw: false);
        var basicRaw = this.ReadFileBytes(path, "BAS", raw: true);
        var integer = this.ReadFileBytes(path, "INT", raw: false);
        var integerRaw = this.ReadFileBytes(path, "INT", raw: true);

        Assert.Multiple(() =>
        {
            Assert.That(binary, Is.EqualTo(new byte[] { 0x11, 0x22 }));
            Assert.That(binaryRaw, Has.Length.EqualTo(Dos33Layout.SectorSize));
            Assert.That(binaryRaw.AsSpan(0, 6).ToArray(), Is.EqualTo(new byte[] { 0x00, 0x08, 0x02, 0x00, 0x11, 0x22 }));
            Assert.That(binaryRaw[6], Is.EqualTo(0xFF));
            Assert.That(basic, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
            Assert.That(basicRaw, Has.Length.EqualTo(Dos33Layout.SectorSize));
            Assert.That(basicRaw[0], Is.EqualTo(0x03));
            Assert.That(basicRaw[5], Is.EqualTo(0xEE));
            Assert.That(integer, Is.EqualTo(new byte[] { 0x42 }));
            Assert.That(integerRaw, Has.Length.EqualTo(Dos33Layout.SectorSize));
            Assert.That(integerRaw[0], Is.EqualTo(0x01));
            Assert.That(integerRaw[3], Is.EqualTo(0xDD));
        });
    }

    /// <summary>
    /// ProDOS and Pascal logical reads are the directory length, including zeros inside that length.
    /// </summary>
    [Test]
    public void Read_ProdosAndPascal_HonorDirectoryEof()
    {
        var prodosBuilder = new ProdosImageBuilder(ProdosLayout.StandardFloppyBlocks, "EOFVOL");
        var noteBytes = new byte[] { 0x48, 0x69, 0x00, 0x21 };
        prodosBuilder.AddSeedling("NOTE", ProdosFileType.Text, 0, noteBytes, false);
        var tail = new byte[6];
        tail[0] = 0x10;
        tail[5] = 0x20;
        prodosBuilder.AddSapling("SPARSE", ProdosFileType.Binary, 0, new byte[]?[] { null, tail }, false);
        var prodos = this.Write(prodosBuilder.Build().Image, "eof-pro.hdv");
        var pascalBuilder = new PascalImageBuilder(8, "EOFVOL");
        pascalBuilder.AddFile("NOTE.TEXT", PascalFileKind.Text, noteBytes);
        pascalBuilder.AddFile("CODE.CODE", PascalFileKind.Code, new byte[] { 0x01, 0x02, 0x03 });
        var pascal = this.Write(pascalBuilder.Build().Image, "eof-pas.hdv");

        var note = this.read.Execute(this.context, ["--image", prodos, "NOTE", "--json", "--to", "note.bin"]);
        using var noteJson = JsonDocument.Parse(this.output.ToString());
        var noteFile = File.ReadAllBytes(Path.Join(this.exportRoot, "note.bin"));
        this.ClearOutput();
        var sparse = this.ReadFileBytes(prodos, "SPARSE", raw: false);
        var pascalText = this.read.Execute(this.context, ["--image", pascal, "NOTE.TEXT", "--json"]);
        using var pascalTextJson = JsonDocument.Parse(this.output.ToString());
        this.ClearOutput();
        var pascalCode = this.ReadFileBytes(pascal, "CODE.CODE", raw: true);
        var notePayload = noteJson.RootElement.GetProperty("payload");
        var pascalPayload = pascalTextJson.RootElement.GetProperty("payload");

        Assert.Multiple(() =>
        {
            Assert.That(note.Success, Is.True, note.Message);
            Assert.That(notePayload.GetProperty("length").GetInt32(), Is.EqualTo(noteBytes.Length));
            Assert.That(notePayload.GetProperty("data").GetString(), Is.EqualTo(Convert.ToHexStringLower(noteBytes)));
            Assert.That(notePayload.GetProperty("text").GetString(), Is.EqualTo("Hi\\x00!"));
            Assert.That(noteFile, Is.EqualTo(noteBytes));
            Assert.That(sparse, Has.Length.EqualTo(ProdosLayout.BlockSize + tail.Length));
            Assert.That(sparse[0], Is.EqualTo(0));
            Assert.That(sparse[ProdosLayout.BlockSize - 1], Is.EqualTo(0));
            Assert.That(sparse[ProdosLayout.BlockSize], Is.EqualTo(0x10));
            Assert.That(sparse[^1], Is.EqualTo(0x20));
            Assert.That(pascalText.Success, Is.True, pascalText.Message);
            Assert.That(pascalPayload.GetProperty("length").GetInt32(), Is.EqualTo(noteBytes.Length));
            Assert.That(pascalPayload.GetProperty("data").GetString(), Is.EqualTo(Convert.ToHexStringLower(noteBytes)));
            Assert.That(pascalPayload.GetProperty("text").GetString(), Is.EqualTo("Hi\\x00!"));
            Assert.That(pascalCode, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
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
            Assert.That(prodosOutput, Does.Contain("SLOT 6  DRIVE 1"));
            Assert.That(prodosOutput, Does.Contain("/BLANK/SUB"));
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

    private static byte[] PaddedSector(byte[] head, byte pad)
    {
        var sector = new byte[Dos33Layout.SectorSize];
        Array.Fill(sector, pad);
        head.CopyTo(sector, 0);
        return sector;
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

    private byte[] ReadFileBytes(string image, string name, bool raw)
    {
        string[] args = raw
            ? ["--image", image, name, "--raw", "--json"]
            : ["--image", image, name, "--json"];
        var result = this.read.Execute(this.context, args);
        Assert.That(result.Success, Is.True, result.Message);
        using var document = JsonDocument.Parse(this.output.ToString());
        var data = Convert.FromHexString(document.RootElement.GetProperty("payload").GetProperty("data").GetString()!);
        this.ClearOutput();
        return data;
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