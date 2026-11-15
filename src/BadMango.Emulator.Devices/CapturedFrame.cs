// <copyright file="CapturedFrame.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// One captured frame: machine state plus the switch log for that frame window.
/// </summary>
/// <typeparam name="TState">The machine-specific snapshot. A reference type, owned by the slot.</typeparam>
/// <remarks>
/// <para>
/// A sealed class, not a record: it owns the slot's event buffers, and value equality
/// would compare those array references without meaning anything.
/// </para>
/// <para>
/// <see cref="SwitchLog"/> and <see cref="WriteLog"/> are computed on every read from a
/// preallocated array plus a count. No span is stored. The frame, and anything read from
/// it, is valid only while the caller holds the slot.
/// </para>
/// <para>
/// <see cref="WriteLog"/> is always empty. No write log is installed in v1.
/// Published sequences start at 1; <see cref="Sequence"/> 0 means none yet.
/// </para>
/// </remarks>
public sealed class CapturedFrame<TState>
    where TState : class
{
    private static readonly VideoWriteEvent[] EmptyWrites = [];

    private readonly DisplaySwitchEvent[] switchEvents;
    private readonly int switchCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturedFrame{TState}"/> class.
    /// </summary>
    /// <param name="sequence">The frame sequence. Published frames start at 1; 0 means none yet.</param>
    /// <param name="cycle">The VBlank-start cycle, or the cycle of an explicit frame request.</param>
    /// <param name="frameStartCycle">The cycle the frame window opened on.</param>
    /// <param name="state">The machine-specific snapshot.</param>
    /// <param name="switchEvents">The slot's preallocated switch-log array. Not copied.</param>
    /// <param name="switchCount">How many leading entries of <paramref name="switchEvents"/> are live.</param>
    /// <param name="logOverflowed"><see langword="true"/> when further switch events were dropped.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="state"/> or <paramref name="switchEvents"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="switchCount"/> is negative or past the end of <paramref name="switchEvents"/>.
    /// </exception>
    public CapturedFrame(
        ulong sequence,
        ulong cycle,
        ulong frameStartCycle,
        TState state,
        DisplaySwitchEvent[] switchEvents,
        int switchCount,
        bool logOverflowed)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(switchEvents);
        ArgumentOutOfRangeException.ThrowIfNegative(switchCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(switchCount, switchEvents.Length);

        Sequence = sequence;
        Cycle = cycle;
        FrameStartCycle = frameStartCycle;
        State = state;
        this.switchEvents = switchEvents;
        this.switchCount = switchCount;
        LogOverflowed = logOverflowed;
    }

    /// <summary>
    /// Gets the frame sequence.
    /// </summary>
    /// <value>0 means none yet. Published frames start at 1.</value>
    public ulong Sequence { get; }

    /// <summary>
    /// Gets the cycle the frame was captured at.
    /// </summary>
    /// <value>The VBlank-start cycle, or the cycle of an explicit frame request.</value>
    public ulong Cycle { get; }

    /// <summary>
    /// Gets the cycle the frame window opened on.
    /// </summary>
    /// <value>The previous VBlank start.</value>
    public ulong FrameStartCycle { get; }

    /// <summary>
    /// Gets the machine-specific snapshot: memory, glyphs, and the switches at the window start.
    /// </summary>
    /// <value>The captured state.</value>
    public TState State { get; }

    /// <summary>
    /// Gets the switch changes recorded in this frame, computed from the slot array and the live count.
    /// </summary>
    /// <value>The live prefix of the preallocated switch log. A new span on every read.</value>
    public ReadOnlySpan<DisplaySwitchEvent> SwitchLog => switchEvents.AsSpan(0, switchCount);

    /// <summary>
    /// Gets a value indicating whether the switch log dropped events after it filled.
    /// </summary>
    /// <value><see langword="true"/> when the log overflowed.</value>
    public bool LogOverflowed { get; }

    /// <summary>
    /// Gets the reserved video-write log. Always empty in v1.
    /// </summary>
    /// <value>An empty span, computed on every read.</value>
    public ReadOnlySpan<VideoWriteEvent> WriteLog => EmptyWrites.AsSpan(0, 0);

    /// <summary>
    /// Gets a value indicating whether the reserved write log overflowed. Always <see langword="false"/> in v1.
    /// </summary>
    /// <value><see langword="false"/>.</value>
    public bool WriteLogOverflowed => false;
}