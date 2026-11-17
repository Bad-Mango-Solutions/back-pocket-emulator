// <copyright file="MachineIoSessionDiskTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Media;

using Moq;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Mount and unmount behavior for <see cref="MachineDebugSession"/>.
/// </summary>
/// <remarks>
/// Eject is Disk II unmount: flush pending writes, release the image, and leave the drive empty.
/// A false return from <see cref="IDiskController.Eject"/> means the flush failed or the drive was empty.
/// </remarks>
[TestFixture]
public sealed class MachineIoSessionDiskTests
{
    private string tempRoot = null!;
    private DebugContext context = null!;
    private Mock<ISlotManager> slotManager = null!;
    private Mock<IDiskController> controller = null!;
    private StringWriter log = null!;
    private Logger logger = null!;
    private MachineDebugSession session = null!;

    /// <summary>Creates a slot 6 Disk II stand-in and a blank 5.25" image.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempRoot = Path.Combine(Path.GetTempPath(), $"bms-io-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempRoot);

        var card = new Mock<ISlotCard>();
        this.controller = card.As<IDiskController>();
        card.SetupGet(c => c.Name).Returns("Disk II");
        this.controller.SetupGet(c => c.DriveCount).Returns(2);
        this.controller.Setup(c => c.Mount(It.IsAny<int>(), It.IsAny<I525Media>(), It.IsAny<string?>()));

        this.slotManager = new Mock<ISlotManager>();
        this.slotManager.Setup(s => s.GetCard(6)).Returns(card.Object);

        var machine = new Mock<IMachine>();
        machine.Setup(m => m.GetComponent<ISlotManager>()).Returns(this.slotManager.Object);

        var dispatcher = new CommandDispatcher();
        this.context = new DebugContext(dispatcher, TextWriter.Null, TextWriter.Null);
        this.context.AttachDiskImageFactory(new DiskImageFactory());
        this.context.AttachPathResolver(new DebugPathResolver());
        this.context.AttachMachine(machine.Object);

