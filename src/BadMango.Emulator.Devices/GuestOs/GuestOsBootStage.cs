// <copyright file="GuestOsBootStage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Where a guest boot is, from reset through a ready operating system.
/// </summary>
/// <remarks>
/// <para>
/// The boot monitor (#685) moves this value. The status monitor (#688) displays it.
/// This assembly defines the stages and does not advance them. The stages name
/// firmware and loader addresses, not a particular card.
/// </para>
/// <para>
/// Stages can be skipped. A boot that does not pass through <c>$Cn00</c> or <c>$0801</c>
/// (slotless, non-standard, or a machine restored already booted) may go straight from
/// <see cref="None"/> to <see cref="Loader"/> or <see cref="Ready"/>.
/// <see cref="Ready"/> can be set from <see cref="IGuestOsDetector"/> alone.
/// An unrecognized loader at <c>$0801</c> is <see cref="CustomBoot"/>, a normal terminal state
/// for self-booting software. <see cref="Unknown"/> is only a DOS or ProDOS boot that left the expected path.
/// </para>
/// </remarks>
public enum GuestOsBootStage
{
    /// <summary>Power-on or reset. No boot firmware has been entered yet.</summary>
    None = 0,

    /// <summary>
    /// Boot firmware of any slot card is running at <c>$Cn00</c>
    /// (Disk II, a block-device or SmartPort-style card, or another card).
    /// </summary>
    BootRom = 1,

    /// <summary>
    /// The first-stage loader read from the boot device is running at <c>$0801</c>
    /// (DOS 3.3 track 0 sector 0, or ProDOS block 0).
    /// </summary>
    Boot1 = 2,

    /// <summary>DOS 3.3 Boot 2, or the ProDOS loader, including language-card relocation.</summary>
    Loader = 3,

    /// <summary>The operating system has booted and is accepting commands.</summary>
    Ready = 4,

    /// <summary>A recognized DOS or ProDOS boot that left the expected path.</summary>
    Unknown = 5,

    /// <summary>
    /// The first-stage loader at <c>$0801</c> does not match the DOS 3.3 or ProDOS signature.
    /// The guest is running its own loader. This is a normal terminal state, not an error.
    /// </summary>
    CustomBoot = 6,
}