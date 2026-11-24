// <copyright file="IGuestMemoryReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Side-effect-free view of guest memory for on-demand OS probes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CpuGuestMemoryReader"/> adapts <see cref="BadMango.Emulator.Core.Interfaces.Cpu.ICpu.Peek8"/>.
/// Peek does not flip soft switches, clear keyboard strobes, or run device handlers
/// that a normal opcode fetch would. Tests supply a byte-array reader instead of a CPU.
/// </para>
/// <para>
/// The reader sees whatever bank the machine currently has mapped. It does not
/// change language-card or auxiliary-memory switches in order to look somewhere else.
/// </para>
/// </remarks>
public interface IGuestMemoryReader
{
    /// <summary>
    /// Reads one byte without soft-switch side effects.
    /// </summary>
    /// <param name="address">The guest address to read.</param>
    /// <returns>The byte at <paramref name="address"/>.</returns>
    byte Read(Addr address);
}