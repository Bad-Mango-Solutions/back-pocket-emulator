// <copyright file="LoopbackDiskTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Disk topology, mount, and eject on the loopback. Paths are recorded and not opened.
/// </summary>
[TestFixture]
public class LoopbackDiskTests
{
    /// <summary>
    /// Mount and eject update the drive list, and a missing file is not read.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task MountListEject_RecordsThePathWithoutOpeningIt()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        const string Path = "missing/no-such-image.dsk";

        IReadOnlyList<HostDiskController> before = await session.Control.ListDisksAsync(id);
        await session.Control.MountDiskAsync(id, new DriveId(6, 1), Path, writeProtect: true);
        IReadOnlyList<HostDiskController> mounted = await session.Control.ListDisksAsync(id);
        await session.Control.EjectDiskAsync(id, new DriveId(6, 1));
        HostDrive ejected = (await session.Control.ListDisksAsync(id))[0].Drives[0];

        Assert.Multiple(() =>
        {
            Assert.That(before, Has.Count.EqualTo(1));
            Assert.That(before[0].Name, Is.EqualTo("Disk II"));
            Assert.That(before[0].Drives, Has.Count.EqualTo(2));
            Assert.That(before[0].Drives[0].ImagePath, Is.Null);
            Assert.That(before[0].Drives[0].IsBootDrive, Is.True);
            Assert.That(session.Control.GetMachineInfo(id).BootDriveId, Is.EqualTo("S6 D1"));
            Assert.That(mounted[0].Drives[0].ImagePath, Is.EqualTo(Path));
            Assert.That(mounted[0].Drives[0].ImageName, Is.EqualTo("no-such-image.dsk"));
            Assert.That(mounted[0].Drives[0].WriteProtected, Is.True);
            Assert.That(File.Exists(Path), Is.False);
            Assert.That(ejected.ImagePath, Is.Null);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.EjectDiskAsync(id, new DriveId(6, 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.MountDiskAsync(id, new DriveId(5, 1), Path, false));
    }

    /// <summary>
    /// Write-protect is a host flag, and a busy drive refuses eject.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task WriteProtectAndBusy_FollowTheDriveFlags()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        DriveId drive = new(6, 2);
        await session.Control.MountDiskAsync(id, drive, "blank.po", writeProtect: false);
        await session.Control.SetWriteProtectAsync(id, drive, true);
        ((ILoopbackMachineEdge)session).SetDriveStatus(id, drive, new HostDriveStatus(Active: true, Dirty: true, Busy: true, Track: 0x12, Block: null, Error: null));

        HostDrive busy = (await session.Control.ListDisksAsync(id))[0].Drives[1];
        Assert.Multiple(() =>
        {
            Assert.That(busy.WriteProtected, Is.True);
            Assert.That(busy.Dirty, Is.True);
            Assert.That(busy.Active, Is.True);
            Assert.That(busy.Busy, Is.True);
            Assert.That(busy.Track, Is.EqualTo(0x12));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Control.EjectDiskAsync(id, drive));
    }

    /// <summary>
    /// The slot strip names the Disk II in slot 6 and leaves the other slots empty.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task ListSlots_ReportsDiskIIInSlot6()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        IReadOnlyList<HostSlotInfo> slots = await session.Control.ListSlotsAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(slots[0].Id, Is.EqualTo("aux"));
            Assert.That(slots[6].CardName, Is.EqualTo("Disk II"));
            Assert.That(slots[1].CardName, Is.Null);
        });
    }
}