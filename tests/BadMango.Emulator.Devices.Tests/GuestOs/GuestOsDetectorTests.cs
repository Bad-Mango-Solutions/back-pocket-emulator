// <copyright file="GuestOsDetectorTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;

using Serilog.Core;

/// <summary>
/// On-demand DOS 3.3 and ProDOS 8 detection against memory images built in the test.
/// </summary>
[TestFixture]
public class GuestOsDetectorTests
{
    /// <summary>Two infos with the same values compare equal.</summary>
    [Test]
    public void GuestOsInfo_WithTheSameValues_ComparesEqual()
    {
        var left = new GuestOsInfo(GuestOsKind.ProDos8, "ProDOS 8 2.4.x", 0x24, 7, 0x01, true, "matched");
        var right = new GuestOsInfo(GuestOsKind.ProDos8, "ProDOS 8 2.4.x", 0x24, 7, 0x01, true, "matched");

        Assert.That(left, Is.EqualTo(right));
    }

    /// <summary>Zeroed RAM is not an operating system.</summary>
    [Test]
    public void Detect_ZeroedRam_ReturnsNone()
    {
        var detector = CreateDetector(new GuestMemoryImage());

        var info = detector.Detect();

        Assert.That(info.Kind, Is.EqualTo(GuestOsKind.None));
        Assert.That(info.Reason, Is.EqualTo("No DOS 3.3 or ProDOS 8 signature."));
        Assert.That(info.KVersion, Is.Null);
        Assert.That(info.HasClock, Is.False);
    }

    /// <summary>Each known KVERSION maps to its label, and the global-page bytes are reported.</summary>
    /// <param name="kVersion">The byte planted at <c>$BFFF</c>.</param>
    /// <param name="label">The label that byte must produce.</param>
    [TestCase(0x00, "ProDOS 8 1.0.x")]
    [TestCase(0x01, "ProDOS 8 1.1.x")]
    [TestCase(0x02, "ProDOS 8 1.2")]
    [TestCase(0x03, "ProDOS 8 1.3")]
    [TestCase(0x04, "ProDOS 8 1.4")]
    [TestCase(0x05, "ProDOS 8 1.5")]
    [TestCase(0x06, "ProDOS 8 1.6")]
    [TestCase(0x07, "ProDOS 8 1.7")]
    [TestCase(0x08, "ProDOS 8 1.8/1.9")]
    [TestCase(0x21, "ProDOS 8 2.0.1")]
    [TestCase(0x22, "ProDOS 8 2.0.2")]
    [TestCase(0x23, "ProDOS 8 2.0.3")]
    [TestCase(0x24, "ProDOS 8 2.4.x")]
    public void Detect_KnownKVersion_MapsToLabel(int kVersion, string label)
    {
        var image = new GuestMemoryImage();
        image.PlantProDos((byte)kVersion, machId: 0x01, iVersion: 7, dateTimeOpcode: 0x4C, driver: 0xFF00);
        var detector = CreateDetector(image);

        var info = detector.Detect();

        Assert.Multiple(() =>
        {
            Assert.That(info.Kind, Is.EqualTo(GuestOsKind.ProDos8));
            Assert.That(info.VersionLabel, Is.EqualTo(label));
            Assert.That(info.KVersion, Is.EqualTo((byte)kVersion));
            Assert.That(info.IVersion, Is.EqualTo(7));
            Assert.That(info.MachId, Is.EqualTo(0x01));
            Assert.That(info.HasClock, Is.True);
            Assert.That(info.Reason, Does.Contain("ProDOS 8"));
        });
    }

    /// <summary>An unknown KVERSION is Unknown, with the byte in the reason.</summary>
    /// <param name="kVersion">A byte that is not in the ProDOS 8 table.</param>
    [TestCase(0x09)]
    [TestCase(0x20)]
    [TestCase(0x25)]
    [TestCase(0xFF)]
    public void Detect_UnknownKVersion_ReturnsUnknownWithReason(int kVersion)
    {
        var image = new GuestMemoryImage();
        image.PlantProDos((byte)kVersion);
        var detector = CreateDetector(image);

        var info = detector.Detect();

        Assert.Multiple(() =>
        {
            Assert.That(info.Kind, Is.EqualTo(GuestOsKind.Unknown));
            Assert.That(info.VersionLabel, Is.EqualTo("Unknown"));
            Assert.That(info.KVersion, Is.EqualTo((byte)kVersion));
            Assert.That(info.Reason, Does.Contain($"KVERSION ${kVersion:X2}"));
            Assert.That(info.Reason, Does.Contain("not a known ProDOS 8 version"));
        });
    }

