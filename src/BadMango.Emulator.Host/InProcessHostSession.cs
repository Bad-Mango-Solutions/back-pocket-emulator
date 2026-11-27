// <copyright file="InProcessHostSession.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Host.Contracts;

/// <summary>
/// In-process host session backed by live <see cref="IMachine"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Each instance keeps the <see cref="IMachine.RunAsync"/> or <see cref="IMachine.ResumeAsync"/>
/// task that owns its run loop. Power, pause, resume, sleep, and power-off call those lifecycle
/// methods from the client thread. <see cref="IMachine.PowerOffAsync"/> throws on the machine
/// thread, and <see cref="IMachine.Pause"/> waits for the loop, so those calls are not posted.
/// <see cref="IMachineDispatcher.Post"/> stays for work that must land at an instruction boundary
/// while a loop is actually running. A stopped machine runs a post inline or not at all, so this
/// session never awaits one.
/// </para>
/// <para>
/// Reported run state follows <see cref="IMachine.StateChanged"/>. Sleeping and Shutting down have
/// no machine state: Sleep is a pause that the host reports as sleeping, and Shut down is published
/// before the loop stops. Notices are queued and delivered off the machine thread after the host
/// has released the command, so a slow sink cannot stall the CPU loop.
/// </para>
/// </remarks>
public sealed partial class InProcessHostSession : IHostSession, IHostControl
{
    private static readonly TimeSpan RunStartTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock tableLock = new();
    private readonly Lock operationLock = new();
    private readonly MachineProfileSerializer serializer = new();
    private readonly string libraryRoot;
    private readonly List<MachineHandle> machines = [];
    private readonly Dictionary<string, MachineHandle> machinesById = [];
    private readonly Dictionary<string, HostProfileDocument> profiles = [];
    private readonly List<string> profileOrder = [];
    private readonly ConcurrentDictionary<IHostNotificationSink, byte> sinks = [];
    private readonly Channel<Action> notices = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    private readonly NotSupportedHostPresentation presentation;
    private readonly NotSupportedHostInput input;
    private readonly NotSupportedHostDebugger debugger;
    private readonly Task noticePump;

    private int liveRunLoops;
    private int operationsInProgress;
    private TaskCompletionSource operationsIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        noticePump = Task.Run(DeliverNoticesAsync);
        ObserveFault(noticePump);
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

    /// <summary>
    /// Gets how many run loops are still executing. Used by tests to prove dispose and power-off join them.
    /// </summary>
    internal int LiveRunLoops => Volatile.Read(ref liveRunLoops);

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

        notices.Writer.TryComplete();
        ObserveFault(noticePump);
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
        machine.ClockSpeedHz = EffectiveClock(spec.NominalClockHz, HostThrottle.Normal);

