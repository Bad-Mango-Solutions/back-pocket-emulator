// <copyright file="HostInputEntry.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// One input event the host has accepted. A later machine binding drains these onto <c>IMachineDispatcher</c> once per event, not once per cycle.
/// </summary>
/// <param name="Kind">Which fields are meaningful.</param>
/// <param name="Key">The key, when <paramref name="Kind"/> is <see cref="HostInputKind.Key"/>.</param>
/// <param name="Modifiers">The modifier snapshot for a key.</param>
/// <param name="IsDown"><see langword="true"/> when a key, button, or capture is pressed or taken.</param>
/// <param name="Index">Joystick, paddle, or game-button index.</param>
/// <param name="X">Joystick X, or mouse X.</param>
/// <param name="Y">Joystick Y, or mouse Y.</param>
/// <param name="MotionKind">The mouse motion kind.</param>
/// <param name="MouseButton">The mouse button.</param>
/// <param name="WindowId">The capture window, or <see langword="null"/>.</param>
public sealed record HostInputEntry(
    HostInputKind Kind,
    HostKey Key,
    HostKeyModifiers Modifiers,
    bool IsDown,
    int Index,
    float X,
    float Y,
    HostMouseMotionKind MotionKind,
    HostMouseButton MouseButton,
    string? WindowId);