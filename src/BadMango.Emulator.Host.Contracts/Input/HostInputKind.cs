// <copyright file="HostInputKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// The kind of one recorded input event.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HostInputKind>))]
public enum HostInputKind
{
    /// <summary>
    /// A key down or key up.
    /// </summary>
    Key,

    /// <summary>
    /// A joystick position.
    /// </summary>
    Joystick,

    /// <summary>
    /// A paddle position.
    /// </summary>
    Paddle,

    /// <summary>
    /// A game-port button.
    /// </summary>
    GameButton,

    /// <summary>
    /// A mouse move.
    /// </summary>
    MouseMotion,

    /// <summary>
    /// A mouse button.
    /// </summary>
    MouseButton,

    /// <summary>
    /// Pointer capture or release.
    /// </summary>
    PointerCapture,
}