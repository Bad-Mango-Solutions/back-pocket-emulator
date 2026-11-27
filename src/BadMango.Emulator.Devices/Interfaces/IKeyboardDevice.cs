// <copyright file="IKeyboardDevice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Bus.Interfaces;

/// <summary>
/// Keyboard device interface for injecting shared <see cref="KeyboardKey"/> input into the emulated system.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines the device-facing API for the keyboard, allowing input sources to inject
/// key events described by the shared Apple II Extended Keyboard II key model.
/// </para>
/// <para>
/// The Apple II keyboard presents a simple interface to software:
/// </para>
/// <list type="bullet">
/// <item><description>$C000: Keyboard data (bit 7 = strobe, bits 6-0 = ASCII)</description></item>
/// <item><description>$C010: Any-key-down in bit 7, latched ASCII in bits 0–6, and the strobe clears on a read or write</description></item>
/// </list>
/// </remarks>
public interface IKeyboardDevice : IMotherboardDevice, IResetSource
{
    /// <summary>
    /// Gets a value indicating whether a key is currently held down.
    /// </summary>
    /// <value><see langword="true"/> if a key is pressed; otherwise, <see langword="false"/>.</value>
    bool HasKeyDown { get; }

    /// <summary>
    /// Gets the current key data register value (bit 7 = strobe).
    /// </summary>
    /// <value>The keyboard data register value with high bit set if strobe is active.</value>
    byte KeyData { get; }

    /// <summary>
    /// Gets the current modifier key state.
    /// </summary>
    /// <value>The active modifier keys.</value>
    KeyboardModifiers Modifiers { get; }

    /// <summary>
    /// Gets the translator that turns shared <see cref="KeyboardKey"/> values into this keyboard's key codes.
    /// </summary>
    /// <value>The key translator owned by this keyboard.</value>
    /// <remarks>
    /// The translator is exposed on the device. Input sources such as the video window map host input
    /// to the shared <see cref="KeyboardKey"/> model, then use this translator to turn that key and the
    /// active modifiers into a key code before calling <see cref="KeyDown"/>, so they never construct
    /// or choose a translator themselves. This is interim: the planned per-machine keyboard input
    /// layer will take over host key translation, accept host keys directly, and keep the translator
    /// private to the keyboard.
    /// </remarks>
    IKeyTranslator Translator { get; }

    /// <summary>
    /// Injects a key press from the host.
    /// </summary>
    /// <param name="asciiCode">The ASCII code of the key (high bit cleared).</param>
    /// <remarks>
    /// Sets the keyboard data register to the specified value with the high bit
    /// (strobe) set, and marks a key as being held down.
    /// </remarks>
    void KeyDown(byte asciiCode);

    /// <summary>
    /// Releases the current key.
    /// </summary>
    /// <remarks>
    /// Clears the key-down state but leaves the keyboard data register unchanged
    /// until the strobe is cleared by reading $C010.
    /// </remarks>
    void KeyUp();

    /// <summary>
    /// Sets the modifier key state.
    /// </summary>
    /// <param name="modifiers">The new modifier state.</param>
    void SetModifiers(KeyboardModifiers modifiers);
}