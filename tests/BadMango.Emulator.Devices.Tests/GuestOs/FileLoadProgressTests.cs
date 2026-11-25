// <copyright file="FileLoadProgressTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// The shared file-load line. Pre-MLI progress (#700) prints the same text.
/// </summary>
[TestFixture]
public class FileLoadProgressTests
{
    /// <summary>A known size prints the count, the size, and the percent.</summary>
    [Test]
    public void FormatLine_KnownSize_PrintsPercent()
    {
        var progress = new FileLoadProgress("/DISK/BASIC.SYSTEM", 10240, 6656, 2560, 1, 1, true);

        Assert.Multiple(() =>
        {
            Assert.That(progress.Label, Is.EqualTo("BASIC.SYSTEM"));
            Assert.That(progress.FormatLine(), Is.EqualTo("BASIC.SYSTEM 6656/10240 (65%)"));
        });
    }

    /// <summary>An unknown size prints the byte count only.</summary>
    [Test]
    public void FormatLine_UnknownSize_PrintsBytesOnly()
    {
        var progress = new FileLoadProgress("/DISK/DRIVER", null, 100, 100, 1, 2, true);

        Assert.Multiple(() =>
        {
            Assert.That(progress.Label, Is.EqualTo("DRIVER"));
            Assert.That(progress.FormatLine(), Is.EqualTo("DRIVER 100"));
        });
    }

    /// <summary>A missing name is <c>unknown</c>. A finished read of a known size caps at 100%.</summary>
    [Test]
    public void FormatLine_EmptyPathAndCompleteFile()
    {
        var unknown = new FileLoadProgress(string.Empty, null, 4, 0, 0, null, true);
        var done = new FileLoadProgress("PRODOS", 1024, 1024, 1024, 10, null, false);
        var empty = new FileLoadProgress("EMPTY", 0, 0, 0, 1, null, true);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.FormatLine(), Is.EqualTo("unknown 4"));
            Assert.That(done.FormatLine(), Is.EqualTo("PRODOS 1024/1024 (100%)"));
            Assert.That(empty.FormatLine(), Is.EqualTo("EMPTY 0/0 (100%)"));
        });
    }
}