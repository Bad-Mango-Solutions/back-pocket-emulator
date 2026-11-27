// <copyright file="InProcessHostDiskTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Host;

/// <summary>
/// Removable media and fixed storage on a live machine. Images are synthetic. The ROM is <c>boot.rom</c>.
/// </summary>
[TestFixture]
[CancelAfter(20000)]
public class InProcessHostDiskTests
{
    private const int SectorImageBytes = 35 * 16 * 256;

    /// <summary>
    /// The slot strip and the boot drive come from the machine, and a synthetic image mounts, write-protects, and ejects.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task DiskTopology_MountsASyntheticImageOnTheLiveController()
    {
        string root = NewLibrary();
        try
        {
            string image = Path.Combine(root, "disks", "blank.dsk");
            File.WriteAllBytes(image, new byte[SectorImageBytes]);
            File.WriteAllBytes(Path.Combine(root, "drives", "volume.hdv"), new byte[512]);
            using InProcessHostSession session = Open(root);
            MachineId id = Add(session, "Disks", startPaused: true);

            IReadOnlyList<HostSlotInfo> slots = session.Control.ListSlots(id);
            IReadOnlyList<HostDiskController> empty = session.Control.ListDisks(id);
            await session.Control.AttachFixedStorageAsync(id, new DriveId(7, 1), "library://drives/volume.hdv", writeProtect: true);
            await session.Control.MountDiskAsync(id, new DriveId(6, 1), "library://disks/blank.dsk", writeProtect: false);

            HostDrive mounted = Find(session.Control.ListDisks(id), new DriveId(6, 1));
            HostDrive volume = Find(session.Control.ListDisks(id), new DriveId(7, 1));
            Assert.Multiple(() =>
            {
                Assert.That(slots[6].CardName, Is.EqualTo("Disk II Controller"));
                Assert.That(slots[1].CardName, Is.Null);
                Assert.That(empty, Has.Count.EqualTo(1));
                Assert.That(empty[0].Category, Is.EqualTo(HostStorageCategory.RemovableMedia));
                Assert.That(empty[0].Drives, Has.Count.EqualTo(2));
                Assert.That(empty[0].Drives[0].IsBootDrive, Is.True);
                Assert.That(session.Control.GetMachineInfo(id).BootDriveId, Is.EqualTo("S6 D1"));
                Assert.That(mounted.ImageName, Is.EqualTo("blank.dsk"));
                Assert.That(mounted.ImagePath, Does.Contain(Path.Combine("disks", "blank.dsk")));
                Assert.That(mounted.WriteProtected, Is.False);
                Assert.That(mounted.Category, Is.EqualTo(HostStorageCategory.RemovableMedia));
                Assert.That(volume.Category, Is.EqualTo(HostStorageCategory.FixedStorage));
                Assert.That(volume.ImagePath, Is.EqualTo("library://drives/volume.hdv"));
            });

            await session.Control.SetWriteProtectAsync(id, new DriveId(6, 1), true);
            await session.Control.EjectDiskAsync(id, new DriveId(6, 1));
            HostDrive ejected = Find(session.Control.ListDisks(id), new DriveId(6, 1));
            Assert.Multiple(() =>
            {
                Assert.That(ejected.ImagePath, Is.Null);
                Assert.That(ejected.WriteProtected, Is.False);
            });

            await Assert.ThrowsAsync<FileNotFoundException>(
                async () => await session.Control.AttachFixedStorageAsync(id, new DriveId(7, 2), "library://drives/missing.hdv", false));
        }
        finally
        {
            DeleteLibrary(root);
        }
    }

