// <copyright file="InProcessHostSession.Machines.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using System.Diagnostics;
using System.Runtime.ExceptionServices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Host.Contracts;

/// <content>
/// Per-machine run loop and the planes this slice does not bind yet.
/// </content>
public sealed partial class InProcessHostSession
{
    private sealed class MachineHandle
    {
        private static readonly TimeSpan MinimumSpeedSample = TimeSpan.FromMilliseconds(100);

        private readonly InProcessHostSession session;
        private readonly Action<MachineHandle, MachineState> stateChanged;
        private readonly List<StateWait> stateWaits = [];
        private readonly bool cpuSummaryFromSpec;
        private CancellationTokenSource? runCancellation;
        private Task? runTask;
        private SpeedSample? speedSample;
        private string? builtOverlay;
        private bool builtFromTemplate = true;
        private int disposed;

        internal MachineHandle(
            InProcessHostSession session,
            MachineId id,
            HostInstanceSpec spec,
            IMachine machine,
            MachineInfo info)
        {
            this.session = session;
            Id = id;
            Name = spec.Name;
            ProfileId = spec.ProfileId;
            StartPaused = spec.StartPaused;
            PowerCapabilities = spec.PowerCapabilities;
            NominalClockHz = spec.NominalClockHz;
            OutputKind = spec.OutputKind;
            cpuSummaryFromSpec = spec.CpuSummary is not null;
            CpuSummary = spec.CpuSummary ?? info.CpuType;
            SchedulerSummary = spec.SchedulerSummary;
            Machine = machine;
            stateChanged = session.OnMachineStateChanged;
            machine.StateChanged += OnMachineStateChanged;
        }

        internal Lock Sync { get; } = new();

        internal SemaphoreSlim Lifecycle { get; } = new(1, 1);

        internal MachineId Id { get; }

        internal IMachine Machine { get; private set; }

        internal string Name { get; }

        internal string ProfileId { get; }

        internal bool StartPaused { get; }

        internal PowerCapabilities? PowerCapabilities { get; }

        internal long NominalClockHz { get; }

        internal HostVideoKind OutputKind { get; }

        internal string? CpuSummary { get; private set; }

        internal string? SchedulerSummary { get; }

        internal HostRunState ReportedState { get; private set; } = HostRunState.Off;

        internal HostThrottle Throttle { get; set; } = HostThrottle.Normal;

        internal string? OverlayJson { get; set; }

        internal Exception? RunFault { get; set; }

        /// <summary>
        /// When set, a pause is reported as <see cref="HostRunState.Sleeping"/>.
        /// The machine itself only has <see cref="MachineState.Paused"/>.
        /// </summary>
        internal bool ReportSleep { get; set; }

        /// <summary>
        /// When set, machine transitions other than <see cref="MachineState.Off"/> are not published.
        /// Power off cancels the run token, which would otherwise surface as a pause.
        /// </summary>
        internal bool SuppressDerived { get; set; }

        /// <summary>
        /// When set, the Running edge of a canceled <see cref="IMachine.RunAsync"/> is ignored.
        /// Start paused uses that edge to reach <see cref="MachineState.Paused"/> without a slice.
        /// </summary>
        internal bool AcceptPausedOnly { get; set; }

        internal Task CurrentRun => Volatile.Read(ref runTask) ?? Task.CompletedTask;

