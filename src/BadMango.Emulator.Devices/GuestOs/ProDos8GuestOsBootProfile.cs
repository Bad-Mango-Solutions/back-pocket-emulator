// <copyright file="ProDos8GuestOsBootProfile.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Storage.FileSystems;

/// <summary>
/// ProDOS 8 boot milestones after the shared <c>$0801</c> loader entry.
/// </summary>
/// <remarks>
/// <para>
/// The signature is <see cref="ProdosLayout.BootBlock.Signature"/>.
/// The first entry at <c>$2000</c> is the loader. The second entry at <c>$2000</c>,
/// or the first entry at <c>$BF00</c>, means the kernel and the system program are up.
/// Later <c>$BF00</c> entries are MLI calls. The monitor owns that trap, scopes it to the guest OS
/// session, and fans the calls out. A clock driver registers <c>$BF06</c> with the same scope.
/// </para>
/// <para>Slot firmware is not part of this profile.</para>
/// </remarks>
public sealed class ProDos8GuestOsBootProfile : IGuestOsBootProfile
{
    private static readonly byte[] Signature = ProdosLayout.BootBlock.Signature.ToArray();

    /// <inheritdoc />
    public GuestOsId Id => GuestOsId.ProDos8;

    /// <inheritdoc />
    public IReadOnlyList<ReadOnlyMemory<byte>> BootSectorSignatures { get; } = [Signature];

    /// <inheritdoc />
    public IReadOnlyList<GuestOsBootTrap> MilestoneTraps { get; } =
    [
        new(0x2000, BootMilestoneKind.LoaderEntered),
        new(0x2000, BootMilestoneKind.KernelReady),
        new(0x2000, BootMilestoneKind.SystemProgramStarted),
        new(ProDosGlobalPage.MliEntry, BootMilestoneKind.KernelReady),
        new(ProDosGlobalPage.MliEntry, BootMilestoneKind.SystemProgramStarted),
        new(ProDosGlobalPage.MliEntry, BootMilestoneKind.FirstSystemCall),
    ];

    /// <inheritdoc />
    public string DescribeMilestone(BootMilestoneKind kind, ushort address) => kind switch
    {
        BootMilestoneKind.BootSectorLoaded => "ProDOS 8",
        BootMilestoneKind.LoaderEntered => "ProDOS loader",
        BootMilestoneKind.KernelReady => "ProDOS kernel",
        BootMilestoneKind.SystemProgramStarted => "ProDOS system program",
        _ => $"{kind} at ${address:X4}",
    };

    /// <inheritdoc />
    public string DescribeSystemCall(IGuestMemoryReader memory, ushort parameterAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        byte command = memory.Read(parameterAddress);
        if (!ProDosMliCall.TryGetName(command, out string? name))
        {
            name = "CALL";
        }

        return $"MLI ${command:X2} {name}";
    }
}