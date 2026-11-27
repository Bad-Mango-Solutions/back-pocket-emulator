// <copyright file="HostAudioFormat.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The line-out PCM format. The host describes the stream. It does not call <c>IAudioOutput.Play</c>.
/// </summary>
/// <param name="SampleRateHz">The sample rate in Hz.</param>
/// <param name="ChannelCount">1 or 2.</param>
/// <param name="BitsPerSample">8 or 16.</param>
/// <param name="Signed"><see langword="true"/> for signed samples. 16-bit blocks are signed.</param>
/// <param name="LittleEndian"><see langword="true"/> when 16-bit samples are little-endian.</param>
/// <remarks>
/// This is the #691 shape: a format descriptor plus cycle-stamped blocks (<see cref="HostAudioBlock"/>).
/// OpenAL or Web Audio is a client sink. The loopback default is 44,100 Hz, mono, signed 16-bit little-endian.
/// </remarks>
public sealed record HostAudioFormat(
    int SampleRateHz,
    int ChannelCount,
    int BitsPerSample,
    bool Signed,
    bool LittleEndian);