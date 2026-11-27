// <copyright file="HostPresentationTiming.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Frame timing from the presentation stream. The frame rate is derived at the publish edge, not in the CPU loop.
/// </summary>
/// <param name="FramesPerSecond">
/// The instantaneous rate between the last two publishes, or <see langword="null"/> until two distinct timestamps exist.
/// </param>
/// <param name="LastSequence">The sequence of the latest sample, or <see langword="null"/> when none has been published.</param>
/// <param name="LastCycle">The cycle of the latest sample, or <see langword="null"/> when none has been published.</param>
public readonly record struct HostPresentationTiming(double? FramesPerSecond, ulong? LastSequence, ulong? LastCycle);