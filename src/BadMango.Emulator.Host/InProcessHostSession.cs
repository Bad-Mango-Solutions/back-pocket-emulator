// <copyright file="InProcessHostSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using System.Collections.Concurrent;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Host.Contracts;

/// <summary>
/// In-process host session backed by live <see cref="IMachine"/> instances.
/// </summary>
public sealed class InProcessHostSession : IHostSession, IHostControl
{
    private readonly Lock tableLock = new();
    private readonly MachineProfileSerializer serializer = new();
    private readonly string libraryRoot;
    private readonly List<MachineHandle> machines = [];
    private readonly Dictionary<string, MachineHandle> machinesById = [];
    private readonly Dictionary<string, HostProfileDocument> profiles = [];
    private readonly List<string> profileOrder = [];
    private readonly ConcurrentDictionary<IHostNotificationSink, byte> sinks = [];
    private readonly NotSupportedHostPresentation presentation;
    private readonly NotSupportedHostInput input;
    private readonly NotSupportedHostDebugger debugger;

    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessHostSession"/> class.
    /// </summary>
    /// <param name="libraryRoot">The library root for <c>library://</c> paths.</param>
    internal InProcessHostSession(string libraryRoot)
    {
        this.libraryRoot = libraryRoot;
        presentation = new("#810");
        input = new("#809");
        debugger = new("#813");
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
    public IHostPresentation Presentation => presentation;

    /// <inheritdoc />
    public IHostInput Input => input;

    /// <inheritdoc />
    public IHostDebugger Debugger => debugger;

    /// <inheritdoc />
    public void Dispose()
    {
        MachineHandle[] snapshot;
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

        foreach (MachineHandle machine in snapshot)
        {
            machine.Dispose();
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

        HostProfileDocument profileDocument;
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!profiles.TryGetValue(spec.ProfileId, out profileDocument!))
            {
                throw new InvalidOperationException($"No profile '{spec.ProfileId}' is stored on this host.");
            }
        }

        MachineProfile profile = serializer.Deserialize(profileDocument.Json);
        ProfilePathResolver resolver = new(libraryRoot);
        (IMachine machine, _, MachineInfo info) = MachineFactory.CreateDebugSystem(profile, resolver);

        MachineId machineId = MachineId.New();
        MachineHandle handle = new(machineId, spec, machine, info, OnMachineStateChanged);
        machine.ClockSpeedHz = EffectiveClock(spec.NominalClockHz, HostThrottle.Normal);

        lock (tableLock)
        {
            ThrowIfDisposed();
            machines.Add(handle);
            machinesById.Add(machineId.Value, handle);
        }

        Notify(sink => sink.OnInstanceAdded(machineId));
        return machineId;
    }

    /// <inheritdoc />
    public void RemoveInstance(MachineId machineId)
    {
        MachineHandle machine = Find(machineId);

        lock (tableLock)
        {
            ThrowIfDisposed();
            if (machine.State != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off before removing it.");
            }

            machines.Remove(machine);
            machinesById.Remove(machine.Id.Value);
        }

        machine.Dispose();
        Notify(sink => sink.OnInstanceRemoved(machine.Id));
    }

    /// <inheritdoc />
    public IReadOnlyList<HostMachineInfo> ListInstances()
    {
        MachineHandle[] snapshot;
        lock (tableLock)
        {
            ThrowIfDisposed();
            snapshot = machines.ToArray();
        }

        HostMachineInfo[] infos = new HostMachineInfo[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            infos[i] = snapshot[i].ReadInfo();
        }

        return infos;
    }

    /// <inheritdoc />
    public HostMachineInfo GetMachineInfo(MachineId machineId)
    {
        return Find(machineId).ReadInfo();
    }

    /// <inheritdoc />
    public void UpsertProfile(HostProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Json);