        internal HostMachineInfo ReadInfo()
        {
            lock (Sync)
            {
                return new HostMachineInfo(
                    Id,
                    Name,
                    ReportedState,
                    PowerCapabilities,
                    NominalClockHz,
                    Throttle,
                    EffectiveClockForInfo(NominalClockHz, Throttle),
                    ReadMeasuredSpeed(),
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

        internal bool IsReported(HostRunState desired)
        {
            lock (Sync)
            {
                return ReportedState == desired;
            }
        }

        internal HostRunState? Translate(MachineState machineState)
        {
            if (machineState == MachineState.Off)
            {
                SuppressDerived = false;
                ReportSleep = false;
                AcceptPausedOnly = false;
                return HostRunState.Off;
            }

            if (SuppressDerived || (AcceptPausedOnly && machineState != MachineState.Paused))
            {
                return null;
            }

            return machineState switch
            {
                MachineState.Running => HostRunState.Running,
                MachineState.Paused when ReportSleep => HostRunState.Sleeping,
                MachineState.Paused => HostRunState.Paused,
                MachineState.Stopped when ReportedState is not (HostRunState.Off or HostRunState.ShuttingDown) => HostRunState.Stopped,
                _ => null,
            };
        }

        internal bool SetReportedState(HostRunState value)
        {
            if (ReportedState == value)
            {
                return false;
            }

            ReportedState = value;
            if (value == HostRunState.Running)
            {
                SeedSpeedWindow();
            }
            else
            {
                speedSample = null;
            }

            for (int index = stateWaits.Count - 1; index >= 0; index--)
            {
                if (stateWaits[index].Desired == value)
                {
                    stateWaits[index].Signal.TrySetResult();
                    stateWaits.RemoveAt(index);
                }
            }

            return true;
        }

        internal bool BuiltFrom(string? overlay)
        {
            return builtFromTemplate && string.Equals(builtOverlay, overlay, StringComparison.Ordinal);
        }

        internal void NoteBuiltOverlay(string? overlay)
        {
            builtFromTemplate = true;
            builtOverlay = overlay;
        }

        internal void ReplaceMachine(IMachine replacement, long clockHz, MachineInfo info)
        {
            Machine.StateChanged -= OnMachineStateChanged;
            Machine.Dispose();
            Machine = replacement;
            replacement.ClockSpeedHz = clockHz;
            replacement.StateChanged += OnMachineStateChanged;
            if (!cpuSummaryFromSpec)
            {
                CpuSummary = info.CpuType;
            }
        }

        internal async Task WaitForReportedAsync(HostRunState desired, CancellationToken cancellationToken)
        {
            StateWait wait;
            lock (Sync)
            {
                if (ReportedState == desired)
                {
                    return;
                }

                wait = new StateWait(desired, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                stateWaits.Add(wait);
            }

            try
            {
                await wait.Signal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (Sync)
                {
                    stateWaits.Remove(wait);
                }
            }
        }

        internal void SeedSpeedWindow()
        {
            speedSample = new SpeedSample(Machine.Now.Value, Stopwatch.GetTimestamp());
        }

        internal void StartRun(Func<IMachine, CancellationToken, Task> start)
        {
            CancellationTokenSource cts = new();
            Interlocked.Increment(ref session.liveRunLoops);
            Volatile.Write(ref runCancellation, cts);
            bool attached = false;
            try
            {
                Task run = Task.Factory.StartNew(
                    () => start(Machine, cts.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default).Unwrap();
                Volatile.Write(ref runTask, run);
                run.ContinueWith(
                    completed => OnRunCompleted(completed, cts),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                attached = true;
            }
            finally
            {
                if (!attached)
                {
                    Interlocked.Decrement(ref session.liveRunLoops);
                    Interlocked.CompareExchange(ref runCancellation, null, cts);
                    cts.Dispose();
                }
            }
        }

        internal void CancelRun()
        {
            CancellationTokenSource? cts = Volatile.Read(ref runCancellation);
            if (cts is null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run task's completion disposes this source after cancel has already been requested.
            }
        }

        internal void AwaitRun()
        {
            Task? run = Volatile.Read(ref runTask);
            if (run is null)
            {
                return;
            }

            if (run.IsFaulted && RunFault is not null)
            {
                _ = run.Exception;
                return;
            }

            run.GetAwaiter().GetResult();
        }

        internal void StopLoop()
        {
            CancelRun();
            if (Machine.State != MachineState.Off)
            {
                Machine.Stop();
            }

            AwaitRun();
        }

        internal void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Machine.StateChanged -= OnMachineStateChanged;
            Exception? error = null;
            try
            {
                StopLoop();
            }
            catch (Exception ex) when (ex is not (
                OutOfMemoryException
                or AccessViolationException
                or InvalidProgramException
                or BadImageFormatException))
            {
                error = ex;
            }
            finally
            {
                Machine.Dispose();
                Lifecycle.Dispose();
            }

            if (error is not null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        private void OnMachineStateChanged(MachineState machineState)
        {
            stateChanged(this, machineState);
        }

        private void OnRunCompleted(Task completed, CancellationTokenSource cts)
        {
            Interlocked.Decrement(ref session.liveRunLoops);
            if (completed.IsFaulted)
            {
                Exception fault = completed.Exception!.GetBaseException();
                session.OnRunFaulted(this, fault);
            }

            Interlocked.CompareExchange(ref runCancellation, null, cts);
            cts.Dispose();
        }

        private double? ReadMeasuredSpeed()
        {
            if (ReportedState != HostRunState.Running || NominalClockHz <= 0)
            {
                speedSample = null;
                return null;
            }

            if (speedSample is not SpeedSample sample)
            {
                return null;
            }

            ulong cycles = Machine.Now.Value;
            long timestamp = Stopwatch.GetTimestamp();
            if (cycles < sample.Cycles)
            {
                speedSample = new SpeedSample(cycles, timestamp);
                return null;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(sample.Timestamp, timestamp);
            if (elapsed < MinimumSpeedSample)
            {
                return null;
            }

            ulong delta = cycles - sample.Cycles;
            speedSample = new SpeedSample(cycles, timestamp);
            return delta / elapsed.TotalSeconds / NominalClockHz;
        }

        private long EffectiveClockForInfo(long nominal, HostThrottle throttle)
        {
            return throttle switch
            {
                HostThrottle.Half => nominal / 2,
                HostThrottle.Normal => nominal,
                HostThrottle.Double => nominal > (long.MaxValue / 2) ? 0 : nominal * 2,
                HostThrottle.Max => 0,
                _ => nominal,
            };
        }

        private readonly record struct SpeedSample(ulong Cycles, long Timestamp);

        private sealed class StateWait(HostRunState desired, TaskCompletionSource signal)
        {
            internal HostRunState Desired { get; } = desired;

            internal TaskCompletionSource Signal { get; } = signal;
        }
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