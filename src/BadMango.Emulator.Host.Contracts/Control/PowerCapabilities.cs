// <copyright file="PowerCapabilities.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// Which power actions a machine supports. The menu reads this record and does not branch on a model name.
/// </summary>
/// <param name="SoftPower">
/// <see langword="true"/> when the machine has a soft-power circuit. The menu shows Shut Down.
/// When <see langword="false"/>, the menu shows Power Off.
/// </param>
/// <param name="Sleep"><see langword="true"/> when the guest can sleep.</param>
/// <param name="HardReset"><see langword="true"/> when the host can power-cycle this machine.</param>
/// <remarks>
/// <para>
/// Pause, Resume, and Reset are host control. They are available while the machine is on
/// whether or not this record is present. Power On is the only action while the machine is off.
/// </para>
/// <para>
/// When machine info carries no <see cref="PowerCapabilities"/> record, clients use the
/// conservative set: Pause or Resume, Reset, Hard Reset, and Power Off. That set can cut
/// power without assuming a soft-power circuit. Sleep and Shut Down are absent.
/// </para>
/// </remarks>
public sealed record PowerCapabilities(bool SoftPower, bool Sleep, bool HardReset);