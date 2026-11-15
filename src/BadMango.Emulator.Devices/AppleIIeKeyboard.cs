// <copyright file="AppleIIeKeyboard.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Signaling;
using BadMango.Emulator.Core.Signaling;

using Interfaces;

/// <summary>
/// Keyboard device handling $C000 (KBD) and $C010 (KBDSTRB).
/// </summary>
/// <remarks>
/// <para>
/// The Apple II keyboard presents a simple interface to software:
/// </para>
/// <list type="bullet">
/// <item><description>$C000: Keyboard data (bit 7 = strobe, bits 6-0 = ASCII)</description></item>
/// <item><description>$C010: Any key down flag (read) / clear strobe (read or write)</description></item>
/// </list>
/// <para>
/// When a key is pressed, the ASCII code is latched into the keyboard data register
/// with the high bit (strobe) set. The strobe remains set until cleared by accessing $C010.
/// </para>
/// <para>
/// The keyboard tracks the modifier inputs that map to the Apple II pushbuttons:
/// </para>
/// <list type="bullet">
/// <item><description>$C061 (PB0): Open Apple / Button 0 - bit 7 set when pressed</description></item>
/// <item><description>$C062 (PB1): Closed Apple / Button 1 - bit 7 set when pressed</description></item>
/// <item><description>$C063 (PB2): Shift key modifier - bit 7 set when pressed</description></item>
/// </list>
/// <para>
/// Threading: the legacy host-facing mutators (<see cref="KeyDown"/>, <see cref="KeyUp"/>,
/// <see cref="SetModifiers"/>, <see cref="PressResetKey"/> and <see cref="ReleaseResetKey"/>) are
/// marshalled onto the machine thread with <see cref="IMachineDispatcher.Post"/> when the keyboard
/// was initialized by a machine. <see cref="IHostKeyboardSink"/> methods are called only by this
/// machine's <see cref="KeyboardInput"/>, and only on the machine thread, so they apply directly.
/// Typed text is not queued here; <see cref="KeyboardInput.TypeText"/> owns that queue.
/// </para>
/// </remarks>
[DeviceType("keyboard")]
public sealed class AppleIIeKeyboard : IKeyboardDevice, IHostKeyboardSink, ISoftSwitchProvider, IResetReceiver
{
    private const ushort KeyboardDataAddress = 0xC000;
    private const ushort KeyboardStrobeAddress = 0xC010;
    private const ushort PushButton0Address = 0xC061;
    private const ushort PushButton1Address = 0xC062;
    private const ushort PushButton2Address = 0xC063;

    private const byte KeyboardDataOffset = 0x00;
    private const byte KeyboardStrobeOffset = 0x10;
    private const byte PushButton0Offset = 0x61;
    private const byte PushButton1Offset = 0x62;
    private const byte PushButton2Offset = 0x63;
    private const byte StrobeBit = 0x80;
    private const byte KeyDownBit = 0x80;
    private const byte ButtonPressedBit = 0x80;

    private const int ResetSourceDeviceId = int.MinValue + 2;

    private readonly IKeyTranslator translator;
    private readonly KeystrokeLookup keystrokes;
    private readonly Dictionary<KeyboardKey, int> heldKeys = [];
    private IMachineDispatcher? dispatcher;
    private byte lastKey;
    private bool strobe;
    private bool keyDown;
    private bool directKeyDown;
    private bool resetKeyPressed;
    private bool resetAsserted;
    private KeyboardModifiers modifiers;
    private IScheduler? scheduler;
    private ISignalBus? signals;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleIIeKeyboard"/> class with the US-layout
    /// Apple IIe key translator.
    /// </summary>
    public AppleIIeKeyboard()
        : this(new AppleIIeKeyTranslator())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleIIeKeyboard"/> class with the specified
    /// key translator.
    /// </summary>
    /// <param name="translator">The translator that maps shared <see cref="KeyboardKey"/> values and modifiers into this keyboard's codes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="translator"/> is <see langword="null"/>.</exception>
    public AppleIIeKeyboard(IKeyTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(translator);
        this.translator = translator;
        keystrokes = new KeystrokeLookup(translator);
    }

    /// <inheritdoc />
    public bool IsReadyForTypedKey => !strobe;

    /// <inheritdoc />
    public string Name => "Keyboard";