        this.log = new StringWriter();
        this.logger = new LoggerConfiguration().WriteTo.Sink(new TextLogSink(this.log)).CreateLogger();
        this.session = new MachineDebugSession(this.context, this.logger);
    }

    /// <summary>Removes the temp image directory.</summary>
    [TearDown]
    public void TearDown()
    {
        this.log.Dispose();
        this.context.Dispose();
        this.logger.Dispose();
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // The rollback-keeps-the-image test may still hold the file.
        }
    }

    /// <summary>A mount is tracked and a later unmount releases it.</summary>
    [Test]
    public void MountDisk_ThenEjectDisk_MountsAndReleasesTheImage()
    {
        string path = this.WriteBlankImage();
        this.controller.Setup(c => c.Eject(0)).Returns(true);

        this.session.MountDisk(6, 1, path);
        Assert.That(this.context.MountedDisks.Count, Is.EqualTo(1));

        this.session.EjectDisk(6, 1);

        Assert.Multiple(() =>
        {
            Assert.That(this.context.MountedDisks.Count, Is.EqualTo(0));
            this.controller.Verify(c => c.Mount(0, It.IsAny<I525Media>(), path), Times.Once);
            this.controller.Verify(c => c.Eject(0), Times.Once);
        });
    }

    /// <summary>A slot outside the peripheral range is rejected.</summary>
    [Test]
    public void MountDisk_BadSlot_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.session.MountDisk(0, 1, this.WriteBlankImage()));
    }

    /// <summary>An empty slot is not a disk controller.</summary>
    [Test]
    public void MountDisk_EmptySlot_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => this.session.MountDisk(4, 1, this.WriteBlankImage()));
        Assert.That(ex!.Message, Does.Contain("does not contain a disk controller"));
    }

    /// <summary>A drive number past the controller's drive count is rejected.</summary>
    [Test]
    public void MountDisk_BadDrive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.session.MountDisk(6, 3, this.WriteBlankImage()));
    }

    /// <summary>Unmounting an empty drive reports that the drive is empty.</summary>
    [Test]
    public void EjectDisk_EmptyDrive_Throws()
    {
        this.controller.Setup(c => c.Eject(0)).Returns(false);
        this.controller.Setup(c => c.GetDriveSnapshot(0)).Returns(EmptySnapshot());

        var ex = Assert.Throws<InvalidOperationException>(() => this.session.EjectDisk(6, 1));
        Assert.That(ex!.Message, Does.Contain("already empty"));
    }

    /// <summary>A flush failure leaves the image mounted.</summary>
    [Test]
    public void EjectDisk_FlushRejected_LeavesTheImageMounted()
    {
        string path = this.WriteBlankImage();
        this.controller.Setup(c => c.Eject(0)).Returns(true);
        this.session.MountDisk(6, 1, path);

        this.controller.Setup(c => c.Eject(0)).Returns(false);
        this.controller.Setup(c => c.GetDriveSnapshot(0)).Returns(EmptySnapshot() with { HasMedia = true, MountedImagePath = path });

        var ex = Assert.Throws<InvalidOperationException>(() => this.session.EjectDisk(6, 1));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("flush"));
            Assert.That(this.context.MountedDisks.Count, Is.EqualTo(1));
            Assert.That(this.log.ToString(), Does.Contain("could not be flushed"));
        });
    }

    /// <summary>A bad slot is rejected before the controller is touched.</summary>
    [Test]
    public void EjectDisk_BadSlot_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.session.EjectDisk(8, 1));
    }

    /// <summary>A drive number past the controller's drive count is rejected.</summary>
    [Test]
    public void EjectDisk_BadDrive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => this.session.EjectDisk(6, 9));
    }

    /// <summary>
    /// When mount tracking is already disposed, a successful unmount rolls the mount back
    /// and releases the image.
    /// </summary>
    [Test]
    public void MountDisk_WhenTrackingDisposed_RollsBackAndReleasesTheImage()
    {
        string path = this.WriteBlankImage();
        this.context.Dispose();
        this.controller.Setup(c => c.Eject(0)).Returns(true);

        var ex = Assert.Throws<InvalidOperationException>(() => this.session.MountDisk(6, 1, path));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("disposed"));
            this.controller.Verify(c => c.Eject(0), Times.Once);
            Assert.That(IsFileOpen(path), Is.False);
        });
    }

    /// <summary>
    /// When mount tracking is disposed and the rollback flush fails, the image stays open
    /// and the failure is logged.
    /// </summary>
    [Test]
    public void MountDisk_WhenTrackingDisposedAndFlushFails_KeepsTheImageAndLogs()
    {
        string path = this.WriteBlankImage();
        this.context.Dispose();
        this.controller.Setup(c => c.Eject(0)).Returns(false);

        var ex = Assert.Throws<InvalidOperationException>(() => this.session.MountDisk(6, 1, path));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("remains mounted"));
            Assert.That(this.log.ToString(), Does.Contain("will not be disposed"));
            Assert.That(IsFileOpen(path), Is.True);
        });
    }

    private static DriveSnapshot EmptySnapshot() => new(
        Selected: false,
        MotorOn: false,
        PhaseLatch: 0,
        QuarterTrack: 0,
        WriteProtect: false,
        HasMedia: false,
        MountedImagePath: null);

    private static bool IsFileOpen(string path)
    {
        const string FdDirectory = "/proc/self/fd";
        if (!Directory.Exists(FdDirectory))
        {
            return false;
        }

        string full = Path.GetFullPath(path);
        foreach (string fd in Directory.EnumerateFiles(FdDirectory))
        {
            FileSystemInfo? link = File.ResolveLinkTarget(fd, returnFinalTarget: false);
            if (link is not null && string.Equals(link.FullName, full, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string WriteBlankImage()
    {
        string path = Path.Combine(this.tempRoot, $"img-{Guid.NewGuid():N}.dsk");
        File.WriteAllBytes(path, new byte[35 * 16 * 256]);
        return path;
    }

    private sealed class TextLogSink(StringWriter writer) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            writer.WriteLine(logEvent.RenderMessage());
        }
    }
}