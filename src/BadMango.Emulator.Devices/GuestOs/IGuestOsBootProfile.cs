// <copyright file="IGuestOsBootProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// How one operating system boots, without naming a card or a disk format.
/// </summary>
/// <remarks>
/// <para>
/// #685 implements a profile for DOS 3.3 and one for ProDOS 8. This assembly defines the
/// contract and does not register a profile. Apple Pascal would add a probe and a profile.
/// A layered system such as GEOS registers a probe whose <see cref="IGuestOsProbe.Order"/>
/// is lower than the base OS. Probe ordering already asks that probe first.
/// </para>
/// <para>
/// <see cref="MatchesBootSector"/> looks at the first-stage loader bytes, wherever they were
/// read from. <see cref="MilestoneTraps"/> are the later execution addresses for that OS.
/// Slot firmware at <c>$Cn00</c> is armed for every card that has it, outside this profile.
/// </para>
/// </remarks>
public interface IGuestOsBootProfile
{
    /// <summary>Gets the operating system this profile boots.</summary>
    /// <value>The same id as that OS's <see cref="IGuestOsProbe"/>.</value>
    GuestOsId Id { get; }

    /// <summary>Gets the prefixes of a first-stage loader for this OS.</summary>
    /// <value>One or more signatures. A match is a prefix of the loader bytes.</value>
    IReadOnlyList<ReadOnlyMemory<byte>> BootSectorSignatures { get; }

    /// <summary>Gets the observe-only traps this OS adds after the loader is classified.</summary>
    /// <value>Address and <see cref="BootMilestoneKind"/> pairs. The list may be empty.</value>
    IReadOnlyList<GuestOsBootTrap> MilestoneTraps { get; }

    /// <summary>
    /// Returns whether <paramref name="sector"/> starts with one of <see cref="BootSectorSignatures"/>.
    /// </summary>
    /// <param name="sector">The first-stage loader bytes. The source of those bytes is not part of this contract.</param>
    /// <returns><see langword="true"/> when a signature is a prefix of <paramref name="sector"/>.</returns>
    bool MatchesBootSector(ReadOnlySpan<byte> sector)
    {
        foreach (var signature in this.BootSectorSignatures)
        {
            if (!signature.IsEmpty && sector.StartsWith(signature.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Describes one milestone this profile owns.
    /// </summary>
    /// <param name="kind">The milestone being recorded.</param>
    /// <param name="address">The guest address that was entered.</param>
    /// <returns>A non-empty detail string. The monitor stores it on the milestone.</returns>
    string DescribeMilestone(BootMilestoneKind kind, ushort address) =>
        $"{kind} at ${address:X4}";

    /// <summary>
    /// Names a system call entered at a profile address.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterAddress">The first guest byte after the call instruction.</param>
    /// <returns>A non-empty detail string.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    string DescribeSystemCall(IGuestMemoryReader memory, ushort parameterAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return this.DescribeMilestone(BootMilestoneKind.FirstSystemCall, parameterAddress);
    }
}