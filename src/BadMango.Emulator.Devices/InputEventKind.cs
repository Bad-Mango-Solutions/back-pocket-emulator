// <copyright file="InputEventKind.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Enumerates the kinds of events recorded in the keyboard input journal.
/// </summary>
public enum InputEventKind
{
    /// <summary>
    /// A host key went down.
    /// </summary>
    KeyDown,

    /// <summary>
    /// A host key went up.
    /// </summary>
    KeyUp,

    /// <summary>
    /// A typed key went down.
    /// </summary>
    TypedKeyDown,

    /// <summary>
    /// A typed key went up.
    /// </summary>
    TypedKeyUp,

    /// <summary>
    /// Typing was cancelled.
    /// </summary>
    CancelTyping,
}