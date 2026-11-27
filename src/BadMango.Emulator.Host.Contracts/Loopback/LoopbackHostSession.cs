// <copyright file="LoopbackHostSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Diagnostics;
using System.Globalization;

/// <summary>
/// In-process host. Client threads call the planes. The machine gate is taken once per call, at this edge, and never from a CPU loop.
/// </summary>
public sealed class LoopbackHostSession : IHostSession, IHostControl, IHostPresentation, IHostInput, IHostDebugger, ILoopbackMachineEdge
{
    private readonly Lock tableLock = new();
    private readonly List<LoopbackMachine> machines = [];
    private readonly Dictionary<string, LoopbackMachine> machinesById = [];
    private readonly Dictionary<string, HostProfileDocument> profiles = [];
    private readonly List<string> profileOrder = [];
    private readonly List<IHostNotificationSink> sinks = [];
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackHostSession"/> class.
    /// </summary>
    internal LoopbackHostSession()
    {
    }

    /// <inheritdoc />
    public int ApiVersion => HostApi.Version;

    /// <inheritdoc />
    public HostTransportInfo Transport { get; } = new(
        HostTransportKind.Loopback,
        "loopback",
        HostApi.Version,
        true,
        "Loopback · healthy");

    /// <inheritdoc />
    public IHostControl Control => this;

    /// <inheritdoc />
    public IHostPresentation Presentation => this;

    /// <inheritdoc />
    public IHostInput Input => this;

    /// <inheritdoc />
    public IHostDebugger Debugger => this;

