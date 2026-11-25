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
/// The usual order is <see cref="None"/>, <see cref="Init"/>, <see cref="BootDeviceSelected"/>,
/// <see cref="BootRom"/>, <see cref="Boot1"/>, <see cref="Loader"/>, <see cref="Ready"/>.
/// Stages can be skipped. A boot that does not pass through <c>$Cn00</c> or <c>$0801</c>
/// (slotless, non-standard, or a machine restored already booted) may go straight from
/// <see cref="None"/> to <see cref="Loader"/> or <see cref="Ready"/>.
/// <see cref="Ready"/> can be set from <see cref="IGuestOsDetector"/> alone.
/// An unrecognized loader at <c>$0801</c> is <see cref="CustomBoot"/>, a normal terminal state
/// for self-booting software. <see cref="Unknown"/> is only a DOS or ProDOS boot that left the expected path.
/// <see cref="NoBootDevice"/> is the other terminal: init finished and the ROM is waiting for a key.
/// </para>
/// <para>
/// Numeric values of the stages that shipped with #685 stay fixed
/// (<see cref="None"/> is 0 through <see cref="CustomBoot"/> is 6). The status monitor,
/// MCP <c>get_guest_os</c>, and <c>get_boot_log</c> store the name, not the number.
/// Stages added after that set use the next free values so a stored integer from the
/// original set still names the same stage. The source order above is the boot progression.
/// </para>
/// </remarks>
public enum GuestOsBootStage
{
    /// <summary>Power-on or reset. No reset handler and no boot firmware has been entered yet.</summary>
    None = 0,

    /// <summary>
    /// The CPU took the reset vector and the system ROM reset handler is running.
    /// </summary>
    /// <remarks>
    /// The <see cref="BootMilestoneKind.RomInitEntered"/> detail names the vector target read from
    /// <c>$FFFC</c>/<c>$FFFD</c> and whether the reset was cold or warm. A cold start is power-on,
    /// a hard reset, or an Open-Apple reset. A warm reset is logged but does not by itself move
    /// the stage back here.
    /// </remarks>
    Init = 7,

    /// <summary>A boot device was chosen: slot, card, and how (autostart, user, or Open-Apple).</summary>
    /// <remarks>
    /// Recorded on the first transfer to a slot's <c>$Cn00</c> after <see cref="Init"/>, in the same
    /// entry as <see cref="BootRom"/>. The milestone detail carries the method token
    /// <c>autostart</c>, <c>user</c>, or <c>open-apple</c>.
    /// </remarks>
    BootDeviceSelected = 8,

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

    /// <summary>
    /// Init finished without entering any boot firmware. The ROM is at its keyboard wait. Terminal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1 treats two consecutive Apple II keyboard-data reads (<c>LDA $C000</c> or <c>BIT $C000</c>)
    /// from a ROM mapping, while <see cref="Init"/> is current and no slot firmware has run, as that
    /// wait. It does not hardcode an Apple IIe entry such as <c>$FD0C</c> or <c>$FA62</c>.
    /// </para>
    /// <para>
    /// A ROM that prompts through some other register, or that never reads <c>$C000</c>, does not
    /// reach this stage. A ROM that reads <c>$C000</c> twice before it jumps to slot firmware can
    /// reach it early. Both are limits of this keyboard-register signal.
    /// </para>
    /// </remarks>
    NoBootDevice = 9,
}