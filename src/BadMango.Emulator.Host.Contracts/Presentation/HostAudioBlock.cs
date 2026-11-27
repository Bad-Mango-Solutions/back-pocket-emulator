// <copyright file="HostAudioBlock.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One cycle-stamped PCM block. The byte length matches the machine's <see cref="HostAudioFormat"/>.
/// </summary>
/// <param name="Cycle">The machine cycle of the first sample.</param>
/// <param name="SampleCount">The number of frames (one frame is every channel).</param>
/// <param name="Pcm">Interleaved PCM. The publisher must not mutate the buffer after publish.</param>
public sealed record HostAudioBlock(ulong Cycle, int SampleCount, ReadOnlyMemory<byte> Pcm);