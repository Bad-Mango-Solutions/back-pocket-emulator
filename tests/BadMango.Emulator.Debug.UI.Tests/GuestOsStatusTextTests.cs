// <copyright file="GuestOsStatusTextTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.UI.Tests;

using BadMango.Emulator.Debug.Infrastructure.Status;
using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Display text for the guest-OS status row. The words are media-neutral.
/// </summary>
[TestFixture]
public class GuestOsStatusTextTests
{
    /// <summary>
    /// Each boot stage uses the status-monitor word, with no card or disk name.
    /// </summary>
    /// <param name="stage">The stage to format.</param>
    /// <param name="expected">The display word.</param>
    [TestCase(GuestOsBootStage.None, "\u2014")]
    [TestCase(GuestOsBootStage.Init, "Init")]
    [TestCase(GuestOsBootStage.BootDeviceSelected, "Boot device")]
    [TestCase(GuestOsBootStage.BootRom, "Boot ROM")]
    [TestCase(GuestOsBootStage.Boot1, "Boot 1")]
    [TestCase(GuestOsBootStage.Loader, "Loader")]
    [TestCase(GuestOsBootStage.Ready, "Ready")]
    [TestCase(GuestOsBootStage.Unknown, "Unknown")]
    [TestCase(GuestOsBootStage.CustomBoot, "Custom boot")]
    [TestCase(GuestOsBootStage.NoBootDevice, "No boot device")]
    public void FormatStage_IsMediaNeutral(GuestOsBootStage stage, string expected)
    {
        var text = GuestOsStatusText.FormatStage(stage);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(text, Does.Not.Contain("Disk"));
            Assert.That(text, Does.Not.Contain("sector"));
            Assert.That(text, Does.Not.Contain("track"));
            Assert.That(text, Does.Not.Contain("slot"));
            Assert.That(text, Does.Not.Contain("SmartPort"));
        });
    }

    /// <summary>
    /// Every named stage has a display word. A new stage has to be added here.
    /// </summary>
    [Test]
    public void FormatStage_CoversEveryNamedStage()
    {
        var expected = new Dictionary<GuestOsBootStage, string>
        {
            [GuestOsBootStage.None] = "\u2014",
            [GuestOsBootStage.Init] = "Init",
            [GuestOsBootStage.BootDeviceSelected] = "Boot device",
            [GuestOsBootStage.BootRom] = "Boot ROM",
            [GuestOsBootStage.Boot1] = "Boot 1",
            [GuestOsBootStage.Loader] = "Loader",
            [GuestOsBootStage.Ready] = "Ready",
            [GuestOsBootStage.Unknown] = "Unknown",
            [GuestOsBootStage.CustomBoot] = "Custom boot",
            [GuestOsBootStage.NoBootDevice] = "No boot device",
        };

        Assert.That(Enum.GetValues<GuestOsBootStage>(), Is.EquivalentTo(expected.Keys));
        Assert.Multiple(() =>
        {
            foreach (var pair in expected)
            {
                Assert.That(GuestOsStatusText.FormatStage(pair.Key), Is.EqualTo(pair.Value));
            }
        });
    }

    /// <summary>
    /// A version label that already starts with the display name is not repeated.
    /// </summary>
    [Test]
    public void FormatOperatingSystem_VersionLabelIncludesDisplayName()
    {
        var guestOs = new GuestOsInfo(GuestOsId.ProDos8, "ProDOS 8", "ProDOS 8 2.4.x", "kversion", null);

        Assert.That(
            GuestOsStatusText.FormatOperatingSystem(guestOs, GuestOsBootStage.Ready),
            Is.EqualTo("ProDOS 8 2.4.x"));
    }

    /// <summary>
    /// A separate version is appended to the display name.
    /// </summary>
    [Test]
    public void FormatOperatingSystem_AppendsDistinctVersionLabel()
    {
        var guestOs = new GuestOsInfo(new GuestOsId("geos"), "GEOS", "2.1", "matched", null);

        Assert.That(
            GuestOsStatusText.FormatOperatingSystem(guestOs, GuestOsBootStage.Ready),
            Is.EqualTo("GEOS 2.1"));
    }

    /// <summary>
    /// An empty version label leaves the display name on its own.
    /// </summary>
    [Test]
    public void FormatOperatingSystem_EmptyVersionLabel_UsesDisplayName()
    {
        var guestOs = new GuestOsInfo(new GuestOsId("pascal"), "Apple Pascal", string.Empty, "matched", null);

        Assert.That(
            GuestOsStatusText.FormatOperatingSystem(guestOs, GuestOsBootStage.Ready),
            Is.EqualTo("Apple Pascal"));
    }

    /// <summary>
    /// The sample lines from the status-monitor issue.
    /// </summary>
    [Test]
    public void FormatLine_MatchesSampleRows()
    {
        var proDos = new GuestOsInfo(GuestOsId.ProDos8, "ProDOS 8", "ProDOS 8 2.4.x", "kversion", null);
        var dos = new GuestOsInfo(GuestOsId.Dos33, "DOS 3.3", "DOS 3.3", "vectors", null);
        var none = new GuestOsInfo(GuestOsId.None, "None", string.Empty, "no signature", null);
        var unknown = new GuestOsInfo(GuestOsId.Unknown, "Unknown", "Unknown", "kversion", null);

        Assert.Multiple(() =>
        {
            Assert.That(GuestOsStatusText.FormatLine(proDos, GuestOsBootStage.Ready), Is.EqualTo("OS: ProDOS 8 2.4.x     Stage: Ready"));
            Assert.That(GuestOsStatusText.FormatLine(dos, GuestOsBootStage.Ready), Is.EqualTo("OS: DOS 3.3     Stage: Ready"));
            Assert.That(
                GuestOsStatusText.FormatLine(none, GuestOsBootStage.Boot1),
                Is.EqualTo($"OS: {GuestOsStatusText.NotEntered}     Stage: Boot 1"));
            Assert.That(
                GuestOsStatusText.FormatLine(none, GuestOsBootStage.CustomBoot),
                Is.EqualTo("OS: None (custom loader)     Stage: Custom boot"));
            Assert.That(GuestOsStatusText.FormatLine(unknown, GuestOsBootStage.Unknown), Is.EqualTo("OS: Unknown     Stage: Unknown"));
            Assert.That(
                GuestOsStatusText.FormatLine(null, GuestOsBootStage.None),
                Is.EqualTo($"OS: (detection off)     Stage: {GuestOsStatusText.NotEntered}"));
        });
    }

    /// <summary>
    /// The no-op stage source reports None.
    /// </summary>
    [Test]
    public void NoneGuestOsStageSource_ReportsNone()
    {
        Assert.That(NoneGuestOsStageSource.Instance.Stage, Is.EqualTo(GuestOsBootStage.None));
        Assert.That(new NoneGuestOsStageSource().Stage, Is.EqualTo(GuestOsBootStage.None));
    }
}