    /// <inheritdoc />
    public string DeviceType => "Keyboard";

    /// <inheritdoc />
    public PeripheralKind Kind => PeripheralKind.Motherboard;

    /// <inheritdoc />
    public bool HasKeyDown => keyDown;

    /// <inheritdoc />
    public byte KeyData => strobe ? (byte)(lastKey | StrobeBit) : lastKey;

    /// <inheritdoc />
    public KeyboardModifiers Modifiers => modifiers;

    /// <inheritdoc />
    public IKeyTranslator Translator => translator;

    /// <inheritdoc />
    public bool RequireControlForReset { get; set; } = true;

    /// <inheritdoc />
    public string ProviderName => "Keyboard";

    /// <inheritdoc />
    public IReadOnlyList<SoftSwitchState> GetSoftSwitchStates()
    {
        bool openApplePressed = modifiers.HasFlag(KeyboardModifiers.OpenApple);
        bool closedApplePressed = modifiers.HasFlag(KeyboardModifiers.ClosedApple);
        bool shiftPressed = modifiers.HasFlag(KeyboardModifiers.Shift);

        return
        [
            new("KBD", KeyboardDataAddress, strobe, "Keyboard data register (strobe set when key available)"),
            new("KBDSTRB", KeyboardStrobeAddress, keyDown, "Any key down flag"),
            new("PB0", PushButton0Address, openApplePressed, "Pushbutton 0 / Open Apple"),
            new("PB1", PushButton1Address, closedApplePressed, "Pushbutton 1 / Closed Apple"),
            new("PB2", PushButton2Address, shiftPressed, "Pushbutton 2 / Shift key"),
        ];
    }

    /// <inheritdoc />
    public void Initialize(IEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        scheduler = context.Scheduler;
        signals = context.Signals;
        dispatcher = context as IMachineDispatcher;
    }

    /// <inheritdoc />
    public void RegisterHandlers(IOPageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        // $C000: Keyboard data (read-only for keyboard, write-only for 80STORE)
        dispatcher.RegisterRead(KeyboardDataOffset, ReadKeyboardData);

        // $C010: Clear strobe (read and write)
        dispatcher.Register(KeyboardStrobeOffset, ClearStrobeRead, ClearStrobeWrite);

        // $C061-$C063: Pushbutton inputs (read-only).
        // Register only when unclaimed so GameIOController can remain the single owner
        // when both devices are present, independent of registration order.
        _ = dispatcher.RegisterReadIfUnregistered(PushButton0Offset, ReadPushButton0);
        _ = dispatcher.RegisterReadIfUnregistered(PushButton1Offset, ReadPushButton1);
        _ = dispatcher.RegisterReadIfUnregistered(PushButton2Offset, ReadPushButton2);
    }

    /// <inheritdoc />
    public void Reset()
    {
        lastKey = 0;
        strobe = false;
        directKeyDown = false;
        heldKeys.Clear();
        keyDown = false;
        resetKeyPressed = false;
        SetResetLineState(asserted: false);
    }

    /// <inheritdoc />
    public void KeyDown(byte asciiCode)
    {
        OnMachineThread(() =>
        {
            lastKey = (byte)(asciiCode & 0x7F); // Clear high bit
            strobe = true;
            directKeyDown = true;
            SetKeyDownFlag();
        });
    }

    /// <inheritdoc />
    public void KeyUp()
    {
        // Note: lastKey and strobe remain unchanged until strobe is cleared.
        // Keys held through the sink stay down until their own key-up.
        OnMachineThread(() =>
        {
            directKeyDown = false;
            SetKeyDownFlag();
        });
    }

    /// <inheritdoc />
    public void SetModifiers(KeyboardModifiers modifiers)
    {
        OnMachineThread(() =>
        {
            this.modifiers = modifiers;
            UpdateResetLineState();
        });
    }

    /// <inheritdoc />
    public void PressResetKey()
    {
        OnMachineThread(() =>
        {
            resetKeyPressed = true;
            UpdateResetLineState();
        });
    }

    /// <inheritdoc />
    public void ReleaseResetKey()
    {
        OnMachineThread(() =>
        {
            resetKeyPressed = false;
            UpdateResetLineState();
        });
    }