        MachineId machineId;
        lock (tableLock)
        {
            if (disposed)
            {
                machine.Dispose();
                ThrowIfDisposed();
            }

            machineId = MachineId.New();
            MachineHandle handle = new(this, machineId, spec, machine, info);
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
            if (machine.ReportedState != HostRunState.Off)
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
            if (machine.ReportedState != HostRunState.Off)
            {
                throw new InvalidOperationException("Power the machine off to change the profile.");
            }

            machine.OverlayJson = string.IsNullOrWhiteSpace(overlayJson) ? null : overlayJson;
        }
    }

    /// <inheritdoc />
    public Task PowerOnAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return PowerOnCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task PauseAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return PauseCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task ResumeAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return ResumeCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task SleepAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return SleepCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task ResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return ResetCoreAsync(machineId, hardReset: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task HardResetAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return ResetCoreAsync(machineId, hardReset: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task ShutDownAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return ShutDownCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task PowerOffAsync(MachineId machineId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return PowerOffCoreAsync(machineId, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetThrottleAsync(MachineId machineId, HostThrottle throttle, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (!Enum.IsDefined(throttle))
        {
            throw new ArgumentOutOfRangeException(nameof(throttle), throttle, "The throttle is not defined.");
        }

        return SetThrottleCoreAsync(machineId, throttle, cancellationToken);
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

    /// <summary>
    /// Reads the scheduler cycle counter. Tests use it to see that a live machine is executing.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The scheduler time, in cycles.</returns>
    internal ulong ReadSchedulerCycles(MachineId machineId)
    {
        return Find(machineId).Machine.Now.Value;
    }

    /// <summary>
    /// Reads the program counter. Tests use it to see that instructions are retiring.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The current program counter.</returns>
    internal uint ReadProgramCounter(MachineId machineId)
    {
        return Find(machineId).Machine.Cpu.GetPC();
    }

    /// <summary>
    /// Gets the live machine. Tests arm signals the guest ROM is waiting on.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The machine instance.</returns>
    internal IMachine GetMachine(MachineId machineId)
    {
        return Find(machineId).Machine;
    }

    /// <summary>
    /// Reads the machine's own lifecycle state.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The machine state.</returns>
    internal MachineState ReadMachineState(MachineId machineId)
    {
        return Find(machineId).Machine.State;
    }

    /// <summary>
    /// Reads the exception from a run loop that faulted after it started.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The fault, or <see langword="null"/> when the loop has not faulted.</returns>
    internal Exception? ReadRunFault(MachineId machineId)
    {
        return Find(machineId).RunFault;
    }

    private async Task PowerOnCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            lock (handle.Sync)
            {
                if (handle.ReportedState != HostRunState.Off)
                {
                    throw new InvalidOperationException($"Cannot power on while {handle.ReportedState}.");
                }
            }

            ApplyPendingOverlay(handle);
            await handle.Machine.PowerOnAsync(cancellationToken).ConfigureAwait(false);
            if (handle.StartPaused)
            {
                await EnterPausedWithoutRunningAsync(handle, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                handle.StartRun(static (machine, token) => machine.RunAsync(token));
                await WaitUntilReportedAsync(handle, HostRunState.Running, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                handle.StopLoop();
                throw;
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task EnterPausedWithoutRunningAsync(MachineHandle handle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // RunAsync refuses to start from the machine thread and only enters Paused when its token
        // is already canceled, before the first slice. That is a powered-on machine with no
        // instruction retired by the run loop. ResumeAsync is what starts the loop later.
        lock (handle.Sync)
        {
            handle.AcceptPausedOnly = true;
        }

        try
        {
            using CancellationTokenSource startPaused = new();
            startPaused.Cancel();
            await handle.Machine.RunAsync(startPaused.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (handle.Sync)
            {
                handle.AcceptPausedOnly = false;
            }
        }

        if (handle.ReportedState != HostRunState.Paused || handle.Machine.State != MachineState.Paused)
        {
            throw new InvalidOperationException("Start paused did not leave the machine paused.");
        }
    }

    private async Task PauseCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            lock (handle.Sync)
            {
                if (handle.ReportedState != HostRunState.Running)
                {
                    throw new InvalidOperationException($"Cannot pause while {handle.ReportedState}.");
                }
            }

            StopExecution(handle);
            if (handle.ReportedState != HostRunState.Paused)
            {
                throw new InvalidOperationException($"Cannot pause while {handle.ReportedState}.");
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task ResumeCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            bool fromStopped;
            lock (handle.Sync)
            {
                if (handle.ReportedState is not (HostRunState.Paused or HostRunState.Sleeping or HostRunState.Stopped))
                {
                    throw new InvalidOperationException($"Cannot resume while {handle.ReportedState}.");
                }

                fromStopped = handle.ReportedState == HostRunState.Stopped;
                handle.ReportSleep = false;
            }

            if (fromStopped && handle.Machine.Cpu.HaltReason == HaltState.Stp)
            {
                handle.Machine.Reset(hardReset: false);
            }

            try
            {
                if (fromStopped)
                {
                    handle.StartRun(static (machine, token) => machine.RunAsync(token));
                }
                else
                {
                    handle.StartRun(static (machine, token) => machine.ResumeAsync(token));
                }

                await WaitUntilReportedAsync(handle, HostRunState.Running, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                handle.StopLoop();
                throw;
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task SleepCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            lock (handle.Sync)
            {
                if (handle.PowerCapabilities?.Sleep != true)
                {
                    throw new InvalidOperationException("This machine cannot sleep.");
                }

                if (handle.ReportedState != HostRunState.Running)
                {
                    throw new InvalidOperationException($"Cannot sleep while {handle.ReportedState}.");
                }

                handle.ReportSleep = true;
            }

            try
            {
                StopExecution(handle);
                if (handle.ReportedState != HostRunState.Sleeping)
                {
                    throw new InvalidOperationException($"Cannot sleep while {handle.ReportedState}.");
                }
            }
            finally
            {
                if (handle.ReportedState != HostRunState.Sleeping)
                {
                    lock (handle.Sync)
                    {
                        handle.ReportSleep = false;
                    }
                }
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task ResetCoreAsync(MachineId machineId, bool hardReset, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            HostRunState before;
            lock (handle.Sync)
            {
                RequireOn(handle, hardReset ? "hard reset" : "reset");
                if (hardReset && handle.PowerCapabilities is not null && !handle.PowerCapabilities.HardReset)
                {
                    throw new InvalidOperationException("Hard Reset is not available on this machine.");
                }

                before = handle.ReportedState;
            }

            // Reset posts itself when a loop is running and keeps the run state. Calling it here
            // avoids a second post, which would run Reset on the machine thread inside the loop.
            handle.Machine.Reset(hardReset);
            if (handle.ReportedState != before)
            {
                throw new InvalidOperationException($"Reset changed the run state from {before} to {handle.ReportedState}.");
            }

            Notify(sink => sink.OnReset(new HostResetNotice(handle.Id, hardReset)));
            if (before == HostRunState.Stopped)
            {
                handle.StartRun(static (machine, token) => machine.RunAsync(token));
                await WaitUntilReportedAsync(handle, HostRunState.Running, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task ShutDownCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            bool changed;
            lock (handle.Sync)
            {
                if (handle.PowerCapabilities?.SoftPower != true)
                {
                    throw new InvalidOperationException("Shut Down is only available on a soft-power machine.");
                }

                RequireOn(handle, "shut down");
                handle.SuppressDerived = true;
                changed = handle.SetReportedState(HostRunState.ShuttingDown);
            }

            if (changed)
            {
                NotifyStateChanged(handle.Id, HostRunState.ShuttingDown);
            }

            try
            {
                await StopThenPowerOffAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ClearSuppressUnlessOff(handle);
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task PowerOffCoreAsync(MachineId machineId, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            lock (handle.Sync)
            {
                if (handle.PowerCapabilities?.SoftPower == true)
                {
                    throw new InvalidOperationException("Power Off is not available on a soft-power machine. Shut Down instead.");
                }

                RequireOn(handle, "power off");
                handle.SuppressDerived = true;
            }

            try
            {
                await StopThenPowerOffAsync(handle, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ClearSuppressUnlessOff(handle);
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task SetThrottleCoreAsync(MachineId machineId, HostThrottle throttle, CancellationToken cancellationToken)
    {
        MachineHandle handle = Find(machineId);
        await handle.Lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        EnterOperation();
        try
        {
            long clock = EffectiveClock(handle.NominalClockHz, throttle);
            lock (handle.Sync)
            {
                handle.Throttle = throttle;
                handle.Machine.ClockSpeedHz = clock;
                if (handle.ReportedState == HostRunState.Running)
                {
                    handle.SeedSpeedWindow();
                }
            }
        }
        finally
        {
            ExitOperation();
            handle.Lifecycle.Release();
        }
    }

    private async Task StopThenPowerOffAsync(MachineHandle handle, CancellationToken cancellationToken)
    {
        handle.StopLoop();
        await handle.Machine.PowerOffAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unblocks a delay inside the run loop, then pauses from this thread.
    /// </summary>
    /// <param name="handle">The machine.</param>
    /// <remarks>
    /// <see cref="IMachine.Pause"/> waits until the loop leaves. The loop may be inside
    /// <see cref="Task.Delay(int, CancellationToken)"/> for throttling, which only notices the
    /// run token. Canceling that token lets the wait finish; <see cref="IMachine.Pause"/> is still
    /// what stops execution.
    /// </remarks>
    private void StopExecution(MachineHandle handle)
    {
        handle.CancelRun();
        handle.Machine.Pause();
        handle.AwaitRun();
    }

    private void ClearSuppressUnlessOff(MachineHandle handle)
    {
        lock (handle.Sync)
        {
            if (handle.Machine.State != MachineState.Off)
            {
                handle.SuppressDerived = false;
            }
        }
    }

    private async Task WaitUntilReportedAsync(MachineHandle handle, HostRunState desired, CancellationToken cancellationToken)
    {
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(RunStartTimeout);
        Task reported = handle.WaitForReportedAsync(desired, wait.Token);
        try
        {
            Task run = handle.CurrentRun;
            Task finished = await Task.WhenAny(reported, run).ConfigureAwait(false);
            if (ReferenceEquals(finished, reported) || handle.IsReported(desired))
            {
                await reported.ConfigureAwait(false);
                return;
            }

            if (run.IsFaulted)
            {
                await run.ConfigureAwait(false);
            }

            if (run.IsCompleted && desired == HostRunState.Running && handle.IsReported(HostRunState.Stopped))
            {
                // The loop started and the guest halted before this wait observed Running.
                return;
            }

            throw new InvalidOperationException($"The run loop ended before the machine was {desired}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"The machine did not reach {desired}.");
        }
        finally
        {
            await wait.CancelAsync().ConfigureAwait(false);
            try
            {
                await reported.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The wait was canceled because the run loop ended or the caller gave up.
            }
        }
    }

    private void OnRunFaulted(MachineHandle handle, Exception fault)
    {
        bool changed;
        lock (handle.Sync)
        {
            handle.RunFault = fault;
            changed = handle.SetReportedState(HostRunState.Stopped);
        }

        if (changed || handle.ReportedState == HostRunState.Stopped)
        {
            NotifyStateChanged(handle.Id, HostRunState.Stopped);
        }
    }

    private void ApplyPendingOverlay(MachineHandle handle)
    {
        while (true)
        {
            string? overlay;
            lock (handle.Sync)
            {
                overlay = handle.OverlayJson;
                if (handle.BuiltFrom(overlay))
                {
                    return;
                }
            }

            string profileJson;
            lock (tableLock)
            {
                ThrowIfDisposed();
                profileJson = profiles[handle.ProfileId].Json;
            }

            MachineProfile profile = serializer.Deserialize(MergeProfileJson(profileJson, overlay));
            (IMachine machine, _, MachineInfo info) = MachineFactory.CreateDebugSystem(profile, new ProfilePathResolver(libraryRoot));
            lock (handle.Sync)
            {
                if (!string.Equals(handle.OverlayJson, overlay, StringComparison.Ordinal))
                {
                    machine.Dispose();
                    continue;
                }

                long clock = EffectiveClock(handle.NominalClockHz, handle.Throttle);
                handle.ReplaceMachine(machine, clock, info);
                handle.NoteBuiltOverlay(overlay);
                return;
            }
        }
    }

    private string MergeProfileJson(string profileJson, string? overlayJson)
    {
        if (string.IsNullOrWhiteSpace(overlayJson))
        {
            return profileJson;
        }

        JsonNode? profileNode = JsonNode.Parse(profileJson);
        JsonNode? overlayNode = JsonNode.Parse(overlayJson);
        if (profileNode is null || overlayNode is null)
        {
            throw new InvalidOperationException("The profile overlay is not valid JSON.");
        }

        return MergeJson(profileNode, overlayNode).ToJsonString();
    }

    private JsonNode MergeJson(JsonNode template, JsonNode overlay)
    {
        if (template is not JsonObject templateObject || overlay is not JsonObject overlayObject)
        {
            return overlay.DeepClone();
        }

        JsonObject merged = [];
        foreach (KeyValuePair<string, JsonNode?> property in templateObject)
        {
            merged[property.Key] = property.Value?.DeepClone();
        }

        foreach (KeyValuePair<string, JsonNode?> property in overlayObject)
        {
            if (property.Value is null)
            {
                merged.Remove(property.Key);
                continue;
            }

            if (merged[property.Key] is JsonObject existing && property.Value is JsonObject patch)
            {
                merged[property.Key] = MergeJson(existing, patch);
                continue;
            }

            merged[property.Key] = property.Value.DeepClone();
        }

        return merged;
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
        if (machine.ReportedState is HostRunState.Off or HostRunState.ShuttingDown)
        {
            throw new InvalidOperationException($"Cannot {verb} while {machine.ReportedState}.");
        }
    }

    private void OnMachineStateChanged(MachineHandle handle, MachineState state)
    {
        HostRunState? reported;
        bool changed;
        lock (handle.Sync)
        {
            reported = handle.Translate(state);
            if (reported is null)
            {
                return;
            }

            changed = handle.SetReportedState(reported.Value);
        }

        if (changed)
        {
            NotifyStateChanged(handle.Id, reported.Value);
        }
    }

    private void NotifyStateChanged(MachineId machineId, HostRunState state)
    {
        Notify(sink => sink.OnStateChanged(new HostStateNotice(machineId, state)));
    }

    private void Notify(Action<IHostNotificationSink> notify)
    {
        if (sinks.IsEmpty)
        {
            return;
        }

        IHostNotificationSink[] snapshot = sinks.Keys.ToArray();
        if (snapshot.Length == 0)
        {
            return;
        }

        notices.Writer.TryWrite(() =>
        {
            foreach (IHostNotificationSink sink in snapshot)
            {
                notify(sink);
            }
        });
    }

    private async Task DeliverNoticesAsync()
    {
        await foreach (Action delivery in notices.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await WaitUntilIdleAsync().ConfigureAwait(false);
            delivery();
        }
    }

    private void EnterOperation()
    {
        lock (operationLock)
        {
            operationsInProgress++;
            if (operationsInProgress == 1)
            {
                operationsIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void ExitOperation()
    {
        TaskCompletionSource? completed = null;
        lock (operationLock)
        {
            operationsInProgress--;
            if (operationsInProgress == 0)
            {
                completed = operationsIdle;
            }
        }

        completed?.TrySetResult();
    }

    private async Task WaitUntilIdleAsync()
    {
        while (true)
        {
            Task idle;
            lock (operationLock)
            {
                idle = operationsInProgress == 0
                    ? Task.CompletedTask
                    : operationsIdle.Task;
            }

            if (idle.IsCompleted)
            {
                return;
            }

            await idle.ConfigureAwait(false);
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

    private void ObserveFault(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        task.ContinueWith(
            static completed =>
            {
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Unsubscribe(IHostNotificationSink sink)
    {
        sinks.TryRemove(sink, out _);
    }
}