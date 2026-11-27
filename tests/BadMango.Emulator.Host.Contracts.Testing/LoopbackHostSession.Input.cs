// <copyright file="LoopbackHostSession.Input.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <content>
/// Input-plane members of <see cref="LoopbackHostSession"/>.
/// </content>
public sealed partial class LoopbackHostSession
{
    /// <inheritdoc />
    public void KeyDown(MachineId machineId, HostKey key, HostKeyModifiers modifiers)
    {
        RequireKey(key);
        Update(machineId, machine => ApplyKey(machine, key, modifiers, down: true));
    }

    /// <inheritdoc />
    public void KeyUp(MachineId machineId, HostKey key, HostKeyModifiers modifiers)
    {
        RequireKey(key);
        Update(machineId, machine => ApplyKey(machine, key, modifiers, down: false));
    }

    /// <inheritdoc />
    public HostTextReceipt InjectText(MachineId machineId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        HostTextReceipt receipt = default;
        Update(machineId, machine =>
        {
            int accepted = 0;
            int skipped = 0;
            foreach (char character in text)
            {
                if (!TryMapChar(character, out HostKey key, out HostKeyModifiers modifiers))
                {
                    skipped++;
                    continue;
                }

                ApplyKey(machine, key, modifiers, down: true);
                ApplyKey(machine, key, modifiers, down: false);
                accepted++;
            }

            receipt = new HostTextReceipt(accepted, skipped);
            machine.LastText = receipt;
        });

        return receipt;
    }

    /// <inheritdoc />
    public void SetJoystick(MachineId machineId, int index, float x, float y)
    {
        if (index is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Joystick index must be 0 or 1.");
        }

        float clampedX = ClampAxis(x, nameof(x));
        float clampedY = ClampAxis(y, nameof(y));
        Update(machineId, machine =>
        {
            machine.JoystickX[index] = clampedX;
            machine.JoystickY[index] = clampedY;
            machine.Paddles[index * 2] = ToPaddle(clampedX);
            machine.Paddles[(index * 2) + 1] = ToPaddle(clampedY);
            AddLog(machine, Entry(HostInputKind.Joystick, index: index, x: clampedX, y: clampedY));
        });
    }

    /// <inheritdoc />
    public void SetPaddle(MachineId machineId, int paddle, byte position)
    {
        if (paddle is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(paddle), paddle, "Paddle index must be 0 through 3.");
        }

