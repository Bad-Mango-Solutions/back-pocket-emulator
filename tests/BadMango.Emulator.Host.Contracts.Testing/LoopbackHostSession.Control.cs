// <copyright file="LoopbackHostSession.Control.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using BadMango.Emulator.Host.Contracts;

/// <content>
/// Control-plane members of <see cref="LoopbackHostSession"/>.
/// </content>
public sealed partial class LoopbackHostSession
{
    /// <inheritdoc />
    public MachineId CreateInstance(HostInstanceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.ProfileId);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.NominalClockHz);
        if (!Enum.IsDefined(spec.OutputKind))
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.OutputKind, "The output kind is not defined.");
        }

        LoopbackMachine machine = new(MachineId.New(), spec);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!profiles.ContainsKey(spec.ProfileId))
            {
                throw new InvalidOperationException($"No profile '{spec.ProfileId}' is stored on this host.");
            }

            machines.Add(machine);
            machinesById.Add(machine.Id.Value, machine);
        }

        Notify(sink => sink.OnInstanceAdded(machine.Id));
        return machine.Id;
    }

    /// <inheritdoc />
    public void RemoveInstance(MachineId machineId)
    {
        LoopbackMachine machine = Find(machineId);
        lock (tableLock)
        {
            ThrowIfDisposed();
            lock (machine.Gate)
            {
                if (machine.Removed)
                {
                    throw Unknown(machineId);
                }

                if (machine.State != HostRunState.Off)
                {
                    throw new InvalidOperationException("Power the machine off before removing it.");
                }

                machines.Remove(machine);
                machinesById.Remove(machine.Id.Value);
                machine.Removed = true;
                machine.Subscriptions.Clear();
            }
        }

        Notify(sink => sink.OnInstanceRemoved(machine.Id));
    }

    /// <inheritdoc />
    public IReadOnlyList<HostMachineInfo> ListInstances()
    {
        LoopbackMachine[] snapshot;
        lock (tableLock)
        {
            ThrowIfDisposed();
            snapshot = machines.ToArray();
        }

        HostMachineInfo[] infos = new HostMachineInfo[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            infos[i] = ReadInfo(snapshot[i]);
        }

        return infos;
    }

    /// <inheritdoc />
    public HostMachineInfo GetMachineInfo(MachineId machineId)
    {
        return ReadInfo(Find(machineId));
    }

    /// <inheritdoc />
    public void UpsertProfile(HostProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Json);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!profiles.ContainsKey(profile.Id))
            {
                profileOrder.Add(profile.Id);
            }

            profiles[profile.Id] = profile;
        }
    }

    /// <inheritdoc />
    public HostProfileDocument GetProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!profiles.TryGetValue(profileId, out HostProfileDocument? profile))
            {
                throw new InvalidOperationException($"No profile '{profileId}' is stored on this host.");
            }

            return profile;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HostProfileSummary> ListProfiles()
    {
        lock (tableLock)
        {
            ThrowIfDisposed();
            HostProfileSummary[] summaries = new HostProfileSummary[profileOrder.Count];
            for (int i = 0; i < profileOrder.Count; i++)
            {
                HostProfileDocument profile = profiles[profileOrder[i]];
                summaries[i] = new HostProfileSummary(profile.Id, profile.Name);
            }

            return summaries;
        }
    }

    /// <inheritdoc />
    public void SetInstanceOverlay(MachineId machineId, string? overlayJson)
    {
        string? stored = NormalizeOverlay(overlayJson);
        Update(machineId, machine =>
        {
            if (machine.State != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change the profile.");
            }

            machine.OverlayJson = stored;
        });
    }

    /// <inheritdoc />
    public Task PowerOnAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            HostStateNotice? notice = null;
            Update(machineId, machine =>
            {
                if (machine.State != HostRunState.Off)
                {
                    throw new InvalidOperationException($"Cannot power on while {machine.State}.");
                }

                machine.State = machine.StartPaused ? HostRunState.Paused : HostRunState.Running;
                notice = new HostStateNotice(machine.Id, machine.State);
            });

            if (notice is not null)
            {
                Notify(sink => sink.OnStateChanged(notice));
            }
        });
    }

    /// <inheritdoc />
    public Task PauseAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () => Transition(machineId, "pause", HostRunState.Running, HostRunState.Paused));
    }

    /// <inheritdoc />
    public Task ResumeAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            HostStateNotice? notice = null;
            Update(machineId, machine =>
            {
                if (machine.State is not (HostRunState.Paused or HostRunState.Sleeping or HostRunState.Stopped))
                {
                    throw new InvalidOperationException($"Cannot resume while {machine.State}.");
                }

                machine.State = HostRunState.Running;
                notice = new HostStateNotice(machine.Id, machine.State);
            });

            if (notice is not null)
            {
                Notify(sink => sink.OnStateChanged(notice));
            }
        });
    }

    /// <inheritdoc />
    public Task SleepAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            HostStateNotice? notice = null;
            Update(machineId, machine =>
            {
                if (machine.Power?.Sleep != true)
                {
                    throw new InvalidOperationException("This machine cannot sleep.");
                }

                if (machine.State != HostRunState.Running)
                {
                    throw new InvalidOperationException($"Cannot sleep while {machine.State}.");
                }

                machine.State = HostRunState.Sleeping;
                notice = new HostStateNotice(machine.Id, machine.State);
            });

            if (notice is not null)
            {
                Notify(sink => sink.OnStateChanged(notice));
            }
        });
    }

    /// <inheritdoc />
    public Task ResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () => Reset(machineId, hard: false));
    }

    /// <inheritdoc />
    public Task HardResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () => Reset(machineId, hard: true));
    }

    /// <inheritdoc />
    public Task ShutDownAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            HostStateNotice? shutting = null;
            Update(machineId, machine =>
            {
                if (machine.Power?.SoftPower != true)
                {
                    throw new InvalidOperationException("Shut Down is only available on a soft-power machine.");
                }

                RequireOn(machine, "shut down");
                machine.State = HostRunState.ShuttingDown;
                shutting = new HostStateNotice(machine.Id, HostRunState.ShuttingDown);
            });

            if (shutting is not null)
            {
                Notify(sink => sink.OnStateChanged(shutting));
            }

            HostStateNotice? off = null;
            Update(machineId, machine =>
            {
                machine.State = HostRunState.Off;
                off = new HostStateNotice(machine.Id, HostRunState.Off);
            });

            if (off is not null)
            {
                Notify(sink => sink.OnStateChanged(off));
            }
        });
    }

    /// <inheritdoc />
    public Task PowerOffAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            HostStateNotice? notice = null;
            Update(machineId, machine =>
            {
                if (machine.Power?.SoftPower == true)
                {
                    throw new InvalidOperationException("Power Off is not available on a soft-power machine. Shut Down instead.");
                }

                RequireOn(machine, "power off");
                machine.State = HostRunState.Off;
                notice = new HostStateNotice(machine.Id, HostRunState.Off);
            });

            if (notice is not null)
            {
                Notify(sink => sink.OnStateChanged(notice));
            }
        });
    }

    /// <inheritdoc />
    public Task SetThrottleAsync(MachineId machineId, HostThrottle throttle, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            if (!Enum.IsDefined(throttle))
            {
                throw new ArgumentOutOfRangeException(nameof(throttle), throttle, "The throttle is not defined.");
            }

            Update(machineId, machine => machine.Throttle = throttle);
        });
    }

    /// <inheritdoc />
    public Task MountDiskAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            HostLibraryPaths.RequireRemovableMedia(path);
            bool changed = false;
            Update(machineId, machine =>
            {
                LoopbackDrive target = RequireRemovable(machine, drive);
                if (target.Busy)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is busy.");
                }

                target.ImagePath = path;
                target.ImageName = Path.GetFileName(path);
                target.WriteProtected = writeProtect;
                target.Dirty = false;
                target.Active = false;
                target.Error = null;
                target.Track = null;
                target.Block = null;
                changed = true;
            });

            if (changed)
            {
                Notify(sink => sink.OnDisksChanged(machineId));
            }
        });
    }

    /// <inheritdoc />
    public Task EjectDiskAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            bool changed = false;
            Update(machineId, machine =>
            {
                LoopbackDrive target = RequireRemovable(machine, drive);
                if (target.Busy)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is busy.");
                }

                if (target.ImagePath is null)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is empty.");
                }

                target.ImagePath = null;
                target.ImageName = null;
                target.WriteProtected = false;
                target.Dirty = false;
                target.Active = false;
                target.Error = null;
                target.Track = null;
                target.Block = null;
                changed = true;
            });

            if (changed)
            {
                Notify(sink => sink.OnDisksChanged(machineId));
            }
        });
    }

    /// <inheritdoc />
    public Task SetWriteProtectAsync(MachineId machineId, DriveId drive, bool writeProtect, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            bool changed = false;
            Update(machineId, machine =>
            {
                LoopbackDrive? fixedUnit = machine.FindFixed(drive);
                if (fixedUnit is not null)
                {
                    RequireOffForFixed(machine);
                    fixedUnit.WriteProtected = writeProtect;
                    changed = true;
                    return;
                }

                LoopbackDrive target = RequireRemovable(machine, drive);
                if (target.Busy)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is busy.");
                }

                if (target.ImagePath is null)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is empty.");
                }

                target.WriteProtected = writeProtect;
                changed = true;
            });

            if (changed)
            {
                Notify(sink => sink.OnDisksChanged(machineId));
            }
        });
    }

    /// <inheritdoc />
    public Task AttachFixedStorageAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            HostLibraryPaths.RequireFixedStorage(path);
            RequireFixedAddress(drive);
            bool changed = false;
            Update(machineId, machine =>
            {
                RequireOffForFixed(machine);
                if (machine.FindRemovable(drive) is not null)
                {
                    throw new InvalidOperationException($"Slot {drive.Slot} is removable media. Fixed storage attaches to its own slot.");
                }

                LoopbackDrive? existing = machine.FindFixed(drive);
                if (existing is null)
                {
                    existing = new LoopbackDrive(drive, boot: false, HostStorageCategory.FixedStorage);
                    machine.FixedDrives.Add(existing);
                }

                existing.ImagePath = path;
                existing.ImageName = Path.GetFileName(path);
                existing.WriteProtected = writeProtect;
                existing.Dirty = false;
                existing.Error = null;
                changed = true;
            });

            if (changed)
            {
                Notify(sink => sink.OnDisksChanged(machineId));
            }
        });
    }

    /// <inheritdoc />
    public Task DetachFixedStorageAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () =>
        {
            bool changed = false;
            Update(machineId, machine =>
            {
                RequireOffForFixed(machine);
                LoopbackDrive? existing = machine.FindFixed(drive);
                if (existing is null)
                {
                    throw new InvalidOperationException($"Drive {drive.Label} is not fixed storage.");
                }

                machine.FixedDrives.Remove(existing);
                changed = true;
            });

            if (changed)
            {
                Notify(sink => sink.OnDisksChanged(machineId));
            }
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<HostDiskController> ListDisks(MachineId machineId)
    {
        HostDiskController[] controllers = [];
        Update(machineId, machine => controllers = ToControllers(machine));
        return controllers;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostSlotInfo> ListSlots(MachineId machineId)
    {
        Find(machineId);
        HostSlotInfo[] slots = new HostSlotInfo[8];
        slots[0] = new HostSlotInfo("aux", "Aux", null, true);
        for (int slot = 1; slot <= 7; slot++)
        {
            string? card = slot == 6 ? "Disk II" : null;
            slots[slot] = new HostSlotInfo(slot.ToString(CultureInfo.InvariantCulture), $"S{slot}", card, true);
        }

        return slots;
    }

    /// <inheritdoc />
    public Task AttachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () => SetDebugger(machineId, attached: true));
    }

    /// <inheritdoc />
    public Task DetachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        return Run(cancellationToken, () => SetDebugger(machineId, attached: false));
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IHostNotificationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (tableLock)
        {
            ThrowIfDisposed();
            sinks.Add(sink);
        }

        return new SinkSubscription(this, sink);
    }

    /// <inheritdoc />
    public void ReportMeasuredSpeed(MachineId machineId, double ratio)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ratio), ratio, "Measured speed must be a finite, non-negative ratio.");
        }

        Update(machineId, machine => machine.MeasuredSpeed = ratio);
    }

    /// <inheritdoc />
    public void SetDriveStatus(MachineId machineId, DriveId drive, HostDriveStatus status)
    {
        bool changed = false;
        Update(machineId, machine =>
        {
            LoopbackDrive target = machine.FindDrive(drive) ?? throw new InvalidOperationException($"No drive {drive.Label} is installed.");
            target.Active = status.Active;
            target.Dirty = status.Dirty;
            target.Busy = status.Busy;
            target.Track = status.Track;
            target.Block = status.Block;
            target.Error = status.Error;
            changed = true;
        });

        if (changed)
        {
            Notify(sink => sink.OnDisksChanged(machineId));
        }
    }

    private static void RequireOn(LoopbackMachine machine, string verb)
    {
        if (machine.State is HostRunState.Off or HostRunState.ShuttingDown)
        {
            throw new InvalidOperationException($"Cannot {verb} while {machine.State}.");
        }
    }

    private static void RequireOffForFixed(LoopbackMachine machine)
    {
        if (machine.State != HostRunState.Off)
        {
            throw new InvalidOperationException("Power the machine off to change fixed storage.");
        }
    }

    private static void RequireFixedAddress(DriveId drive)
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

    private static LoopbackDrive RequireRemovable(LoopbackMachine machine, DriveId drive)
    {
        if (machine.FindFixed(drive) is not null)
        {
            throw new InvalidOperationException($"Drive {drive.Label} is fixed storage. Attach and detach it while the machine is off.");
        }

        LoopbackDrive? target = machine.FindRemovable(drive);
        if (target is null)
        {
            throw new InvalidOperationException($"No drive {drive.Label} is installed.");
        }

        return target;
    }

    private static HostDiskController[] ToControllers(LoopbackMachine machine)
    {
        HostDrive[] floppy = new HostDrive[machine.Drives.Count];
        for (int i = 0; i < floppy.Length; i++)
        {
            floppy[i] = machine.Drives[i].ToDrive();
        }

        var controllers = new List<HostDiskController>
        {
            new(6, "Disk II", floppy, HostStorageCategory.RemovableMedia),
        };

        foreach (IGrouping<int, LoopbackDrive> slot in machine.FixedDrives.GroupBy(drive => drive.Id.Slot).OrderBy(group => group.Key))
        {
            HostDrive[] units = slot.OrderBy(drive => drive.Id.Drive).Select(drive => drive.ToDrive()).ToArray();
            controllers.Add(new HostDiskController(slot.Key, "Hard Disk", units, HostStorageCategory.FixedStorage));
        }

        return controllers.ToArray();
    }

    private static long EffectiveClock(long nominal, HostThrottle throttle)
    {
        return throttle switch
        {
            HostThrottle.Half => nominal / 2,
            HostThrottle.Normal => nominal,
            HostThrottle.Double => nominal > (long.MaxValue / 2)
                ? throw new ArgumentOutOfRangeException(nameof(nominal), nominal, "Twice the nominal clock overflows.")
                : nominal * 2,
            HostThrottle.Max => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(throttle), throttle, "The throttle is not defined."),
        };
    }

    private string? NormalizeOverlay(string? overlayJson)
    {
        if (string.IsNullOrWhiteSpace(overlayJson))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(overlayJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("The profile overlay must be a JSON object.", nameof(overlayJson), ex);
        }

        if (node is not JsonObject)
        {
            throw new ArgumentException("The profile overlay must be a JSON object.", nameof(overlayJson));
        }

        return overlayJson;
    }

    private void Reset(MachineId machineId, bool hard)
    {
        HostResetNotice? notice = null;
        HostStateNotice? resumed = null;
        Update(machineId, machine =>
        {
            RequireOn(machine, hard ? "hard reset" : "reset");
            if (hard && machine.Power is not null && !machine.Power.HardReset)
            {
                throw new InvalidOperationException("Hard Reset is not available on this machine.");
            }

            if (machine.State == HostRunState.Stopped)
            {
                machine.State = HostRunState.Running;
                resumed = new HostStateNotice(machine.Id, HostRunState.Running);
            }

            notice = new HostResetNotice(machine.Id, hard);
        });

        if (notice is not null)
        {
            Notify(sink => sink.OnReset(notice));
        }

        if (resumed is not null)
        {
            Notify(sink => sink.OnStateChanged(resumed));
        }
    }

    private void Transition(MachineId machineId, string verb, HostRunState from, HostRunState to)
    {
        HostStateNotice? notice = null;
        Update(machineId, machine =>
        {
            if (machine.State != from)
            {
                throw new InvalidOperationException($"Cannot {verb} while {machine.State}.");
            }

            machine.State = to;
            notice = new HostStateNotice(machine.Id, to);
        });

        if (notice is not null)
        {
            Notify(sink => sink.OnStateChanged(notice));
        }
    }

    private void SetDebugger(MachineId machineId, bool attached)
    {
        HostDebuggerNotice? notice = null;
        Update(machineId, machine =>
        {
            if (machine.DebuggerAttached == attached)
            {
                return;
            }

            machine.DebuggerAttached = attached;
            notice = new HostDebuggerNotice(machine.Id, attached);
        });

        if (notice is not null)
        {
            Notify(sink => sink.OnDebuggerAttachmentChanged(notice));
        }
    }

    private HostMachineInfo ReadInfo(LoopbackMachine machine)
    {
        lock (machine.Gate)
        {
            EnsureLive(machine);
            return new HostMachineInfo(
                machine.Id,
                machine.Name,
                machine.State,
                machine.Power,
                machine.NominalClockHz,
                machine.Throttle,
                EffectiveClock(machine.NominalClockHz, machine.Throttle),
                machine.MeasuredSpeed,
                machine.ProfileId,
                machine.OverlayJson,
                machine.DebuggerAttached,
                machine.CpuSummary,
                machine.SchedulerSummary,
                machine.BootDriveId,
                machine.OutputKind,
                machine.StartPaused);
        }
    }
}