    /// <inheritdoc />
    public void Dispose()
    {
        LoopbackMachine[] snapshot;
        lock (tableLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            snapshot = machines.ToArray();
            machines.Clear();
            machinesById.Clear();
            sinks.Clear();
        }

        foreach (LoopbackMachine machine in snapshot)
        {
            lock (machine.Gate)
            {
                machine.Removed = true;
                machine.Subscriptions.Clear();
            }
        }
    }

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
        string? stored = string.IsNullOrWhiteSpace(overlayJson) ? null : overlayJson;
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
                if (machine.State is not (HostRunState.Paused or HostRunState.Sleeping))
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
            bool changed = false;
            Update(machineId, machine =>
            {
                LoopbackDrive target = RequireDrive(machine, drive);
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
                LoopbackDrive target = RequireDrive(machine, drive);
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
                LoopbackDrive target = RequireDrive(machine, drive);
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
    public IReadOnlyList<HostDiskController> ListDisks(MachineId machineId)
    {
        HostDiskController[] controllers = [];
        Update(machineId, machine => controllers = [ToController(machine)]);
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
    public IPresentationSubscription Subscribe(MachineId machineId, string windowId, HostMonitorChoice? monitor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        LoopbackMachine machine = Find(machineId);
        HostMonitorChoice choice = monitor ?? DefaultMonitor(machine.OutputKind);
        if (!MonitorFits(machine.OutputKind, choice))
        {
            throw new InvalidOperationException($"Monitor {choice} does not match a {machine.OutputKind} output.");
        }

        lock (machine.Gate)
        {
            EnsureLive(machine);
            if (machine.Subscriptions.Exists(subscription => subscription.WindowId == windowId))
            {
                throw new ArgumentException($"Window '{windowId}' is already subscribed.", nameof(windowId));
            }

            LoopbackSubscription subscription = new(this, machine, windowId, choice);
            machine.Subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <inheritdoc />
    public HostScreenText GetScreenText(MachineId machineId)
    {
        HostScreenText screen = new(40, 24, string.Empty, false, false, false);
        Update(machineId, machine => screen = machine.Screen);
        return screen;
    }

    /// <inheritdoc />
    public HostVideoState GetVideoState(MachineId machineId)
    {
        HostVideoState state = new(HostVideoKind.Composite, true, false, false, false, false, false);
        Update(machineId, machine => state = machine.Video);
        return state;
    }

    /// <inheritdoc />
    public void KeyDown(MachineId machineId, HostKey key, HostKeyModifiers modifiers)
    {
        RequireKey(key);
        Update(machineId, machine => ApplyKey(machine, key, modifiers, down: true));
    }

    /// <inheritdoc />
    public void KeyUp(MachineId machineId, HostKey key, HostKeyModifiers modifiers)
    {
        RequireKey(key);
        Update(machineId, machine => ApplyKey(machine, key, modifiers, down: false));
    }

    /// <inheritdoc />
    public HostTextReceipt InjectText(MachineId machineId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        HostTextReceipt receipt = default;
        Update(machineId, machine =>
        {
            int accepted = 0;
            int skipped = 0;
            foreach (char character in text)
            {
                if (!TryMapChar(character, out HostKey key, out HostKeyModifiers modifiers))
                {
                    skipped++;
                    continue;
                }

                ApplyKey(machine, key, modifiers, down: true);
                ApplyKey(machine, key, modifiers, down: false);
                accepted++;
            }

            receipt = new HostTextReceipt(accepted, skipped);
            machine.LastText = receipt;
        });

        return receipt;
    }

    /// <inheritdoc />
    public void SetJoystick(MachineId machineId, int index, float x, float y)
    {
        if (index is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Joystick index must be 0 or 1.");
        }

        float clampedX = ClampAxis(x, nameof(x));
        float clampedY = ClampAxis(y, nameof(y));
        Update(machineId, machine =>
        {
            machine.JoystickX[index] = clampedX;
            machine.JoystickY[index] = clampedY;
            machine.Paddles[index * 2] = ToPaddle(clampedX);
            machine.Paddles[(index * 2) + 1] = ToPaddle(clampedY);
            AddLog(machine, Entry(HostInputKind.Joystick, index: index, x: clampedX, y: clampedY));
        });
    }

    /// <inheritdoc />
    public void SetPaddle(MachineId machineId, int paddle, byte position)
    {
        if (paddle is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(paddle), paddle, "Paddle index must be 0 through 3.");
        }

        Update(machineId, machine =>
        {
            machine.Paddles[paddle] = position;
            AddLog(machine, Entry(HostInputKind.Paddle, index: paddle, x: position));
        });
    }

    /// <inheritdoc />
    public void SetButton(MachineId machineId, int button, bool pressed)
    {
        if (button is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "Button index must be 0 through 3.");
        }

        Update(machineId, machine =>
        {
            machine.Buttons[button] = pressed;
            AddLog(machine, Entry(HostInputKind.GameButton, index: button, down: pressed));
        });
    }

    /// <inheritdoc />
    public void MoveMouse(MachineId machineId, HostMouseMotionKind kind, float x, float y)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The motion kind is not defined.");
        }

        float finiteX = RequireFinite(x, nameof(x));
        float finiteY = RequireFinite(y, nameof(y));
        if (kind == HostMouseMotionKind.Absolute)
        {
            finiteX = Math.Clamp(finiteX, 0f, 1f);
            finiteY = Math.Clamp(finiteY, 0f, 1f);
        }

        float storedX = finiteX;
        float storedY = finiteY;
        Update(machineId, machine =>
        {
            machine.MouseKind = kind;
            machine.MouseX = storedX;
            machine.MouseY = storedY;
            AddLog(machine, Entry(HostInputKind.MouseMotion, x: storedX, y: storedY, motion: kind));
        });
    }

    /// <inheritdoc />
    public void SetMouseButton(MachineId machineId, HostMouseButton button, bool pressed)
    {
        if (!Enum.IsDefined(button))
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button is not defined.");
        }

        Update(machineId, machine =>
        {
            switch (button)
            {
                case HostMouseButton.Left:
                    machine.MouseLeft = pressed;
                    break;
                case HostMouseButton.Middle:
                    machine.MouseMiddle = pressed;
                    break;
                case HostMouseButton.Right:
                    machine.MouseRight = pressed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button is not defined.");
            }

            AddLog(machine, Entry(HostInputKind.MouseButton, down: pressed, mouseButton: button));
        });
    }

    /// <inheritdoc />
    public void CapturePointer(MachineId machineId, string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        Update(machineId, machine =>
        {
            machine.CaptureWindowId = windowId;
            AddLog(machine, Entry(HostInputKind.PointerCapture, down: true, windowId: windowId));
        });
    }

    /// <inheritdoc />
    public void ReleasePointer(MachineId machineId, string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        Update(machineId, machine =>
        {
            if (machine.CaptureWindowId != windowId)
            {
                return;
            }

            machine.CaptureWindowId = null;
            AddLog(machine, Entry(HostInputKind.PointerCapture, down: false, windowId: windowId));
        });
    }

