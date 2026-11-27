// <copyright file="HostInputState.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The input the host has accepted for one machine. The input window reads pocket-key, game-port, and mouse capture from this.
/// </summary>
/// <param name="KeysDown">Keys that are down.</param>
/// <param name="Modifiers">The modifier snapshot from the last key event.</param>
/// <param name="GamePort">Joystick, paddle, and button values.</param>
/// <param name="Mouse">Mouse motion, buttons, and capture.</param>
/// <param name="LastText">The last <see cref="IHostInput.InjectText"/> receipt.</param>
public sealed record HostInputState(
    IReadOnlyList<HostKey> KeysDown,
    HostKeyModifiers Modifiers,
    HostGamePortState GamePort,
    HostMouseState Mouse,
    HostTextReceipt LastText);