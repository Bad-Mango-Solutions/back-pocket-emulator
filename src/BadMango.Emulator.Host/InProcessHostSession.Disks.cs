// <copyright file="InProcessHostSession.Disks.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Host.Contracts;
using BadMango.Emulator.Storage.Formats;
using BadMango.Emulator.Storage.Media;

/// <content>
/// Disk and slot binding. Mount, eject, and write-protect reuse <see cref="IMachineIoSession"/>
/// and land on the machine thread at an instruction boundary while a run loop is running.
/// </content>
public sealed partial class InProcessHostSession
{
    /// <inheritdoc />
    public Task MountDiskAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        HostLibraryPaths.RequireRemovableMedia(path);
        MachineHandle handle = Find(machineId);
        return RunDiskAsync(handle, cancellationToken, () => MountCore(handle, drive, path, writeProtect));
    }

    /// <inheritdoc />
    public Task EjectDiskAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        MachineHandle handle = Find(machineId);
        return RunDiskAsync(handle, cancellationToken, () => EjectCore(handle, drive));
    }

    /// <inheritdoc />
    public Task SetWriteProtectAsync(MachineId machineId, DriveId drive, bool writeProtect, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        MachineHandle handle = Find(machineId);
        return RunDiskAsync(handle, cancellationToken, () => WriteProtectCore(handle, drive, writeProtect));
    }

    /// <inheritdoc />
    public Task AttachFixedStorageAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        HostLibraryPaths.RequireFixedStorage(path);
        RequireFixedAddress(drive);
        MachineHandle handle = Find(machineId);
        return RunDiskAsync(handle, cancellationToken, () => AttachCore(handle, drive, path, writeProtect));
    }

    /// <inheritdoc />
    public Task DetachFixedStorageAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        MachineHandle handle = Find(machineId);
        return RunDiskAsync(handle, cancellationToken, () => DetachCore(handle, drive));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HostDiskController>> ListDisksAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<HostDiskController>>(cancellationToken);
        }

        MachineHandle handle = Find(machineId);
        return ReadOnMachineAsync(handle, cancellationToken, () => ReadTopology(handle));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HostSlotInfo>> ListSlotsAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<HostSlotInfo>>(cancellationToken);
        }

        MachineHandle handle = Find(machineId);
        return ReadOnMachineAsync(handle, cancellationToken, () => ReadSlots(handle.Machine));
    }

    private async Task RunDiskAsync(MachineHandle handle, CancellationToken cancellationToken, Action action)
    {
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunAtInstructionBoundaryAsync(handle, action, cancellationToken).ConfigureAwait(false);
            Notify(sink => sink.OnDisksChanged(handle.Id));
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task<T> ReadOnMachineAsync<T>(MachineHandle handle, CancellationToken cancellationToken, Func<T> read)
    {
        T result = default!;
        await RunAtInstructionBoundaryAsync(handle, () => result = read(), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Runs <paramref name="action"/> at an instruction boundary while a loop is running.
    /// A stopped, paused, or off machine has no loop to drain a queued post, so the post runs
    /// inline when the machine is quiescent and this wait ends when that inline call returns.
    /// </summary>
    /// <param name="handle">The machine.</param>
    /// <param name="action">The disk or slot work.</param>
    /// <param name="cancellationToken">Cancels the wait. A post that already started still finishes.</param>
    /// <returns>A task that completes when <paramref name="action"/> has run on the machine thread.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the post does not run before the timeout.</exception>
    private async Task RunAtInstructionBoundaryAsync(MachineHandle handle, Action action, CancellationToken cancellationToken)
    {
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Machine.Post(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception ex) when (ex is not (
                OutOfMemoryException
                or AccessViolationException
                or InvalidProgramException
                or BadImageFormatException))
            {
                done.TrySetException(ex);
            }
        });

        try
        {
            await done.Task.WaitAsync(RunStartTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The disk command did not land on the machine thread.");
        }
    }

    private void MountCore(MachineHandle handle, DriveId drive, string path, bool writeProtect)
    {
        RejectFixedTarget(handle, drive);
        DiskDriveSnapshot snapshot = RequireRemovable(handle, drive);
        if (IsBusy(handle, drive.Slot, snapshot))
        {
            throw new InvalidOperationException($"Drive {drive.Label} is busy.");
        }

        handle.Io.Session.MountDisk(drive.Slot, drive.Drive, path, writeProtect);
        handle.Io.RememberPath(drive, path);
    }

    private void EjectCore(MachineHandle handle, DriveId drive)
    {
        RejectFixedTarget(handle, drive);
        DiskDriveSnapshot snapshot = RequireRemovable(handle, drive);
        if (IsBusy(handle, drive.Slot, snapshot))
        {
            throw new InvalidOperationException($"Drive {drive.Label} is busy.");
        }

        if (!snapshot.HasMedia)
        {
            throw new InvalidOperationException($"Drive {drive.Label} is empty.");
        }

        handle.Io.Session.EjectDisk(drive.Slot, drive.Drive);
        handle.Io.ForgetPath(drive);
    }

    private void WriteProtectCore(MachineHandle handle, DriveId drive, bool writeProtect)
    {
        lock (handle.Sync)
        {
            FixedVolume? unit = handle.FindFixed(drive);
            if (unit is not null)
            {
                if (handle.ReportedState != HostRunState.Off)
                {
                    throw new InvalidOperationException("Power the machine off to change fixed storage.");
                }

                unit.WriteProtected = writeProtect;
                return;
            }
        }

        DiskDriveSnapshot snapshot = RequireRemovable(handle, drive);
        if (IsBusy(handle, drive.Slot, snapshot))
        {
            throw new InvalidOperationException($"Drive {drive.Label} is busy.");
        }

        if (!snapshot.HasMedia)
        {
            throw new InvalidOperationException($"Drive {drive.Label} is empty.");
        }

        string path = handle.Io.TryGetPath(drive, out string? remembered) && remembered is not null
            ? remembered
            : snapshot.MountedImagePath ?? throw new InvalidOperationException($"Drive {drive.Label} is empty.");
        handle.Io.Session.MountDisk(drive.Slot, drive.Drive, path, writeProtect);
        handle.Io.RememberPath(drive, path);
    }

    private void AttachCore(MachineHandle handle, DriveId drive, string path, bool writeProtect)
    {
        if (ResolveFixedLibrary(path, out string? resolved) && !File.Exists(resolved))
        {
            throw new FileNotFoundException($"Fixed storage image '{path}' was not found.", resolved);
        }

        lock (handle.Sync)
        {
            if (handle.ReportedState != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change fixed storage.");
            }
        }

        if (SlotIsRemovable(handle.Machine, drive.Slot))
        {
            throw new InvalidOperationException($"Slot {drive.Slot} is removable media. Fixed storage attaches to its own slot.");
        }

        lock (handle.Sync)
        {
            if (handle.ReportedState != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change fixed storage.");
            }

            FixedVolume? existing = handle.FindFixed(drive);
            if (existing is null)
            {
                handle.FixedVolumes.Add(new FixedVolume(drive, path, writeProtect));
            }
            else
            {
                existing.ImagePath = path;
                existing.WriteProtected = writeProtect;
            }

            handle.RefreshBootDrive();
        }
    }

    private void DetachCore(MachineHandle handle, DriveId drive)
    {
        lock (handle.Sync)
        {
            if (handle.ReportedState != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change fixed storage.");
            }

            FixedVolume? existing = handle.FindFixed(drive);
            if (existing is null)
            {
                throw new InvalidOperationException($"Drive {drive.Label} is not fixed storage.");
            }

            handle.FixedVolumes.Remove(existing);
            handle.RefreshBootDrive();
        }
    }

    private IReadOnlyList<HostDiskController> ReadTopology(MachineHandle handle)
    {
        List<HostDiskController> controllers = ReadRemovable(handle);
        bool removableBoot = false;
        if (handle.StartupSlot is int bootSlot)
        {
            foreach (HostDiskController controller in controllers)
            {
                if (controller.Slot == bootSlot)
                {
                    removableBoot = true;
                    break;
                }
            }
        }

        FixedVolume[] units;
        lock (handle.Sync)
        {
            units = handle.FixedVolumes.ToArray();
        }

        foreach (IGrouping<int, FixedVolume> slot in units.GroupBy(unit => unit.Id.Slot).OrderBy(group => group.Key))
        {
            FixedVolume[] ordered = slot.OrderBy(unit => unit.Id.Drive).ToArray();
            HostDrive[] drives = new HostDrive[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                FixedVolume unit = ordered[index];
                bool boot = !removableBoot && handle.StartupSlot == unit.Id.Slot && unit.Id.Drive == 1;
                drives[index] = new HostDrive(
                    unit.Id,
                    unit.Id.Label,
                    unit.ImagePath,
                    Path.GetFileName(unit.ImagePath),
                    unit.WriteProtected,
                    Dirty: false,
                    Busy: false,
                    Active: false,
                    boot,
                    Error: null,
                    Track: null,
                    Block: null,
                    HostStorageCategory.FixedStorage);
            }

            controllers.Add(new HostDiskController(slot.Key, "Hard Disk", drives, HostStorageCategory.FixedStorage));
        }

        controllers.Sort(static (left, right) =>
        {
            int slot = left.Slot.CompareTo(right.Slot);
            return slot != 0 ? slot : left.Category.CompareTo(right.Category);
        });
        return controllers;
    }

    private List<HostDiskController> ReadRemovable(MachineHandle handle)
    {
        ISlotManager? slots = handle.Machine.GetComponent<ISlotManager>();
        List<HostDiskController> controllers = [];
        if (slots is null)
        {
            return controllers;
        }

        for (int slot = 1; slot <= 7; slot++)
        {
            if (slots.GetCard(slot) is not IDiskController controller)
            {
                continue;
            }

            bool controllerBusy = false;
            DiskDriveSnapshot[] snapshots = new DiskDriveSnapshot[controller.DriveCount];
            string?[] errors = new string?[controller.DriveCount];
            bool[] dirty = new bool[controller.DriveCount];
            for (int index = 0; index < controller.DriveCount; index++)
            {
                try
                {
                    DriveSnapshot live = controller.GetDriveSnapshot(index);
                    dirty[index] = live.UnflushedChanges;
                    snapshots[index] = ToDebugSnapshot(index + 1, live);
                    if (live.MotorOn)
                    {
                        controllerBusy = true;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    errors[index] = ex.Message;
                    snapshots[index] = EmptySnapshot(index + 1);
                }
            }

            HostDrive[] drives = new HostDrive[controller.DriveCount];
            for (int index = 0; index < controller.DriveCount; index++)
            {
                DiskDriveSnapshot snapshot = snapshots[index];
                string? imagePath = snapshot.MountedImagePath;
                bool boot = handle.StartupSlot == slot && snapshot.Drive == 1 && errors[index] is null;
                drives[index] = new HostDrive(
                    new DriveId(slot, snapshot.Drive),
                    new DriveId(slot, snapshot.Drive).Label,
                    imagePath,
                    imagePath is null ? null : Path.GetFileName(imagePath),
                    snapshot.WriteProtect,
                    dirty[index],
                    controllerBusy || snapshot.MotorOffPending,
                    snapshot.MotorOn,
                    boot,
                    errors[index],
                    errors[index] is null ? snapshot.QuarterTrack : null,
                    Block: null,
                    HostStorageCategory.RemovableMedia);
            }

            string name = slots.GetCard(slot)?.Name ?? "Disk";
            controllers.Add(new HostDiskController(slot, name, drives, HostStorageCategory.RemovableMedia));
        }

        return controllers;
    }

    private IReadOnlyList<HostSlotInfo> ReadSlots(IMachine machine)
    {
        HostSlotInfo[] rows = new HostSlotInfo[8];
        rows[0] = new HostSlotInfo("aux", "Aux", AuxCardName(machine), true);
        ISlotManager? slots = machine.GetComponent<ISlotManager>();
        for (int slot = 1; slot <= 7; slot++)
        {
            ISlotCard? card = slots?.GetCard(slot);
            rows[slot] = new HostSlotInfo(
                slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "S" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture),
                card?.Name,
                true);
        }

        return rows;
    }

    private string? AuxCardName(IMachine machine)
    {
        foreach (IMotherboardDevice device in machine.GetComponents<IMotherboardDevice>())
        {
            if (string.Equals(device.DeviceType, "Extended80Column", StringComparison.OrdinalIgnoreCase))
            {
                return device.Name;
            }
        }

        return null;
    }

    private DiskDriveSnapshot RequireRemovable(MachineHandle handle, DriveId drive)
    {
        ISlotManager slots = handle.Machine.GetComponent<ISlotManager>()
            ?? throw new InvalidOperationException($"No drive {drive.Label} is installed.");
        if (slots.GetCard(drive.Slot) is not IDiskController controller)
        {
            throw new InvalidOperationException($"No drive {drive.Label} is installed.");
        }

        if (drive.Drive < 1 || drive.Drive > controller.DriveCount)
        {
            throw new InvalidOperationException($"No drive {drive.Label} is installed.");
        }

        try
        {
            return ToDebugSnapshot(drive.Drive, controller.GetDriveSnapshot(drive.Drive - 1));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidOperationException($"Drive {drive.Label} could not be read: {ex.Message}", ex);
        }
    }

    private bool IsBusy(MachineHandle handle, int slot, DiskDriveSnapshot snapshot)
    {
        if (snapshot.MotorOn || snapshot.MotorOffPending)
        {
            return true;
        }

        ISlotManager? slots = handle.Machine.GetComponent<ISlotManager>();
        if (slots?.GetCard(slot) is not IDiskController controller)
        {
            return false;
        }

        for (int index = 0; index < controller.DriveCount; index++)
        {
            try
            {
                if (controller.GetDriveSnapshot(index).MotorOn)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                continue;
            }
        }

        return false;
    }

    private bool SlotIsRemovable(IMachine machine, int slot)
    {
        ISlotManager? slots = machine.GetComponent<ISlotManager>();
        return slots?.GetCard(slot) is IDiskController;
    }

    private void RejectFixedTarget(MachineHandle handle, DriveId drive)
    {
        lock (handle.Sync)
        {
            if (handle.FindFixed(drive) is not null)
            {
                throw new InvalidOperationException($"Drive {drive.Label} is fixed storage. Attach and detach it while the machine is off.");
            }
        }
    }

    private void RequireFixedAddress(DriveId drive)
    {
        if (drive.Slot is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(drive), drive.Slot, "Fixed storage uses slot 1 through 7.");
        }

        if (drive.Drive is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(drive), drive.Drive, "Fixed storage uses unit 1 through 8.");
        }
    }

    private DiskDriveSnapshot ToDebugSnapshot(int drive, DriveSnapshot snapshot)
    {
        return new DiskDriveSnapshot(
            drive,
            snapshot.Selected,
            snapshot.MotorOn,
            snapshot.PhaseLatch,
            snapshot.QuarterTrack,
            snapshot.WriteProtect,
            snapshot.HasMedia,
            snapshot.MountedImagePath,
            Geometry: null,
            MotorOffPending: snapshot.MotorOffPending);
    }

    private DiskDriveSnapshot EmptySnapshot(int drive)
    {
        return new DiskDriveSnapshot(drive, false, false, 0, 0, false, false, null, null);
    }

    private bool ResolveFixedLibrary(string path, out string? resolved)
    {
        resolved = null;
        if (!path.StartsWith("library://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolved = new ProfilePathResolver(libraryRoot).Resolve(path);
        return true;
    }

    private sealed class HostDiskBinding : IDisposable
    {
        private readonly DebugContext context;
        private readonly Dictionary<(int Slot, int Drive), string> clientPaths = [];

        internal HostDiskBinding(IMachine machine, string libraryRoot)
        {
            context = new DebugContext(new CommandDispatcher(), TextWriter.Null, TextWriter.Null);
            context.AttachPathResolver(new DebugPathResolver(libraryRoot));
            context.AttachDiskImageFactory(new DiskImageFactory());
            context.AttachMachine(machine);
            Session = new MachineDebugSession(context);
        }

        internal IMachineIoSession Session { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            context.Dispose();
        }

        internal void RememberPath(DriveId drive, string path)
        {
            clientPaths[(drive.Slot, drive.Drive)] = path;
        }

        internal bool TryGetPath(DriveId drive, out string? path)
        {
            return clientPaths.TryGetValue((drive.Slot, drive.Drive), out path);
        }

        internal void ForgetPath(DriveId drive)
        {
            clientPaths.Remove((drive.Slot, drive.Drive));
        }

        internal void Retarget(IMachine machine)
        {
            clientPaths.Clear();
            context.DetachSystem();
            context.AttachMachine(machine);
        }
    }

    private sealed class FixedVolume
    {
        internal FixedVolume(DriveId id, string imagePath, bool writeProtected)
        {
            Id = id;
            ImagePath = imagePath;
            WriteProtected = writeProtected;
        }

        internal DriveId Id { get; }

        internal string ImagePath { get; set; }

        internal bool WriteProtected { get; set; }
    }
}