    /// <summary>
    /// Mount lands while the machine is running, paused, or stopped, and a spinning drive refuses the change.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task Mount_WorksWhileRunningPausedAndStopped_AndRefusesABusyDrive()
    {
        string root = NewLibrary();
        try
        {
            string image = Path.Combine(root, "blank.dsk");
            File.WriteAllBytes(image, new byte[SectorImageBytes]);
            using InProcessHostSession session = Open(root);
            MachineId id = Add(session, "States", startPaused: true);
            await session.Control.PowerOnAsync(id);

            await session.Control.MountDiskAsync(id, new DriveId(6, 2), image, writeProtect: true);
            Assert.That(Find(session.Control.ListDisks(id), new DriveId(6, 2)).ImageName, Is.EqualTo("blank.dsk"));

            WriteSoftSwitch(session, id, 0xC0E9);
            HostDrive busy = Find(session.Control.ListDisks(id), new DriveId(6, 1));
            Assert.That(busy.Busy, Is.True);
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await session.Control.MountDiskAsync(id, new DriveId(6, 1), image, false));

            // Motor-off arms the controller's hold while the loop is paused, so the drive stays busy.
            // Power off drops that hold. The next power-on is paused again.
            await session.Control.PowerOffAsync(id);
            await session.Control.PowerOnAsync(id);
            HoldIrq(session, id);
            await session.Control.ResumeAsync(id);
            await session.Control.MountDiskAsync(id, new DriveId(6, 1), image, writeProtect: true);
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Running));
            Assert.That(Find(session.Control.ListDisks(id), new DriveId(6, 1)).WriteProtected, Is.True);

            await session.Control.PauseAsync(id);
            ReleaseIrq(session, id);
            byte[] program = [0xDB, 0xEA, 0x4C, 0x01, 0x08];
            WriteProgram(session, id, 0x0800, program);
            await session.Control.ResumeAsync(id);
            await WaitForStateAsync(session, id, HostRunState.Stopped);
            await session.Control.EjectDiskAsync(id, new DriveId(6, 1));
            Assert.That(Find(session.Control.ListDisks(id), new DriveId(6, 1)).ImagePath, Is.Null);
            Assert.That(session.Control.GetMachineInfo(id).State, Is.EqualTo(HostRunState.Stopped));
        }
        finally
        {
            DeleteLibrary(root);
        }
    }

    private static string NewLibrary()
    {
        string root = Path.Combine(Path.GetTempPath(), "bpe-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "disks"));
        Directory.CreateDirectory(Path.Combine(root, "drives"));
        return root;
    }

    private static void DeleteLibrary(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A mounted image can keep the file open until the session dispose finishes. The temp directory is unique.
        }
    }

    private static InProcessHostSession Open(string libraryRoot)
    {
        return new InProcessHostTransport(libraryRoot).Connect(HostApi.Version);
    }

    private static MachineId Add(InProcessHostSession session, string name, bool startPaused)
    {
        session.Control.UpsertProfile(new HostProfileDocument("disk-host", "Disk host", DiskProfile()));
        return session.Control.CreateInstance(new HostInstanceSpec(name, "disk-host", startPaused, CpuSummary: "65C02"));
    }

    private static HostDrive Find(IReadOnlyList<HostDiskController> controllers, DriveId id)
    {
        foreach (HostDiskController controller in controllers)
        {
            foreach (HostDrive drive in controller.Drives)
            {
                if (drive.Id == id)
                {
                    return drive;
                }
            }
        }

        Assert.Fail($"Drive {id.Label} was not listed.");
        return null!;
    }

    private static void WriteSoftSwitch(InProcessHostSession session, MachineId id, uint address)
    {
        IMachine machine = session.GetMachine(id);
        BusResult written = machine.Bus.TryWrite8(
            new BusAccess(address, 0, 8, BusAccessMode.Atomic, true, AccessIntent.DataWrite, 0, 0, AccessFlags.None),
            0);
        Assert.That(written.Ok, Is.True, $"Write of ${address:X4} faulted with {written.Fault.Kind}.");
    }

    private static void WriteProgram(InProcessHostSession session, MachineId id, uint address, byte[] program)
    {
        IMachine machine = session.GetMachine(id);
        for (int index = 0; index < program.Length; index++)
        {
            BusResult written = machine.Bus.TryWrite8(
                new BusAccess((uint)(address + index), 0, 8, BusAccessMode.Atomic, true, AccessIntent.DataWrite, 0, 0, AccessFlags.None),
                program[index]);
            Assert.That(written.Fault.Kind, Is.EqualTo(FaultKind.None));
        }

        machine.Cpu.SetPC(address);
    }

    private static void HoldIrq(InProcessHostSession session, MachineId id)
    {
        IMachine machine = session.GetMachine(id);
        machine.Post(() => machine.Signals.Assert(SignalLine.IRQ, deviceId: 1, machine.Now));
    }

    private static void ReleaseIrq(InProcessHostSession session, MachineId id)
    {
        IMachine machine = session.GetMachine(id);
        machine.Post(() => machine.Signals.Deassert(SignalLine.IRQ, deviceId: 1, machine.Now));
    }

    private static async Task WaitForStateAsync(InProcessHostSession session, MachineId id, HostRunState state)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (session.Control.GetMachineInfo(id).State == state)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"State stayed {session.Control.GetMachineInfo(id).State}.");
    }

    private static string DiskProfile()
    {
        return """
            {
                "name": "disk-host",
                "displayName": "Disk host",
                "cpu": { "type": "65C02", "clockSpeed": 1020484 },
                "addressSpace": 16,
                "memory": {
                    "rom-images": [
                        {
                            "name": "boot-rom",
                            "source": "embedded://BadMango.Emulator.Devices/BadMango.Emulator.Devices.Resources.boot.rom",
                            "size": "0x0800",
                            "required": true,
                            "on_verification_fail": "stop"
                        }
                    ],
                    "physical": [
                        { "name": "main-ram", "size": "0xC000", "fill": "0x00" },
                        { "name": "high-ram", "size": "0x2000", "fill": "0x00" },
                        {
                            "name": "system-rom",
                            "size": "0x1000",
                            "fill": "0x00",
                            "sources": [{ "type": "rom-image", "name": "boot", "rom-image": "boot-rom", "offset": "0x0800" }]
                        }
                    ],
                    "regions": [
                        {
                            "name": "ram",
                            "type": "ram",
                            "start": "0x0000",
                            "size": "0xC000",
                            "permissions": "rwx",
                            "source": "main-ram",
                            "source-offset": "0x0000"
                        },
                        {
                            "name": "io-region",
                            "type": "composite",
                            "start": "0xC000",
                            "size": "0x1000",
                            "handler": "composite-io"
                        },
                        {
                            "name": "high-ram",
                            "type": "ram",
                            "start": "0xD000",
                            "size": "0x2000",
                            "permissions": "rwx",
                            "source": "high-ram",
                            "source-offset": "0x0000"
                        },
                        {
                            "name": "rom",
                            "type": "rom",
                            "start": "0xF000",
                            "size": "0x1000",
                            "permissions": "rx",
                            "source": "system-rom",
                            "source-offset": "0x0000"
                        }
                    ]
                },
                "devices": {
                    "slots": {
                        "io-region": "io-region",
                        "enabled": true,
                        "cards": [ { "slot": 6, "type": "disk-ii-compatible" } ]
                    }
                },
                "boot": { "startupSlot": 6 }
            }
            """;
    }
}