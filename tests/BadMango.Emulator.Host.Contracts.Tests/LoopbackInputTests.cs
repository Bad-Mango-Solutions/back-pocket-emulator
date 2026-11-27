// <copyright file="LoopbackInputTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Tests;

/// <summary>
/// Keyboard, joystick, paddle, and mouse injection. Events are recorded at the call, not on a machine thread.
/// </summary>
[TestFixture]
public class LoopbackInputTests
{
    /// <summary>
    /// Key, joystick, paddle, button, and mouse events are visible from the client thread that sent them.
    /// </summary>
    [Test]
    public void KeyJoystickMouse_AreAppliedOnTheCallingThread()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");
        IHostInput input = session.Input;

        input.KeyDown(id, HostKey.A, HostKeyModifiers.Shift | HostKeyModifiers.Control);
        Assert.That(input.GetInputState(id).Modifiers, Is.EqualTo(HostKeyModifiers.Shift | HostKeyModifiers.Control));
        input.SetJoystick(id, 0, 1f, -1f);
        input.SetPaddle(id, 2, 200);
        input.SetButton(id, 0, true);
        input.MoveMouse(id, HostMouseMotionKind.Relative, 3f, -2f);
        input.MoveMouse(id, HostMouseMotionKind.Absolute, 2f, -1f);
        input.SetMouseButton(id, HostMouseButton.Right, true);
        input.CapturePointer(id, "window-a");
        HostTextReceipt receipt = input.InjectText(id, "Hi!");

        HostInputState state = input.GetInputState(id);
        IReadOnlyList<HostInputEntry> log = ((ILoopbackMachineEdge)session).DrainInput(id);

        Assert.Multiple(() =>
        {
            Assert.That(state.KeysDown, Does.Contain(HostKey.A));
            Assert.That(state.GamePort.Joystick0X, Is.EqualTo(1f));
            Assert.That(state.GamePort.Joystick0Y, Is.EqualTo(-1f));
            Assert.That(state.GamePort.Paddle0, Is.EqualTo(255));
            Assert.That(state.GamePort.Paddle1, Is.EqualTo(0));
            Assert.That(state.GamePort.Paddle2, Is.EqualTo(200));
            Assert.That(state.GamePort.Button0, Is.True);
            Assert.That(state.Mouse.MotionKind, Is.EqualTo(HostMouseMotionKind.Absolute));
            Assert.That(state.Mouse.X, Is.EqualTo(1f));
            Assert.That(state.Mouse.Y, Is.EqualTo(0f));
            Assert.That(state.Mouse.Right, Is.True);
            Assert.That(state.Mouse.CaptureWindowId, Is.EqualTo("window-a"));
            Assert.That(receipt.Accepted, Is.EqualTo(2));
            Assert.That(receipt.Skipped, Is.EqualTo(1));
            Assert.That(log.Any(entry => entry.Kind == HostInputKind.Key && entry.Key == HostKey.A), Is.True);
            Assert.That(log.Any(entry => entry.Kind == HostInputKind.Joystick), Is.True);
            Assert.That(log.Any(entry => entry.Kind == HostInputKind.MouseMotion), Is.True);
            Assert.That(log.Any(entry => entry.Kind == HostInputKind.PointerCapture && entry.IsDown), Is.True);
        });

        input.KeyUp(id, HostKey.A, HostKeyModifiers.None);
        input.ReleasePointer(id, "window-a");
        HostInputState released = input.GetInputState(id);
        Assert.Multiple(() =>
        {
            Assert.That(released.KeysDown, Does.Not.Contain(HostKey.A));
            Assert.That(released.Mouse.CaptureWindowId, Is.Null);
        });
    }

    /// <summary>
    /// A client thread other than the caller can inject a key. There is no machine thread to marshal onto.
    /// </summary>
    /// <returns>The test task.</returns>
    [Test]
    public async Task KeyDown_FromAnotherThread_IsVisible()
    {
        using LoopbackHostSession session = HostSessions.Open();
        MachineId id = HostSessions.Add(session, "Workshop");

        await Task.Run(() => session.Input.KeyDown(id, HostKey.Return, HostKeyModifiers.None));

        Assert.That(session.Input.GetInputState(id).KeysDown, Is.EqualTo(new[] { HostKey.Return }));
    }
}