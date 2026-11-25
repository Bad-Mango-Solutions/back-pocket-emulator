// <copyright file="GuestOsBootProfileTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;
using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// DOS 3.3 and ProDOS 8 boot profiles own the signatures and the later milestone addresses.
/// </summary>
[TestFixture]
public class GuestOsBootProfileTests
{
    /// <summary>DOS 3.3 matches the shared boot-sector prefix and names Boot 2 and cold start.</summary>
    [Test]
    public void Dos33Profile_MatchesTheBootSector_AndNamesBoot2()
    {
        IGuestOsBootProfile profile = new Dos33GuestOsBootProfile();
        var sector = Dos33Layout.BootSector.Signature.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(profile.Id, Is.EqualTo(GuestOsId.Dos33));
            Assert.That(profile.MatchesBootSector(sector), Is.True);
            Assert.That(profile.MilestoneTraps, Is.EqualTo(new[]
            {
                new GuestOsBootTrap(0xB700, BootMilestoneKind.LoaderEntered),
                new GuestOsBootTrap(0x3700, BootMilestoneKind.LoaderEntered),
                new GuestOsBootTrap(0x9D84, BootMilestoneKind.KernelReady),
            }));
            Assert.That(profile.DescribeMilestone(BootMilestoneKind.LoaderEntered, 0xB700), Is.EqualTo("DOS 3.3 Boot 2 at $B700"));
            Assert.That(profile.DescribeMilestone(BootMilestoneKind.LoaderEntered, 0x3700), Is.EqualTo("DOS 3.3 Boot 2 at $3700"));
            Assert.That(profile.DescribeMilestone(BootMilestoneKind.KernelReady, 0x9D84), Is.EqualTo("DOS 3.3 cold start"));
            Assert.That(profile.SessionTraps, Is.EqualTo(new[]
            {
                new GuestOsSessionTrap(Dos33VectorPage.FileManagerTarget, "DOS 3.3 file manager"),
                new GuestOsSessionTrap(Dos33VectorPage.RwtsTarget, "DOS 3.3 RWTS"),
            }));
        });
    }

    /// <summary>ProDOS 8 matches the shared boot block and names the loader, kernel, and MLI call.</summary>
    [Test]
    public void ProDosProfile_MatchesTheBootBlock_AndNamesMliOpen()
    {
        IGuestOsBootProfile profile = new ProDos8GuestOsBootProfile();
        var sector = ProdosLayout.BootBlock.Signature.ToArray();
        var memory = new GuestMemoryImage();
        memory.Poke(0x0303, 0xC8);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Id, Is.EqualTo(GuestOsId.ProDos8));
            Assert.That(profile.MatchesBootSector(sector), Is.True);
            Assert.That(profile.MilestoneTraps, Is.EqualTo(new[]
            {
                new GuestOsBootTrap(0x2000, BootMilestoneKind.LoaderEntered),
                new GuestOsBootTrap(0x2000, BootMilestoneKind.KernelReady),
                new GuestOsBootTrap(0x2000, BootMilestoneKind.SystemProgramStarted),
                new GuestOsBootTrap(0xBF00, BootMilestoneKind.KernelReady),
                new GuestOsBootTrap(0xBF00, BootMilestoneKind.SystemProgramStarted),
                new GuestOsBootTrap(0xBF00, BootMilestoneKind.FirstSystemCall),
            }));
            Assert.That(profile.DescribeMilestone(BootMilestoneKind.LoaderEntered, 0x2000), Is.EqualTo("ProDOS loader"));
            Assert.That(profile.DescribeSystemCall(memory, 0x0303), Is.EqualTo("MLI $C8 OPEN"));
            Assert.That(profile.SessionTraps, Is.Empty);
        });
    }
}