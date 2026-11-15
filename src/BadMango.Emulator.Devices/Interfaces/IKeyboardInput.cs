// <copyright file="IKeyboardInput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Receives host-originated keyboard input and typed-text requests and applies them to a machine.
/// </summary>
public interface IKeyboardInput
{
    /// <summary>
    /// Gets a value indicating whether text is currently being typed.
    /// </summary>
    bool IsTyping { get; }

    /// <summary>
    /// Gets the journal of applied input events.
    /// </summary>
    IKeyboardInputJournal Journal { get; }

    /// <summary>
    /// Signals that a host key is pressed.
    /// </summary>
    /// <param name="key">The shared keyboard key.</param>
    /// <param name="modifiers">The full modifier snapshot associated with this event.</param>
    void KeyDown(KeyboardKey key, KeyboardModifiers modifiers);

    /// <summary>
    /// Signals that a host key is released.
    /// </summary>
    /// <param name="key">The shared keyboard key.</param>
    /// <param name="modifiers">The full modifier snapshot associated with this event.</param>
    void KeyUp(KeyboardKey key, KeyboardModifiers modifiers);

    /// <summary>
    /// Queues text to be typed as scheduled keyboard input.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="options">Typing options.</param>
    /// <returns>A receipt describing accepted, skipped and rejected characters.</returns>
    TypeTextReceipt TypeText(string text, TypeTextOptions? options = null);

    /// <summary>
    /// Cancels any pending typed-text work.
    /// </summary>
    void CancelTyping();
}