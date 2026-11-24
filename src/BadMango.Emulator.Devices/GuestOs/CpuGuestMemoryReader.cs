// <copyright file="CpuGuestMemoryReader.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Core.Interfaces.Cpu;

/// <summary>
/// Adapts <see cref="ICpu.Peek8"/> for guest-OS probes.
/// </summary>
/// <remarks>
/// <see cref="Read"/> calls <see cref="ICpu.Peek8"/> only. It does not call
/// <see cref="ICpu.Read8"/> or <see cref="ICpu.Write8"/>, so a probe cannot
/// toggle a soft switch or consume a keystroke.
/// </remarks>
public sealed class CpuGuestMemoryReader : IGuestMemoryReader
{
    private readonly ICpu cpu;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuGuestMemoryReader"/> class.
    /// </summary>
    /// <param name="cpu">The CPU whose debug peek supplies bytes.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="cpu"/> is <see langword="null"/>.</exception>
    public CpuGuestMemoryReader(ICpu cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        this.cpu = cpu;
    }

    /// <inheritdoc />
    public byte Read(Addr address) => this.cpu.Peek8(address);
}