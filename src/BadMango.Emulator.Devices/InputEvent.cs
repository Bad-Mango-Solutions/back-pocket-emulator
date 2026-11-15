// <copyright file="InputEvent.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// A queued or applied keyboard input event.
/// </summary>
/// <param name="Kind">The event kind.</param>
/// <param name="Key">The key involved in the event.</param>
/// <param name="Modifiers">The modifier snapshot associated with the event.</param>
public readonly record struct InputEvent(InputEventKind Kind, KeyboardKey Key, KeyboardModifiers Modifiers)
{
    /// <summary>
    /// Creates a host key-down event.
    /// </summary>
    /// <param name="key">The key involved.</param>
    /// <param name="modifiers">The modifier snapshot.</param>
    /// <returns>The event.</returns>
    public static InputEvent KeyDownEvent(KeyboardKey key, KeyboardModifiers modifiers = KeyboardModifiers.None) => new(InputEventKind.KeyDown, key, modifiers);

    /// <summary>
    /// Creates a host key-up event.
    /// </summary>
    /// <param name="key">The key involved.</param>
    /// <param name="modifiers">The modifier snapshot.</param>
    /// <returns>The event.</returns>
    public static InputEvent KeyUpEvent(KeyboardKey key, KeyboardModifiers modifiers = KeyboardModifiers.None) => new(InputEventKind.KeyUp, key, modifiers);

    /// <summary>
    /// Creates a typed key-down event.
    /// </summary>
    /// <param name="key">The key involved.</param>
    /// <param name="modifiers">The modifier snapshot.</param>
    /// <returns>The event.</returns>
    public static InputEvent TypedKeyDownEvent(KeyboardKey key, KeyboardModifiers modifiers = KeyboardModifiers.None) => new(InputEventKind.TypedKeyDown, key, modifiers);

    /// <summary>
    /// Creates a typed key-up event.
    /// </summary>
    /// <param name="key">The key involved.</param>
    /// <param name="modifiers">The modifier snapshot.</param>
    /// <returns>The event.</returns>
    public static InputEvent TypedKeyUpEvent(KeyboardKey key, KeyboardModifiers modifiers = KeyboardModifiers.None) => new(InputEventKind.TypedKeyUp, key, modifiers);

    /// <summary>
    /// Creates a cancel-typing event.
    /// </summary>
    /// <param name="key">The key currently being released.</param>
    /// <param name="modifiers">The modifier snapshot.</param>
    /// <returns>The event.</returns>
    public static InputEvent CancelTypingEvent(KeyboardKey key = KeyboardKey.None, KeyboardModifiers modifiers = KeyboardModifiers.None) => new(InputEventKind.CancelTyping, key, modifiers);
}