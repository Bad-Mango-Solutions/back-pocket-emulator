// <copyright file="Dos33GuestOsBootProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// DOS 3.3 boot milestones after the shared <c>$0801</c> loader entry.
/// </summary>
/// <remarks>
/// The signature is <see cref="Dos33Layout.BootSector.Signature"/>.
/// Boot 2 is entered at <c>$B700</c> (48K) or <c>$3700</c> (master disk).
/// Cold start is <c>$9D84</c>. The file manager and RWTS are session traps.
/// Slot firmware is not part of this profile.
/// </remarks>
public sealed class Dos33GuestOsBootProfile : IGuestOsBootProfile
{
    private static readonly byte[] Signature = Dos33Layout.BootSector.Signature.ToArray();

    /// <inheritdoc />
    public GuestOsId Id => GuestOsId.Dos33;

    /// <inheritdoc />
    public IReadOnlyList<ReadOnlyMemory<byte>> BootSectorSignatures { get; } = [Signature];

    /// <inheritdoc />
    public IReadOnlyList<GuestOsBootTrap> MilestoneTraps { get; } =
    [
        new(0xB700, BootMilestoneKind.LoaderEntered),
        new(0x3700, BootMilestoneKind.LoaderEntered),
        new(0x9D84, BootMilestoneKind.KernelReady),
    ];

    /// <inheritdoc />
    public IReadOnlyList<GuestOsSessionTrap> SessionTraps { get; } =
    [
        new(Dos33VectorPage.FileManagerTarget, "DOS 3.3 file manager"),
        new(Dos33VectorPage.RwtsTarget, "DOS 3.3 RWTS"),
    ];

    /// <inheritdoc />
    public string DescribeMilestone(BootMilestoneKind kind, ushort address) => kind switch
    {
        BootMilestoneKind.BootSectorLoaded => "DOS 3.3",
        BootMilestoneKind.LoaderEntered => $"DOS 3.3 Boot 2 at ${address:X4}",
        BootMilestoneKind.KernelReady => "DOS 3.3 cold start",
        _ => $"{kind} at ${address:X4}",
    };
}