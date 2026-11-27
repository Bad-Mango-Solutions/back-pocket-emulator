// <copyright file="LoopbackHostSession.Presentation.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using System.Diagnostics;

using BadMango.Emulator.Host.Contracts;

/// <content>
/// Presentation-plane members of <see cref="LoopbackHostSession"/>.
/// </content>
public sealed partial class LoopbackHostSession
{
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
            if (subscription.IsDisposed)
            {
                return;
            }

            subscription.MarkDisposed();
            subscription.Machine.Subscriptions.Remove(subscription);
        }
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
}