        Update(machineId, machine =>
        {
            machine.Paddles[paddle] = position;
            AddLog(machine, Entry(HostInputKind.Paddle, index: paddle, x: position));
        });
    }

    /// <inheritdoc />
    public void SetButton(MachineId machineId, int button, bool pressed)
    {
        if (button is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "Button index must be 0 through 3.");
        }

        Update(machineId, machine =>
        {
            machine.Buttons[button] = pressed;
            AddLog(machine, Entry(HostInputKind.GameButton, index: button, down: pressed));
        });
    }

    /// <inheritdoc />
    public void MoveMouse(MachineId machineId, HostMouseMotionKind kind, float x, float y)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The motion kind is not defined.");
        }

        float finiteX = RequireFinite(x, nameof(x));
        float finiteY = RequireFinite(y, nameof(y));
        if (kind == HostMouseMotionKind.Absolute)
        {
            finiteX = Math.Clamp(finiteX, 0f, 1f);
            finiteY = Math.Clamp(finiteY, 0f, 1f);
        }

        float storedX = finiteX;
        float storedY = finiteY;
        Update(machineId, machine =>
        {
            machine.MouseKind = kind;
            machine.MouseX = storedX;
            machine.MouseY = storedY;
            AddLog(machine, Entry(HostInputKind.MouseMotion, x: storedX, y: storedY, motion: kind));
        });
    }

    /// <inheritdoc />
    public void SetMouseButton(MachineId machineId, HostMouseButton button, bool pressed)
    {
        if (!Enum.IsDefined(button))
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button is not defined.");
        }

        Update(machineId, machine =>
        {
            switch (button)
            {
                case HostMouseButton.Left:
                    machine.MouseLeft = pressed;
                    break;
                case HostMouseButton.Middle:
                    machine.MouseMiddle = pressed;
                    break;
                case HostMouseButton.Right:
                    machine.MouseRight = pressed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, "The mouse button is not defined.");
            }

            AddLog(machine, Entry(HostInputKind.MouseButton, down: pressed, mouseButton: button));
        });
    }

    /// <inheritdoc />
    public void CapturePointer(MachineId machineId, string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        Update(machineId, machine =>
        {
            machine.CaptureWindowId = windowId;
            AddLog(machine, Entry(HostInputKind.PointerCapture, down: true, windowId: windowId));
        });
    }

    /// <inheritdoc />
    public void ReleasePointer(MachineId machineId, string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);
        Update(machineId, machine =>
        {
            if (machine.CaptureWindowId != windowId)
            {
                return;
            }

            machine.CaptureWindowId = null;
            AddLog(machine, Entry(HostInputKind.PointerCapture, down: false, windowId: windowId));
        });
    }

    /// <inheritdoc />
    public HostInputState GetInputState(MachineId machineId)
    {
        HostInputState state = new([], HostKeyModifiers.None, new HostGamePortState(0, 0, 0, 0, 0, 0, 0, 0, false, false, false, false), new HostMouseState(HostMouseMotionKind.Relative, 0, 0, false, false, false, null), default);
        Update(machineId, machine => state = ToInputState(machine));
        return state;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostInputEntry> CopyInputLog(MachineId machineId)
    {
        HostInputEntry[] copy = [];
        Update(machineId, machine => copy = machine.InputLog.ToArray());
        return copy;
    }

    /// <inheritdoc />
    public IReadOnlyList<HostInputEntry> DrainInput(MachineId machineId)
    {
        HostInputEntry[] copy = [];
        Update(machineId, machine =>
        {
            copy = machine.InputLog.ToArray();
            machine.InputLog.Clear();
        });

        return copy;
    }

    private static void RequireKey(HostKey key)
    {
        if (key == HostKey.None || !Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A host key is required.");
        }
    }

    private static HostInputState ToInputState(LoopbackMachine machine)
    {
        return new HostInputState(
            machine.KeysDown.ToArray(),
            machine.Modifiers,
            new HostGamePortState(
                machine.JoystickX[0],
                machine.JoystickY[0],
                machine.JoystickX[1],
                machine.JoystickY[1],
                machine.Paddles[0],
                machine.Paddles[1],
                machine.Paddles[2],
                machine.Paddles[3],
                machine.Buttons[0],
                machine.Buttons[1],
                machine.Buttons[2],
                machine.Buttons[3]),
            new HostMouseState(
                machine.MouseKind,
                machine.MouseX,
                machine.MouseY,
                machine.MouseLeft,
                machine.MouseMiddle,
                machine.MouseRight,
                machine.CaptureWindowId),
            machine.LastText);
    }

    private static void ApplyKey(LoopbackMachine machine, HostKey key, HostKeyModifiers modifiers, bool down)
    {
        machine.Modifiers = modifiers;
        if (down)
        {
            if (!machine.KeysDown.Contains(key))
            {
                machine.KeysDown.Add(key);
            }
        }
        else
        {
            machine.KeysDown.Remove(key);
        }

        AddLog(machine, Entry(HostInputKind.Key, key: key, modifiers: modifiers, down: down));
    }

    private static void AddLog(LoopbackMachine machine, HostInputEntry entry)
    {
        machine.InputLog.Add(entry);
        if (machine.InputLog.Count > LoopbackMachine.MaxInputLog)
        {
            machine.InputLog.RemoveAt(0);
        }
    }

    private static HostInputEntry Entry(
        HostInputKind kind,
        HostKey key = HostKey.None,
        HostKeyModifiers modifiers = HostKeyModifiers.None,
        bool down = false,
        int index = 0,
        float x = 0,
        float y = 0,
        HostMouseMotionKind motion = HostMouseMotionKind.Relative,
        HostMouseButton mouseButton = HostMouseButton.Left,
        string? windowId = null)
    {
        return new HostInputEntry(kind, key, modifiers, down, index, x, y, motion, mouseButton, windowId);
    }

    private static bool TryMapChar(char character, out HostKey key, out HostKeyModifiers modifiers)
    {
        modifiers = HostKeyModifiers.None;
        if (character is >= 'a' and <= 'z')
        {
            key = (HostKey)((int)HostKey.A + (character - 'a'));
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            key = (HostKey)((int)HostKey.A + (character - 'A'));
            modifiers = HostKeyModifiers.Shift;
            return true;
        }

        if (character is >= '0' and <= '9')
        {
            key = (HostKey)((int)HostKey.D0 + (character - '0'));
            return true;
        }

        if (character == ' ')
        {
            key = HostKey.Space;
            return true;
        }

        if (character is '\r' or '\n')
        {
            key = HostKey.Return;
            return true;
        }

        key = HostKey.None;
        return false;
    }

    private static float ClampAxis(float value, string name)
    {
        float finite = RequireFinite(value, name);
        return Math.Clamp(finite, -1f, 1f);
    }

    private static float RequireFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");
        }

        return value;
    }

    private static byte ToPaddle(float axis)
    {
        int scaled = (int)Math.Round((axis + 1f) * 127.5f, MidpointRounding.AwayFromZero);
        return (byte)Math.Clamp(scaled, 0, 255);
    }
}