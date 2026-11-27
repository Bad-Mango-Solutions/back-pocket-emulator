// <copyright file="IHostInput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// The input plane. Keyboard, joystick, paddles, buttons, and mouse, including capture.
/// </summary>
/// <remarks>
/// <para>
/// Each call records one event and returns. It does not touch a CPU loop. A later binding
/// posts the recorded event through <c>IMachineDispatcher</c> at an instruction boundary.
/// The AppleMouse card is not implemented here. The events are, so that card does not revise the API.
/// </para>
/// <para>
/// <see cref="SetJoystick"/> also writes the mapped paddles: index 0 writes paddles 0 and 1,
/// index 1 writes paddles 2 and 3. Axis -1 becomes paddle 0 and axis 1 becomes paddle 255.
/// </para>
/// </remarks>
public interface IHostInput
{
    /// <summary>
    /// Records a key down.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="key">The key. <see cref="HostKey.None"/> is rejected.</param>
    /// <param name="modifiers">The full modifier snapshot.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="key"/> is <see cref="HostKey.None"/>.</exception>
    void KeyDown(MachineId machineId, HostKey key, HostKeyModifiers modifiers);

    /// <summary>
    /// Records a key up.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The full modifier snapshot.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="key"/> is <see cref="HostKey.None"/>.</exception>
    void KeyUp(MachineId machineId, HostKey key, HostKeyModifiers modifiers);

    /// <summary>
    /// Turns text into key down and key up pairs. Paste uses this.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="text">The text. Letters, digits, space, and CR or LF are accepted.</param>
    /// <returns>How many characters were accepted and skipped.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    HostTextReceipt InjectText(MachineId machineId, string text);

    /// <summary>
    /// Sets a joystick. Values outside -1 to 1 are clamped. Index 0 or 1.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="index">0 or 1.</param>
    /// <param name="x">X axis, -1 (left) to 1 (right).</param>
    /// <param name="y">Y axis, -1 (up) to 1 (down).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is not 0 or 1, or an axis is not finite.</exception>
    void SetJoystick(MachineId machineId, int index, float x, float y);

    /// <summary>
    /// Sets one paddle, 0–3, to a position 0–255.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="paddle">The paddle index, 0–3.</param>
    /// <param name="position">The position, 0–255.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="paddle"/> is outside 0–3.</exception>
    void SetPaddle(MachineId machineId, int paddle, byte position);

    /// <summary>
    /// Sets a game-port button, 0–3.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="button">The button index, 0–3.</param>
    /// <param name="pressed">Whether the button is down.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="button"/> is outside 0–3.</exception>
    void SetButton(MachineId machineId, int button, bool pressed);

    /// <summary>
    /// Moves the mouse. Absolute positions are clamped to 0–1.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="kind">Relative deltas or an absolute position.</param>
    /// <param name="x">The horizontal delta, or the absolute position from 0 to 1.</param>
    /// <param name="y">The vertical delta, or the absolute position from 0 to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an axis is not finite.</exception>
    void MoveMouse(MachineId machineId, HostMouseMotionKind kind, float x, float y);

    /// <summary>
    /// Sets a mouse button.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="button">The button.</param>
    /// <param name="pressed">Whether it is down.</param>
    void SetMouseButton(MachineId machineId, HostMouseButton button, bool pressed);

    /// <summary>
    /// Captures the pointer for one window. A later capture replaces the owner.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="windowId">The window.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="windowId"/> is empty.</exception>
    void CapturePointer(MachineId machineId, string windowId);

    /// <summary>
    /// Releases the pointer when <paramref name="windowId"/> holds it. A release from another window is ignored.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="windowId">The window.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="windowId"/> is empty.</exception>
    void ReleasePointer(MachineId machineId, string windowId);

    /// <summary>
    /// Gets the input the host has accepted.
    /// </summary>
    /// <param name="machineId">The machine.</param>
    /// <returns>The current keys, game port, and mouse.</returns>
    HostInputState GetInputState(MachineId machineId);
}