    /// <inheritdoc />
    public HostInputState GetInputState(MachineId machineId)
    {
        HostInputState state = new([], HostKeyModifiers.None, new HostGamePortState(0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false), new HostMouseState(HostMouseMotionKind.Relative, 0, 0, false, false, false, null), default);
        Update(machineId, machine => state = ToInputState(machine));
        return state;
    }

    /// <inheritdoc />
    public bool IsAttached(MachineId machineId)
    {
        bool attached = false;
        Update(machineId, machine => attached = machine.DebuggerAttached);
        return attached;
    }

    /// <inheritdoc />
    public HostMemoryBlock ReadMemory(MachineId machineId, uint address, int length)
    {
        byte[] copy = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, length);
            copy = machine.Memory.AsSpan((int)address, length).ToArray();
        });

        return new HostMemoryBlock(address, copy);
    }

    /// <inheritdoc />
    public void WriteMemory(MachineId machineId, uint address, ReadOnlyMemory<byte> data)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, data.Length);
            data.Span.CopyTo(machine.Memory.AsSpan((int)address, data.Length));
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<HostDisassemblyLine> Disassemble(MachineId machineId, uint address, int count)
    {
        if (count is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Disassembly count must be 1 through 256.");
        }

        HostDisassemblyLine[] lines = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            RequireRange(address, count);
            lines = new HostDisassemblyLine[count];
            for (int i = 0; i < count; i++)
            {
                uint lineAddress = address + (uint)i;
                byte value = machine.Memory[lineAddress];
                string hex = value.ToString("X2", CultureInfo.InvariantCulture);
                lines[i] = new HostDisassemblyLine(lineAddress, 1, hex, "DB", "$" + hex);
            }
        });

        return lines;
    }

    /// <inheritdoc />
    public HostRegisters GetRegisters(MachineId machineId)
    {
        Update(machineId, RequireDebugger);
        return new HostRegisters(0, 0, 0, 0, 0, 0);
    }

    /// <inheritdoc />
    public void AddBreakpoint(MachineId machineId, uint address, string? label = null)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            HostBreakpoint breakpoint = new(address, label);
            int index = machine.Breakpoints.FindIndex(existing => existing.Address == address);
            if (index >= 0)
            {
                machine.Breakpoints[index] = breakpoint;
            }
            else
            {
                machine.Breakpoints.Add(breakpoint);
            }
        });
    }

    /// <inheritdoc />
    public bool RemoveBreakpoint(MachineId machineId, uint address)
    {
        bool removed = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            removed = machine.Breakpoints.RemoveAll(existing => existing.Address == address) > 0;
        });

        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostBreakpoint> GetBreakpoints(MachineId machineId)
    {
        HostBreakpoint[] breakpoints = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            breakpoints = machine.Breakpoints.ToArray();
        });

        return breakpoints;
    }

    /// <inheritdoc />
    public void ClearBreakpoints(MachineId machineId)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            machine.Breakpoints.Clear();
        });
    }

    /// <inheritdoc />
    public void AddWatch(MachineId machineId, uint address, HostWatchAccess access = HostWatchAccess.ReadWrite, bool stopOnHit = false, string? label = null)
    {
        if (!Enum.IsDefined(access))
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "The watch access is not defined.");
        }

        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            HostWatch watch = new(address, access, stopOnHit, label);
            int index = machine.Watches.FindIndex(existing => existing.Address == address);
            if (index >= 0)
            {
                machine.Watches[index] = watch;
            }
            else
            {
                machine.Watches.Add(watch);
            }
        });
    }

    /// <inheritdoc />
    public bool RemoveWatch(MachineId machineId, uint address)
    {
        bool removed = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            removed = machine.Watches.RemoveAll(existing => existing.Address == address) > 0;
        });

        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostWatch> GetWatches(MachineId machineId)
    {
        HostWatch[] watches = [];
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            watches = machine.Watches.ToArray();
        });

        return watches;
    }

    /// <inheritdoc />
    public void ClearWatches(MachineId machineId)
    {
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            machine.Watches.Clear();
        });
    }

    /// <inheritdoc />
    public void PublishFrame(MachineId machineId, HostPresentationSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Update(machineId, machine =>
        {
            ValidateSample(machine, sample);
            long now = Stopwatch.GetTimestamp();
            if (machine.LastFrameTimestamp != 0)
            {
                double seconds = (now - machine.LastFrameTimestamp) / (double)Stopwatch.Frequency;
                if (seconds > 0)
                {
                    machine.FramesPerSecond = 1.0 / seconds;
                }
            }

            machine.LastFrameTimestamp = now;
            machine.LastSequence = sample.Sequence;
            machine.LastCycle = sample.Cycle;
            machine.Latest = sample;
        });
    }

    /// <inheritdoc />
    public void PublishAudio(MachineId machineId, HostAudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Update(machineId, machine =>
        {
            ValidateBlock(machine.AudioFormat, block);
            foreach (LoopbackSubscription subscription in machine.Subscriptions)
            {
                if (!subscription.IsDisposed)
                {
                    subscription.Enqueue(block);
                }
            }
        });
    }

    /// <inheritdoc />
    public void SetAudioFormat(MachineId machineId, HostAudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        ValidateFormat(format);
        Update(machineId, machine =>
        {
            machine.AudioFormat = format;
            foreach (LoopbackSubscription subscription in machine.Subscriptions)
            {
                subscription.ClearAudio();
            }
        });
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
    public void SetScreenText(MachineId machineId, HostScreenText screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(screen.Text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(screen.Columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(screen.Rows);
        Update(machineId, machine => machine.Screen = screen);
    }

    /// <inheritdoc />
    public void SetVideoState(MachineId machineId, HostVideoState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Update(machineId, machine =>
        {
            if (state.Output != machine.OutputKind)
            {
                throw new InvalidOperationException("The video state output does not match the machine.");
            }

            machine.Video = state;
        });
    }

    /// <inheritdoc />
    public void SetDriveStatus(MachineId machineId, DriveId drive, HostDriveStatus status)
    {
        bool changed = false;
        Update(machineId, machine =>
        {
            LoopbackDrive target = RequireDrive(machine, drive);
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

    /// <inheritdoc />
    public void RaiseBreakpointHit(MachineId machineId, uint address)
    {
        bool hit = false;
        Update(machineId, machine =>
        {
            RequireDebugger(machine);
            if (!machine.Breakpoints.Exists(breakpoint => breakpoint.Address == address))
            {
                throw new InvalidOperationException($"No breakpoint is set at ${address:X}.");
            }

            hit = true;
        });

        if (hit)
        {
            Notify(sink => sink.OnBreakpointHit(new HostBreakpointNotice(machineId, address)));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HostInputEntry> CopyInputLog(MachineId machineId)
    {
        HostInputEntry[] copy = [];
        Update(machineId, machine => copy = machine.InputLog.ToArray());
        return copy;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostInputEntry> DrainInput(MachineId machineId)
    {
        HostInputEntry[] copy = [];
        Update(machineId, machine =>
        {
            copy = machine.InputLog.ToArray();
            machine.InputLog.Clear();
        });

        return copy;
    }

    /// <summary>
    /// Sets one window's monitor when the choice matches the output.
    /// </summary>
    /// <param name="subscription">The window.</param>
    /// <param name="monitor">The choice.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="monitor"/> is not defined.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the choice does not match the output.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the subscription is disposed.</exception>
    internal void ChangeMonitor(LoopbackSubscription subscription, HostMonitorChoice monitor)
    {
        if (!Enum.IsDefined(monitor))
        {
            throw new ArgumentOutOfRangeException(nameof(monitor), monitor, "The monitor choice is not defined.");
        }

        lock (subscription.Machine.Gate)
        {
            EnsureSubscription(subscription);
            if (!MonitorFits(subscription.Machine.OutputKind, monitor))
            {
                throw new InvalidOperationException($"Monitor {monitor} does not match a {subscription.Machine.OutputKind} output.");
            }

            subscription.ApplyMonitor(monitor);
        }
    }

    /// <summary>
    /// Reads the latest sample without removing it.
    /// </summary>
    /// <param name="subscription">The window.</param>
    /// <param name="sample">The sample, or <see langword="null"/> when none has been published.</param>
    /// <returns><see langword="true"/> when a sample is available.</returns>
    internal bool ReadLatest(LoopbackSubscription subscription, out HostPresentationSample? sample)
    {
        lock (subscription.Machine.Gate)
        {
            if (subscription.IsDisposed || subscription.Machine.Removed)
            {
                sample = null;
                return false;
            }

            sample = subscription.Machine.Latest;
            return sample is not null;
        }
    }

    /// <summary>
    /// Takes the next audio block for one window.
    /// </summary>
    /// <param name="subscription">The window.</param>
    /// <param name="block">The block, or <see langword="null"/> when the queue is empty.</param>
    /// <returns><see langword="true"/> when a block was waiting.</returns>
    internal bool ReadAudio(LoopbackSubscription subscription, out HostAudioBlock? block)
    {
        lock (subscription.Machine.Gate)
        {
            if (subscription.IsDisposed || subscription.Machine.Removed)
            {
                block = null;
                return false;
            }

            return subscription.TryDequeue(out block);
        }
    }

    /// <summary>
    /// Reads the line-out format.
    /// </summary>
    /// <param name="subscription">The window.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the subscription is disposed.</exception>
    internal HostAudioFormat ReadAudioFormat(LoopbackSubscription subscription)
    {
        lock (subscription.Machine.Gate)
        {
            EnsureSubscription(subscription);
            return subscription.Machine.AudioFormat;
        }
    }

    /// <summary>
    /// Reads the frame timing.
    /// </summary>
    /// <param name="subscription">The window.</param>
    /// <returns>The timing. The default when the subscription is disposed.</returns>
    internal HostPresentationTiming ReadTiming(LoopbackSubscription subscription)
    {
        lock (subscription.Machine.Gate)
        {
            if (subscription.IsDisposed || subscription.Machine.Removed)
            {
                return default;
            }

            LoopbackMachine machine = subscription.Machine;
            return new HostPresentationTiming(machine.FramesPerSecond, machine.LastSequence, machine.LastCycle);
        }
    }

    /// <summary>
    /// Drops a subscription from its machine.
    /// </summary>
    /// <param name="subscription">The window.</param>
    internal void RemoveSubscription(LoopbackSubscription subscription)
    {
        lock (subscription.Machine.Gate)
        {
            subscription.Machine.Subscriptions.Remove(subscription);
        }
    }

    private static void EnsureLive(LoopbackMachine machine)
    {
        if (machine.Removed)
        {
            throw Unknown(machine.Id);
        }
    }

    private static void EnsureSubscription(LoopbackSubscription subscription)
    {
        if (subscription.IsDisposed || subscription.Machine.Removed)
        {
            throw new ObjectDisposedException(nameof(IPresentationSubscription));
        }
    }

    private static void RequireOn(LoopbackMachine machine, string verb)
    {
        if (machine.State is HostRunState.Off or HostRunState.ShuttingDown)
        {
            throw new InvalidOperationException($"Cannot {verb} while {machine.State}.");
        }
    }

    private static void RequireDebugger(LoopbackMachine machine)
    {
        if (!machine.DebuggerAttached)
        {
            throw new InvalidOperationException("The debugger is not attached.");
        }
    }

    private static void RequireKey(HostKey key)
    {
        if (key == HostKey.None || !Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A host key is required.");
        }
    }

    private static void RequireRange(uint address, int length)
    {
        if (length <= 0 || address >= LoopbackMachine.MemorySize || length > LoopbackMachine.MemorySize - address)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The range is outside the 64 KiB stub memory.");
        }
    }

    private static LoopbackDrive RequireDrive(LoopbackMachine machine, DriveId drive)
    {
        LoopbackDrive? target = machine.FindDrive(drive);
        if (target is null)
        {
            throw new InvalidOperationException($"No drive {drive.Label} is installed.");
        }

        return target;
    }

    private static HostDiskController ToController(LoopbackMachine machine)
    {
        HostDrive[] drives = new HostDrive[machine.Drives.Count];
        for (int i = 0; i < drives.Length; i++)
        {
            drives[i] = machine.Drives[i].ToDrive();
        }

        return new HostDiskController(6, "Disk II", drives);
    }

    private static HostInputState ToInputState(LoopbackMachine machine)
    {
        return new HostInputState(
            machine.KeysDown.ToArray(),
            machine.Modifiers,
            new HostGamePortState(
                machine.JoystickX[0],
                machine.JoystickY[0],
                machine.JoystickX[1],
                machine.JoystickY[1],
                machine.Paddles[0],
                machine.Paddles[1],
                machine.Paddles[2],
                machine.Paddles[3],
                machine.Buttons[0],
                machine.Buttons[1],
                machine.Buttons[2],
                machine.Buttons[3]),
            new HostMouseState(
                machine.MouseKind,
                machine.MouseX,
                machine.MouseY,
                machine.MouseLeft,
                machine.MouseMiddle,
                machine.MouseRight,
                machine.CaptureWindowId),
            machine.LastText);
    }

    private static void ApplyKey(LoopbackMachine machine, HostKey key, HostKeyModifiers modifiers, bool down)
    {
        machine.Modifiers = modifiers;
        if (down)
        {
            if (!machine.KeysDown.Contains(key))
            {
                machine.KeysDown.Add(key);
            }
        }
        else
        {
            machine.KeysDown.Remove(key);
        }

        AddLog(machine, Entry(HostInputKind.Key, key: key, modifiers: modifiers, down: down));
    }

    private static void AddLog(LoopbackMachine machine, HostInputEntry entry)
    {
        machine.InputLog.Add(entry);
        if (machine.InputLog.Count > LoopbackMachine.MaxInputLog)
        {
            machine.InputLog.RemoveAt(0);
        }
    }

    private static HostInputEntry Entry(
        HostInputKind kind,
        HostKey key = HostKey.None,
        HostKeyModifiers modifiers = HostKeyModifiers.None,
        bool down = false,
        int index = 0,
        float x = 0,
        float y = 0,
        HostMouseMotionKind motion = HostMouseMotionKind.Relative,
        HostMouseButton mouseButton = HostMouseButton.Left,
        string? windowId = null)
    {
        return new HostInputEntry(kind, key, modifiers, down, index, x, y, motion, mouseButton, windowId);
    }

    private static bool TryMapChar(char character, out HostKey key, out HostKeyModifiers modifiers)
    {
        modifiers = HostKeyModifiers.None;
        if (character is >= 'a' and <= 'z')
        {
            key = (HostKey)((int)HostKey.A + (character - 'a'));
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            key = (HostKey)((int)HostKey.A + (character - 'A'));
            modifiers = HostKeyModifiers.Shift;
            return true;
        }

        if (character is >= '0' and <= '9')
        {
            key = (HostKey)((int)HostKey.D0 + (character - '0'));
            return true;
        }

        if (character == ' ')
        {
            key = HostKey.Space;
            return true;
        }

        if (character is '\r' or '\n')
        {
            key = HostKey.Return;
            return true;
        }

        key = HostKey.None;
        return false;
    }

    private static float ClampAxis(float value, string name)
    {
        float finite = RequireFinite(value, name);
        return Math.Clamp(finite, -1f, 1f);
    }

    private static float RequireFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }

        return value;
    }

    private static byte ToPaddle(float axis)
    {
        int scaled = (int)Math.Round((axis + 1f) * 127.5f, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(scaled, 0, 255);
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

    private static HostMonitorChoice DefaultMonitor(HostVideoKind output)
    {
        return output == HostVideoKind.Rgb ? HostMonitorChoice.Rgb : HostMonitorChoice.Ntsc;
    }

    private static bool MonitorFits(HostVideoKind output, HostMonitorChoice monitor)
    {
        if (output == HostVideoKind.Rgb)
        {
            return monitor == HostMonitorChoice.Rgb;
        }

        return monitor is HostMonitorChoice.Mono or HostMonitorChoice.Green or HostMonitorChoice.Amber or HostMonitorChoice.Ntsc;
    }

    private static void ValidateSample(LoopbackMachine machine, HostPresentationSample sample)
    {
        if (!Enum.IsDefined(sample.Kind) || sample.Kind != machine.OutputKind)
        {
            throw new InvalidOperationException("The sample output does not match the machine.");
        }

        if (sample.Kind == HostVideoKind.Rgb)
        {
            if (sample.Frame is null || sample.Signal is not null)
            {
                throw new InvalidOperationException("An RGB sample is a finished BGRA frame and has no raw signal.");
            }

            ValidateFrame(sample.Frame);
            return;
        }

        if (sample.Frame is null && sample.Signal is null)
        {
            throw new InvalidOperationException("A composite sample needs a raw signal, a decoded frame, or both.");
        }

        if (sample.Frame is not null)
        {
            ValidateFrame(sample.Frame);
        }

        if (sample.Signal is not null)
        {
            ValidateSignal(sample.Signal);
        }
    }

    private static void ValidateFrame(HostBgraFrame frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frame.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frame.Height);
        if (frame.StrideBytes < (frame.Width * 4))
        {
            throw new ArgumentException("The frame stride is shorter than the width.", nameof(frame));
        }

        if (frame.Bgra.Length < (frame.StrideBytes * frame.Height))
        {
            throw new ArgumentException("The frame buffer is shorter than stride times height.", nameof(frame));
        }
    }

    private static void ValidateSignal(HostCompositeSignal signal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(signal.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(signal.Height);
        if (signal.Dots.Length == 0)
        {
            throw new ArgumentException("The composite dots are empty.", nameof(signal));
        }

        if (signal.LineFlags.Length < signal.Height)
        {
            throw new ArgumentException("The line flags are shorter than the height.", nameof(signal));
        }
    }

    private static void ValidateFormat(HostAudioFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(format.SampleRateHz);
        if (format.ChannelCount is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.ChannelCount, "Channel count must be 1 or 2.");
        }

        if (format.BitsPerSample is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.BitsPerSample, "Bits per sample must be 8 or 16.");
        }

        if (format.BitsPerSample == 16 && (!format.Signed || !format.LittleEndian))
        {
            throw new ArgumentOutOfRangeException(nameof(format), "16-bit PCM is signed little-endian.");
        }
    }

    private static void ValidateBlock(HostAudioFormat format, HostAudioBlock block)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(block.SampleCount);
        int expected = block.SampleCount * format.ChannelCount * (format.BitsPerSample / 8);
        if (block.Pcm.Length != expected)
        {
            throw new ArgumentException("The PCM length does not match the format.", nameof(block));
        }
    }

    private static InvalidOperationException Unknown(MachineId machineId)
    {
        return new InvalidOperationException($"No machine '{machineId.Value}' is on this host.");
    }

    private static Task Run(CancellationToken cancellationToken, Action action)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            return Task.FromException(ex);
        }
    }

    private void Reset(MachineId machineId, bool hard)
    {
        HostResetNotice? notice = null;
        Update(machineId, machine =>
        {
            RequireOn(machine, hard ? "hard reset" : "reset");
            if (hard && machine.Power is not null && !machine.Power.HardReset)
            {
                throw new InvalidOperationException("Hard Reset is not available on this machine.");
            }

            notice = new HostResetNotice(machine.Id, hard);
        });

        if (notice is not null)
        {
            Notify(sink => sink.OnReset(notice));
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

    private void Update(MachineId machineId, Action<LoopbackMachine> action)
    {
        LoopbackMachine machine = Find(machineId);
        lock (machine.Gate)
        {
            EnsureLive(machine);
            action(machine);
        }
    }

    private LoopbackMachine Find(MachineId machineId)
    {
        ArgumentNullException.ThrowIfNull(machineId);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!machinesById.TryGetValue(machineId.Value, out LoopbackMachine? machine))
            {
                throw Unknown(machineId);
            }

            return machine;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void Notify(Action<IHostNotificationSink> notify)
    {
        IHostNotificationSink[] snapshot;
        lock (tableLock)
        {
            snapshot = sinks.ToArray();
        }

        foreach (IHostNotificationSink sink in snapshot)
        {
            notify(sink);
        }
    }

    private void Unsubscribe(IHostNotificationSink sink)
    {
        lock (tableLock)
        {
            sinks.Remove(sink);
        }
    }

    private sealed class SinkSubscription : IDisposable
    {
        private readonly LoopbackHostSession session;
        private readonly IHostNotificationSink sink;
        private bool disposed;

        internal SinkSubscription(LoopbackHostSession session, IHostNotificationSink sink)
        {
            this.session = session;
            this.sink = sink;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            session.Unsubscribe(sink);
        }
    }
}