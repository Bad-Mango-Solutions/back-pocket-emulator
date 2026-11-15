// <copyright file="VideoPipelineStats.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Snapshot of the video-pipeline counters. <paramref name="LatestSequence"/> is a
/// <see cref="ulong"/>; every other counter is a <see cref="long"/>.
/// </summary>
/// <param name="FramesCaptured">Frames published from the emulator thread.</param>
/// <param name="SwitchEvents">Display-switch changes appended to a frame log.</param>
/// <param name="SwitchLogOverflows">Frames whose switch log dropped events.</param>
/// <param name="Scans">Scans performed.</param>
/// <param name="SignalsGenerated">Signals generated.</param>
/// <param name="FramesProvided">Frames a provider wrote.</param>
/// <param name="CacheHits">Acquires served from a cache.</param>
/// <param name="CaptureErrors">Capture failures on the emulator thread.</param>
/// <param name="ScanErrors">Scan failures on the consumer side.</param>
/// <param name="SignalErrors">Signal-generation failures.</param>
/// <param name="ProvideErrors">Provider failures.</param>
/// <param name="SubscriberErrors">FrameReady handlers that threw.</param>
/// <param name="LeasesOutstanding">Leases that have not been disposed.</param>
/// <param name="PoolExhausted">Acquires that failed because a holder table or the pool was full.</param>
/// <param name="RecorderDroppedFrames">Frames the recorder could not pin. Zero unless a recorder is on.</param>
/// <param name="LatestSequence">The newest published sequence, or 0 when nothing has been published.</param>
public readonly record struct VideoPipelineStats(
    long FramesCaptured,
    long SwitchEvents,
    long SwitchLogOverflows,
    long Scans,
    long SignalsGenerated,
    long FramesProvided,
    long CacheHits,
    long CaptureErrors,
    long ScanErrors,
    long SignalErrors,
    long ProvideErrors,
    long SubscriberErrors,
    long LeasesOutstanding,
    long PoolExhausted,
    long RecorderDroppedFrames,
    ulong LatestSequence);