    /// <summary>MACHID bit 0 is the clock flag. The DATETIME opcode is not.</summary>
    [Test]
    public void Detect_ProDosWithoutClockBit_ReportsHasClockFalse()
    {
        var image = new GuestMemoryImage();
        image.PlantProDos(0x24, machId: 0x02, dateTimeOpcode: 0x4C);

        var info = CreateDetector(image).Detect();

        Assert.That(info.Kind, Is.EqualTo(GuestOsKind.ProDos8));
        Assert.That(info.HasClock, Is.False);
        Assert.That(info.MachId, Is.EqualTo(0x02));
    }

    /// <summary>Page-3 DOS vectors do not hide a ProDOS global page.</summary>
    [Test]
    public void Detect_ProDosWithDosPage3_ReturnsProDos8()
    {
        var image = new GuestMemoryImage();
        image.PlantProDos(0x24, machId: 0x01, iVersion: 7);
        image.PlantDos33();

        var info = CreateDetector(image).Detect();

        Assert.Multiple(() =>
        {
            Assert.That(info.Kind, Is.EqualTo(GuestOsKind.ProDos8));
            Assert.That(info.VersionLabel, Is.EqualTo("ProDOS 8 2.4.x"));
        });
    }

    /// <summary>The DOS probe itself rejects a ProDOS page, even when it runs first.</summary>
    [Test]
    public void DosProbe_WhenProDosPageIsPresent_Rejects()
    {
        var image = new GuestMemoryImage();
        image.PlantProDos(0x24);
        image.PlantDos33();
        var detector = new GuestOsDetector(Logger.None, [new Dos33GuestOsProbe(), new ProDos8GuestOsProbe()], image);

        var info = detector.Detect();

        Assert.That(info.Kind, Is.EqualTo(GuestOsKind.ProDos8));
    }

    /// <summary>Standard 48K vectors are DOS 3.3.</summary>
    [Test]
    public void Detect_StandardDosVectors_ReturnsDos33()
    {
        var image = new GuestMemoryImage();
        image.PlantDos33();

        var info = CreateDetector(image).Detect();

        Assert.Multiple(() =>
        {
            Assert.That(info.Kind, Is.EqualTo(GuestOsKind.Dos33));
            Assert.That(info.VersionLabel, Is.EqualTo("DOS 3.3"));
            Assert.That(info.Reason, Does.Contain("48K"));
            Assert.That(info.KVersion, Is.Null);
            Assert.That(info.HasClock, Is.False);
        });
    }

    /// <summary>JMP vectors with other targets are a DOS 3.3-compatible system.</summary>
    [Test]
    public void Detect_NonstandardDosVectors_ReturnsCompatibleLabel()
    {
        var image = new GuestMemoryImage();
        image.PlantDos33(standard: false);

        var info = CreateDetector(image).Detect();

        Assert.That(info.Kind, Is.EqualTo(GuestOsKind.Dos33));
        Assert.That(info.VersionLabel, Is.EqualTo("DOS 3.3-compatible (nonstandard vectors)"));
    }

    /// <summary>A page that is missing one JMP is not DOS.</summary>
    [Test]
    public void Detect_DosMissingOneJmp_ReturnsNone()
    {
        var image = new GuestMemoryImage();
        image.PlantDos33();
        image.Poke(0x03EA, 0x60);

        var info = CreateDetector(image).Detect();

        Assert.That(info.Kind, Is.EqualTo(GuestOsKind.None));
    }

    /// <summary>Changed fires once per transition and not when Detect repeats the same result.</summary>
    [Test]
    public void Detect_Changed_FiresOncePerTransition()
    {
        var image = new GuestMemoryImage();
        var detector = CreateDetector(image);
        var transitions = new List<GuestOsKind>();
        detector.Changed += (_, args) =>
        {
            Assert.That(detector.Current, Is.SameAs(args.Current));
            transitions.Add(args.Current.Kind);
        };

        Assert.That(detector.Current.Kind, Is.EqualTo(GuestOsKind.None));
        Assert.That(detector.Current.Reason, Is.EqualTo("Guest OS detection has not run."));

        detector.Detect();
        detector.Detect();
        image.PlantProDos(0x24);
        detector.Detect();
        detector.Detect();
        image.Clear();
        image.PlantProDos(0x21);
        detector.Detect();
        image.Clear();
        image.PlantDos33();
        detector.Detect();
        detector.Detect();

        Assert.That(transitions, Is.EqualTo(new[]
        {
            GuestOsKind.None,
            GuestOsKind.ProDos8,
            GuestOsKind.ProDos8,
            GuestOsKind.Dos33,
        }));
        Assert.That(detector.Current.VersionLabel, Is.EqualTo("DOS 3.3"));
    }

