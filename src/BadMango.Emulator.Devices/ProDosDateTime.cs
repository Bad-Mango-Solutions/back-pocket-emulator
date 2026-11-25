// <copyright file="ProDosDateTime.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// ProDOS <c>DATE</c> and <c>TIME</c> bytes for one civil time.
/// </summary>
/// <remarks>
/// <para>
/// <c>DATE</c> is <c>(year &lt;&lt; 9) | (month &lt;&lt; 5) | day</c> at <c>$BF90</c>.
/// The year byte is <c>year % 100</c>: 40–99 reads as 1940–1999 and 0–39 as 2000–2039.
/// <c>TIME</c> is the minute at <c>$BF92</c> and the hour at <c>$BF93</c>. Unused high bits are zero.
/// Nothing is written at <c>$0200</c>.
/// </para>
/// </remarks>
/// <param name="DateLo">Low byte of <c>DATE</c>, stored at <c>$BF90</c>.</param>
/// <param name="DateHi">High byte of <c>DATE</c>, stored at <c>$BF91</c>.</param>
/// <param name="Minute"><c>TIME</c> minute at <c>$BF92</c>. Bits 7–6 are zero.</param>
/// <param name="Hour"><c>TIME</c> hour at <c>$BF93</c>. Bits 7–5 are zero.</param>
public readonly record struct ProDosDateTime(byte DateLo, byte DateHi, byte Minute, byte Hour)
{
    /// <summary>
    /// Packs <paramref name="time"/> into the ProDOS global-page layout.
    /// </summary>
    /// <param name="time">The civil time. The year contribution is <c>Year % 100</c>.</param>
    /// <returns>The four bytes ProDOS stores at <c>$BF90</c>–<c>$BF93</c>.</returns>
    public static ProDosDateTime Pack(DateTime time)
    {
        int year = time.Year % 100;
        int packed = (year << 9) | (time.Month << 5) | time.Day;
        return new(
            (byte)packed,
            (byte)(packed >> 8),
            (byte)(time.Minute & 0x3F),
            (byte)(time.Hour & 0x1F));
    }

    /// <summary>
    /// Writes <see cref="Pack"/> at <c>$BF90</c>–<c>$BF93</c>.
    /// </summary>
    /// <param name="cpu">The CPU whose main memory receives the bytes.</param>
    /// <param name="time">The civil time to store.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="cpu"/> is <see langword="null"/>.</exception>
    public static void Write(ICpu cpu, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ProDosDateTime packed = Pack(time);
        cpu.Write8(ProDosGlobalPage.DateLo, packed.DateLo);
        cpu.Write8(ProDosGlobalPage.DateLo + 1u, packed.DateHi);
        cpu.Write8(ProDosGlobalPage.TimeLo, packed.Minute);
        cpu.Write8(ProDosGlobalPage.TimeLo + 1u, packed.Hour);
    }
}