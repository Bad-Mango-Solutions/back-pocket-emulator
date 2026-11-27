// <copyright file="HostKeyModifiers.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// The modifier snapshot delivered with a key. Bit values match <c>KeyboardModifiers</c> so a later adapter can cast by value.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<HostKeyModifiers>))]
public enum HostKeyModifiers
{
    /// <summary>
    /// No modifiers are down.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shift is down.
    /// </summary>
    Shift = 1 << 0,

    /// <summary>
    /// Control is down.
    /// </summary>
    Control = 1 << 1,

    /// <summary>
    /// Open Apple is down. This is also game-port button 0.
    /// </summary>
    OpenApple = 1 << 2,

    /// <summary>
    /// Closed Apple is down. This is also game-port button 1.
    /// </summary>
    ClosedApple = 1 << 3,

    /// <summary>
    /// Caps Lock is active.
    /// </summary>
    CapsLock = 1 << 4,
}