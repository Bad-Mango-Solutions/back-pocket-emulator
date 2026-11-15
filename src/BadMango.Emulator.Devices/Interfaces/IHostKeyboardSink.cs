// <copyright file="IHostKeyboardSink.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Represents a machine keyboard that can translate and apply host key events.
/// </summary>
public interface IHostKeyboardSink
{
    /// <summary>
    /// Gets a value indicating whether the sink accepts another typed key.
    /// </summary>
    bool IsReadyForTypedKey { get; }

    /// <summary>
    /// Applies a host key down event to the device.
    /// </summary>
    /// <param name="key">The shared keyboard key.</param>
    /// <param name="modifiers">The full modifier snapshot.</param>
    void OnHostKeyDown(KeyboardKey key, KeyboardModifiers modifiers);

    /// <summary>
    /// Applies a host key up event to the device.
    /// </summary>
    /// <param name="key">The shared keyboard key.</param>
    /// <param name="modifiers">The full modifier snapshot.</param>
    void OnHostKeyUp(KeyboardKey key, KeyboardModifiers modifiers);

    /// <summary>
    /// Attempts to resolve a character to a host keystroke.
    /// </summary>
    /// <param name="c">The character to translate.</param>
    /// <param name="keystroke">The resolved host keystroke if one exists.</param>
    /// <returns><see langword="true"/> when a keystroke is available; otherwise, <see langword="false"/>.</returns>
    bool TryGetKeystroke(char c, out HostKeystroke keystroke);

    /// <summary>
    /// Release any currently latched typed-key state.
    /// </summary>
    void ReleaseAllTypedKeys();
}