        _ = serializer.Deserialize(profile.Json);

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
        MachineHandle machine = Find(machineId);
        lock (machine.Sync)
        {
            if (machine.State != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change the profile.");
            }

            machine.OverlayJson = string.IsNullOrWhiteSpace(overlayJson) ? null : overlayJson;
        }
    }

    /// <inheritdoc />
    public async Task PowerOnAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (machine.Sync)
        {
            if (machine.State != HostRunState.Off)
            {
                throw new InvalidOperationException($"Cannot power on while {machine.State}.");
            }
        }

        await machine.Machine.PowerOnAsync(cancellationToken).ConfigureAwait(false);
        if (machine.StartPaused)
        {
            machine.UpdateState(HostRunState.Paused);
            NotifyStateChanged(machine.Id, HostRunState.Paused);
            return;
        }

        machine.UpdateState(HostRunState.Running);
        NotifyStateChanged(machine.Id, HostRunState.Running);
    }

    /// <inheritdoc />
    public Task PauseAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            if (machine.State != HostRunState.Running)
            {
                throw new InvalidOperationException($"Cannot pause while {machine.State}.");
            }

            machine.UpdateState(HostRunState.Paused);
            NotifyStateChanged(machine.Id, HostRunState.Paused);
        });
    }

    /// <inheritdoc />
    public Task ResumeAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            if (machine.State is not (HostRunState.Paused or HostRunState.Sleeping))
            {
                throw new InvalidOperationException($"Cannot resume while {machine.State}.");
            }

            machine.UpdateState(HostRunState.Running);
            NotifyStateChanged(machine.Id, HostRunState.Running);
        });
    }

    /// <inheritdoc />
    public Task SleepAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            if (machine.PowerCapabilities?.Sleep != true)
            {
                throw new InvalidOperationException("This machine cannot sleep.");
            }

            if (machine.State != HostRunState.Running)
            {
                throw new InvalidOperationException($"Cannot sleep while {machine.State}.");
            }

            machine.UpdateState(HostRunState.Sleeping);
            NotifyStateChanged(machine.Id, HostRunState.Sleeping);
        });
    }

    /// <inheritdoc />
    public Task ResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            RequireOn(machine, "reset");
            machine.Machine.Reset(hardReset: false);
            Notify(sink => sink.OnReset(new HostResetNotice(machine.Id, Hard: false)));
        });
    }

    /// <inheritdoc />
    public Task HardResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            RequireOn(machine, "hard reset");
            if (machine.PowerCapabilities is not null && !machine.PowerCapabilities.HardReset)
            {
                throw new InvalidOperationException("Hard Reset is not available on this machine.");
            }

            machine.Machine.Reset(hardReset: true);
            Notify(sink => sink.OnReset(new HostResetNotice(machine.Id, Hard: true)));
        });
    }

    /// <inheritdoc />
    public async Task ShutDownAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        await PostMutationAsync(machine, cancellationToken, () =>
        {
            if (machine.PowerCapabilities?.SoftPower != true)
            {
                throw new InvalidOperationException("Shut Down is only available on a soft-power machine.");
            }

            RequireOn(machine, "shut down");
            machine.UpdateState(HostRunState.ShuttingDown);
            NotifyStateChanged(machine.Id, HostRunState.ShuttingDown);
        }).ConfigureAwait(false);

        await machine.Machine.PowerOffAsync(cancellationToken).ConfigureAwait(false);
        machine.UpdateState(HostRunState.Off);
        NotifyStateChanged(machine.Id, HostRunState.Off);
    }

    /// <inheritdoc />
    public async Task PowerOffAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        MachineHandle machine = Find(machineId);
        await PostMutationAsync(machine, cancellationToken, () =>
        {
            if (machine.PowerCapabilities?.SoftPower == true)
            {
                throw new InvalidOperationException("Power Off is not available on a soft-power machine. Shut Down instead.");
            }

            RequireOn(machine, "power off");
        }).ConfigureAwait(false);

        await machine.Machine.PowerOffAsync(cancellationToken).ConfigureAwait(false);
        machine.UpdateState(HostRunState.Off);
        NotifyStateChanged(machine.Id, HostRunState.Off);
    }

    /// <inheritdoc />
    public Task SetThrottleAsync(MachineId machineId, HostThrottle throttle, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(throttle))
        {
            throw new ArgumentOutOfRangeException(nameof(throttle), throttle, "The throttle is not defined.");
        }

        MachineHandle machine = Find(machineId);
        return PostMutationAsync(machine, cancellationToken, () =>
        {
            machine.Throttle = throttle;
            machine.Machine.ClockSpeedHz = EffectiveClock(machine.NominalClockHz, throttle);
        });
    }

    /// <inheritdoc />
    public Task MountDiskAsync(MachineId machineId, DriveId drive, string path, bool writeProtect, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Disk control is not implemented in host slice #808.");
    }

    /// <inheritdoc />
    public Task EjectDiskAsync(MachineId machineId, DriveId drive, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Disk control is not implemented in host slice #808.");
    }

    /// <inheritdoc />
    public Task SetWriteProtectAsync(MachineId machineId, DriveId drive, bool writeProtect, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Disk control is not implemented in host slice #808.");
    }

    /// <inheritdoc />
    public IReadOnlyList<HostDiskController> ListDisks(MachineId machineId)
    {
        throw new NotSupportedException("Disk control is not implemented in host slice #808.");
    }

    /// <inheritdoc />
    public IReadOnlyList<HostSlotInfo> ListSlots(MachineId machineId)
    {
        throw new NotSupportedException("Slot control is not implemented in host slice #808.");
    }

    /// <inheritdoc />
    public Task AttachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Debugger control is not implemented in host slice #813.");
    }

    /// <inheritdoc />
    public Task DetachDebuggerAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Debugger control is not implemented in host slice #813.");
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IHostNotificationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ThrowIfDisposed();
        sinks[sink] = 0;
        return new SinkSubscription(this, sink);
    }

    private long EffectiveClock(long nominal, HostThrottle throttle)
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

    private void RequireOn(MachineHandle machine, string verb)
    {
        if (machine.State is HostRunState.Off or HostRunState.ShuttingDown)
        {
            throw new InvalidOperationException($"Cannot {verb} while {machine.State}.");
        }
    }

    private void NotifyStateChanged(MachineId machineId, HostRunState state)
    {
        Notify(sink => sink.OnStateChanged(new HostStateNotice(machineId, state)));
    }

    private void OnMachineStateChanged(MachineHandle machine, MachineState state)
    {
        HostRunState? translated = state switch
        {
            MachineState.Off => HostRunState.Off,
            MachineState.Running => HostRunState.Running,
            MachineState.Paused => HostRunState.Paused,
            MachineState.Stopped => null,
            _ => null,
        };

        if (translated is null)
        {
            return;
        }

        bool changed = machine.UpdateState(translated.Value);
        if (!changed)
        {
            return;
        }

        NotifyStateChanged(machine.Id, translated.Value);
    }

    private void Notify(Action<IHostNotificationSink> notify)
    {
        foreach (KeyValuePair<IHostNotificationSink, byte> sink in sinks)
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                static state =>
                {
                    var (callback, target) = ((Action<IHostNotificationSink>, IHostNotificationSink))state!;
                    callback(target);
                },
                (notify, sink.Key));
        }
    }

    private MachineHandle Find(MachineId machineId)
    {
        ArgumentNullException.ThrowIfNull(machineId);
        lock (tableLock)
        {
            ThrowIfDisposed();
            if (!machinesById.TryGetValue(machineId.Value, out MachineHandle? machine))
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

    private InvalidOperationException Unknown(MachineId machineId)
    {
        return new InvalidOperationException($"No machine '{machineId.Value}' is on this host.");
    }

    private Task PostMutationAsync(MachineHandle machine, CancellationToken cancellationToken, Action action)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        machine.Machine.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completed.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                action();
                completed.TrySetResult();
            }
            catch (Exception ex)
            {
                completed.TrySetException(ex);
            }
        });

        return completed.Task;
    }

    private void Unsubscribe(IHostNotificationSink sink)
    {
        sinks.TryRemove(sink, out _);
    }

    private sealed class SinkSubscription : IDisposable
    {
        private readonly InProcessHostSession session;
        private readonly IHostNotificationSink sink;
        private int disposed;

        internal SinkSubscription(InProcessHostSession session, IHostNotificationSink sink)
        {
            this.session = session;
            this.sink = sink;
        }

        void IDisposable.Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            session.Unsubscribe(sink);
        }
    }

    private sealed class MachineHandle
    {
        private readonly Action<MachineHandle, MachineState> stateChanged;
        private int disposed;

        internal MachineHandle(
            MachineId id,
            HostInstanceSpec spec,
            IMachine machine,
            MachineInfo info,
            Action<MachineHandle, MachineState> stateChanged)
        {
            Id = id;
            Name = spec.Name;
            ProfileId = spec.ProfileId;
            StartPaused = spec.StartPaused;
            PowerCapabilities = spec.PowerCapabilities;
            NominalClockHz = spec.NominalClockHz;
            OutputKind = spec.OutputKind;
            CpuSummary = spec.CpuSummary ?? info.CpuType;
            SchedulerSummary = spec.SchedulerSummary;
            Machine = machine;
            this.stateChanged = stateChanged;
            machine.StateChanged += OnMachineStateChanged;
        }

        internal Lock Sync { get; } = new();

        internal MachineId Id { get; }

        internal IMachine Machine { get; }

        internal string Name { get; }

        internal string ProfileId { get; }

        internal bool StartPaused { get; }

        internal PowerCapabilities? PowerCapabilities { get; }

        internal long NominalClockHz { get; }

        internal HostVideoKind OutputKind { get; }

        internal string? CpuSummary { get; }

        internal string? SchedulerSummary { get; }

        internal HostRunState State { get; private set; } = HostRunState.Off;

        internal HostThrottle Throttle { get; set; } = HostThrottle.Normal;

        internal string? OverlayJson { get; set; }

        internal HostMachineInfo ReadInfo()
        {
            lock (Sync)
            {
                return new HostMachineInfo(
                    Id,
                    Name,
                    State,
                    PowerCapabilities,
                    NominalClockHz,
                    Throttle,
                    EffectiveClockForInfo(NominalClockHz, Throttle),
                    CalculateMeasuredSpeed(),
                    ProfileId,
                    OverlayJson,
                    DebuggerAttached: false,
                    CpuSummary,
                    SchedulerSummary,
                    BootDriveId: null,
                    OutputKind,
                    StartPaused);
            }
        }

        internal bool UpdateState(HostRunState value)
        {
            lock (Sync)
            {
                if (State == value)
                {
                    return false;
                }

                State = value;
                return true;
            }
        }

        internal void Dispose()
        {
            DisposeCore();
        }

        private void OnMachineStateChanged(MachineState state)
        {
            stateChanged(this, state);
        }

        private double? CalculateMeasuredSpeed()
        {
            lock (Sync)
            {
                if (State != HostRunState.Running)
                {
                    return null;
                }

                long clockSpeed = Machine.ClockSpeedHz;
                if (NominalClockHz <= 0)
                {
                    return null;
                }

                if (clockSpeed == 0)
                {
                    return null;
                }

                return Math.Clamp(clockSpeed / (double)NominalClockHz, 0, 1000);
            }
        }

        private long EffectiveClockForInfo(long nominal, HostThrottle throttle)
        {
            return throttle switch
            {
                HostThrottle.Half => nominal / 2,
                HostThrottle.Normal => nominal,
                HostThrottle.Double => nominal > (long.MaxValue / 2)
                    ? 0
                    : nominal * 2,
                HostThrottle.Max => 0,
                _ => nominal,
            };
        }

        private void DisposeCore()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Machine.StateChanged -= OnMachineStateChanged;
            Machine.Dispose();
        }
    }

    private sealed class NotSupportedHostPresentation(string slice) : IHostPresentation
    {
        IPresentationSubscription IHostPresentation.Subscribe(MachineId machineId, string windowId, HostMonitorChoice? monitor)
        {
            throw new NotSupportedException($"Presentation is not implemented in host slice {slice}.");
        }

        HostScreenText IHostPresentation.GetScreenText(MachineId machineId)
        {
            throw new NotSupportedException($"Presentation is not implemented in host slice {slice}.");
        }

        HostVideoState IHostPresentation.GetVideoState(MachineId machineId)
        {
            throw new NotSupportedException($"Presentation is not implemented in host slice {slice}.");
        }
    }

    private sealed class NotSupportedHostInput(string slice) : IHostInput
    {
        void IHostInput.KeyDown(MachineId machineId, HostKey key, HostKeyModifiers modifiers) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.KeyUp(MachineId machineId, HostKey key, HostKeyModifiers modifiers) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        HostTextReceipt IHostInput.InjectText(MachineId machineId, string text) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.SetJoystick(MachineId machineId, int index, float x, float y) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.SetPaddle(MachineId machineId, int paddle, byte position) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.SetButton(MachineId machineId, int button, bool pressed) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.MoveMouse(MachineId machineId, HostMouseMotionKind kind, float x, float y) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.SetMouseButton(MachineId machineId, HostMouseButton button, bool pressed) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.CapturePointer(MachineId machineId, string windowId) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        void IHostInput.ReleasePointer(MachineId machineId, string windowId) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");

        HostInputState IHostInput.GetInputState(MachineId machineId) => throw new NotSupportedException($"Input is not implemented in host slice {slice}.");
    }

    private sealed class NotSupportedHostDebugger(string slice) : IHostDebugger
    {
        bool IHostDebugger.IsAttached(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        HostMemoryBlock IHostDebugger.ReadMemory(MachineId machineId, uint address, int length) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        void IHostDebugger.WriteMemory(MachineId machineId, uint address, ReadOnlyMemory<byte> data) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        IReadOnlyList<HostDisassemblyLine> IHostDebugger.Disassemble(MachineId machineId, uint address, int count) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        HostRegisters IHostDebugger.GetRegisters(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        void IHostDebugger.AddBreakpoint(MachineId machineId, uint address, string? label) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        bool IHostDebugger.RemoveBreakpoint(MachineId machineId, uint address) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        IReadOnlyList<HostBreakpoint> IHostDebugger.GetBreakpoints(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        void IHostDebugger.ClearBreakpoints(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        void IHostDebugger.AddWatch(MachineId machineId, uint address, HostWatchAccess access, bool stopOnHit, string? label) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        bool IHostDebugger.RemoveWatch(MachineId machineId, uint address) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        IReadOnlyList<HostWatch> IHostDebugger.GetWatches(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");

        void IHostDebugger.ClearWatches(MachineId machineId) => throw new NotSupportedException($"Debugger is not implemented in host slice {slice}.");
    }
}