    /// <summary>The structural checks each fail on their own.</summary>
    [Test]
    public void ProDosGlobalPage_EachStructuralCheck_FailsAlone()
    {
        var missingJmp = new GuestMemoryImage();
        missingJmp.PlantProDos(0x24);
        missingJmp.Poke(0xBF00, 0x60);
        Assert.That(ProDosGlobalPage.Inspect(missingJmp).StructuralRejection, Is.EqualTo("$BF00 is not a JMP."));

        var badDateTime = new GuestMemoryImage();
        badDateTime.PlantProDos(0x24, dateTimeOpcode: 0xEA);
        Assert.That(ProDosGlobalPage.Inspect(badDateTime).StructuralRejection, Is.EqualTo("$BF06 is not RTS or JMP."));

        var lowDriver = new GuestMemoryImage();
        lowDriver.PlantProDos(0x24, driver: 0xCFFF);
        Assert.That(ProDosGlobalPage.Inspect(lowDriver).StructuralRejection, Is.EqualTo("DATETIME vector is outside $D000-$FFFF."));

        var atCard = new GuestMemoryImage();
        atCard.PlantProDos(0x24, driver: 0xD000);
        Assert.That(ProDosGlobalPage.Inspect(atCard).IsKnownProDos8, Is.True);

        var atTop = new GuestMemoryImage();
        atTop.PlantProDos(0x24, dateTimeOpcode: 0x60, driver: 0xFFFF);
        Assert.That(ProDosGlobalPage.Inspect(atTop).IsKnownProDos8, Is.True);
    }

    /// <summary>RTS and JMP are both legal DATETIME opcodes.</summary>
    /// <param name="opcode">The byte at <c>$BF06</c>.</param>
    [TestCase(0x60)]
    [TestCase(0x4C)]
    public void ProDosGlobalPage_DateTimeOpcode_AcceptsRtsAndJmp(int opcode)
    {
        var image = new GuestMemoryImage();
        image.PlantProDos(0x24, dateTimeOpcode: (byte)opcode);

        Assert.That(ProDosGlobalPage.Inspect(image).IsKnownProDos8, Is.True);
    }

    /// <summary>The vector constants are the 48K addresses from Beneath Apple DOS.</summary>
    [Test]
    public void Dos33VectorPage_StandardTargets_MatchThe48KAddresses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Dos33VectorPage.WarmStart, Is.EqualTo(0x03D0));
            Assert.That(Dos33VectorPage.WarmStartTarget, Is.EqualTo(0x9DBF));
            Assert.That(Dos33VectorPage.ColdStart, Is.EqualTo(0x03D3));
            Assert.That(Dos33VectorPage.ColdStartTarget, Is.EqualTo(0x9D84));
            Assert.That(Dos33VectorPage.FileManager, Is.EqualTo(0x03D6));
            Assert.That(Dos33VectorPage.FileManagerTarget, Is.EqualTo(0xAAFD));
            Assert.That(Dos33VectorPage.Rwts, Is.EqualTo(0x03D9));
            Assert.That(Dos33VectorPage.RwtsTarget, Is.EqualTo(0xB7B5));
            Assert.That(Dos33VectorPage.Reconnect, Is.EqualTo(0x03EA));
            Assert.That(Dos33VectorPage.ReconnectTarget, Is.EqualTo(0xA851));
        });
    }

    /// <summary>Boot stages and milestone kinds are the set later issues bind to.</summary>
    [Test]
    public void BootContracts_NameTheStagesAndMilestones()
    {
        Assert.That(Enum.GetNames<GuestOsBootStage>(), Is.EqualTo(new[]
        {
            nameof(GuestOsBootStage.None),
            nameof(GuestOsBootStage.BootRom),
            nameof(GuestOsBootStage.Boot1),
            nameof(GuestOsBootStage.Loader),
            nameof(GuestOsBootStage.Ready),
            nameof(GuestOsBootStage.Unknown),
            nameof(GuestOsBootStage.CustomBoot),
        }));
        Assert.That((int)GuestOsBootStage.Unknown, Is.EqualTo(5));
        Assert.That((int)GuestOsBootStage.CustomBoot, Is.EqualTo(6));
        Assert.That(Enum.GetNames<BootMilestoneKind>(), Does.Contain(nameof(BootMilestoneKind.BootRomEntered)));
        Assert.That(Enum.GetNames<BootMilestoneKind>(), Does.Contain(nameof(BootMilestoneKind.FirstMliCall)));
        Assert.That(Enum.GetNames<BootMilestoneKind>(), Does.Contain(nameof(BootMilestoneKind.CustomBootLoaderEntered)));
        Assert.That((int)BootMilestoneKind.FirstMliCall, Is.EqualTo(7));
        Assert.That((int)BootMilestoneKind.CustomBootLoaderEntered, Is.EqualTo(8));
    }

    private static GuestOsDetector CreateDetector(IGuestMemoryReader memory) =>
        new(Logger.None, [new ProDos8GuestOsProbe(), new Dos33GuestOsProbe()], memory);
}