// <copyright file="WriteRecordingRamTarget.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;

/// <summary>
/// A RAM bus target that records every address written through it.
/// </summary>
/// <remarks>
/// This type is not a <see cref="RamTarget"/>, so the bus never publishes its pages to the
/// array fast path and every store reaches <see cref="Write8"/>. The vector runner uses the
/// record to clear stray writes between cases and to flag writes the case does not expect.
/// </remarks>
internal sealed class WriteRecordingRamTarget : IBusTarget
{
    private readonly Memory<byte> memory;
    private readonly List<ushort> writes = new(8);

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteRecordingRamTarget"/> class.
    /// </summary>
    /// <param name="memorySlice">The 64KB slice this target reads and writes.</param>
    internal WriteRecordingRamTarget(Memory<byte> memorySlice)
    {
        memory = memorySlice;
    }

    /// <inheritdoc />
    public string Name => "wdc65c02-vectors-ram";

    /// <inheritdoc />
    public TargetCaps Capabilities => TargetCaps.SupportsPeek | TargetCaps.SupportsPoke;

    /// <summary>Gets the addresses written since the last <see cref="ResetWrites"/>, in order.</summary>
    internal IReadOnlyList<ushort> Writes => writes;

    /// <inheritdoc />
    public byte Read8(Addr offset, in BusAccess access)
    {
        return memory.Span[(int)offset];
    }

    /// <inheritdoc />
    public void Write8(Addr offset, byte value, in BusAccess access)
    {
        writes.Add((ushort)offset);
        memory.Span[(int)offset] = value;
    }

    /// <summary>Forgets the recorded writes.</summary>
    internal void ResetWrites()
    {
        writes.Clear();
    }
}