    /// <inheritdoc />
    public void OnHostKeyDown(KeyboardKey key, KeyboardModifiers modifiers)
    {
        this.modifiers = modifiers;
        UpdateResetLineState();

        byte? code = translator.Translate(key, modifiers);
        if (code is null)
        {
            // Modifier keys, Reset, and Power update the snapshot and the soft switches only.
            // Reset routing to the RESET line is a later change; until then Reset translates to null.
            return;
        }

        heldKeys[key] = heldKeys.GetValueOrDefault(key) + 1;
        lastKey = (byte)(code.Value & 0x7F);
        strobe = true;
        SetKeyDownFlag();
    }

    /// <inheritdoc />
    public void OnHostKeyUp(KeyboardKey key, KeyboardModifiers modifiers)
    {
        this.modifiers = modifiers;
        UpdateResetLineState();

        if (heldKeys.TryGetValue(key, out int count))
        {
            if (count <= 1)
            {
                heldKeys.Remove(key);
            }
            else
            {
                heldKeys[key] = count - 1;
            }
        }

        SetKeyDownFlag();
    }

    /// <inheritdoc />
    public bool TryGetKeystroke(char c, out HostKeystroke keystroke) => keystrokes.TryGet(c, out keystroke);

    /// <inheritdoc />
    public void ReleaseAllTypedKeys()
    {
        // KeyboardInput releases the typed key with OnHostKeyUp before calling this. Recompute AKD
        // from the keys still held so a live key stays down and a typed key that is already up
        // cannot leave the flag stuck.
        SetKeyDownFlag();
    }

    /// <inheritdoc />
    /// <remarks>
    /// RESET clears the keyboard strobe. Releasing a key that <see cref="KeyboardInput"/> is
    /// typing, and dropping the rest of that text, is the input layer's job
    /// (<see cref="KeyboardInput.OnResetAsserted"/>). Keys and modifiers the user is physically
    /// holding are unchanged.
    /// </remarks>
    public void OnResetAsserted(Cycle cycle)
    {
        strobe = false;
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
    }

    private byte ReadKeyboardData(byte offset, in BusAccess context)
    {
        return KeyData;
    }

    private byte ClearStrobeRead(byte offset, in BusAccess context)
    {
        byte result = keyDown ? KeyDownBit : (byte)0x00;

        if (!context.IsSideEffectFree)
        {
            strobe = false;
        }

        return result;
    }

    private void ClearStrobeWrite(byte offset, byte value, in BusAccess context)
    {
        if (!context.IsSideEffectFree)
        {
            strobe = false;
        }
    }

    private byte ReadPushButton0(byte offset, in BusAccess context)
    {
        // PB0 / Open Apple: bit 7 set = button pressed
        return modifiers.HasFlag(KeyboardModifiers.OpenApple) ? ButtonPressedBit : (byte)0x00;
    }

    private byte ReadPushButton1(byte offset, in BusAccess context)
    {
        // PB1 / Closed Apple: bit 7 set = button pressed
        return modifiers.HasFlag(KeyboardModifiers.ClosedApple) ? ButtonPressedBit : (byte)0x00;
    }

    private byte ReadPushButton2(byte offset, in BusAccess context)
    {
        // PB2 / Shift key: bit 7 set = key pressed
        return modifiers.HasFlag(KeyboardModifiers.Shift) ? ButtonPressedBit : (byte)0x00;
    }

    private void OnMachineThread(Action action)
    {
        if (dispatcher is null)
        {
            action();
        }
        else
        {
            dispatcher.Post(action);
        }
    }

    private void SetKeyDownFlag()
    {
        keyDown = directKeyDown || heldKeys.Count > 0;
    }

    private void UpdateResetLineState()
    {
        bool controlHeld = modifiers.HasFlag(KeyboardModifiers.Control);
        bool shouldAssert = resetKeyPressed && (!RequireControlForReset || controlHeld);
        SetResetLineState(shouldAssert);
    }

    private void SetResetLineState(bool asserted)
    {
        if (signals is null || resetAsserted == asserted)
        {
            return;
        }

        resetAsserted = asserted;

        if (asserted)
        {
            signals.Assert(SignalLine.Reset, ResetSourceDeviceId, scheduler?.Now ?? Cycle.Zero);
        }
        else
        {
            signals.Deassert(SignalLine.Reset, ResetSourceDeviceId, scheduler?.Now ?? Cycle.Zero);
        }
    }
}