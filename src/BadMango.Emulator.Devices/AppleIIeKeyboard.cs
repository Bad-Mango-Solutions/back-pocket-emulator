// <copyright file="AppleIIeKeyboard.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Text.Json;

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
/// <item><description>$C010: Any-key-down in bit 7 and the latched ASCII in bits 0–6 (read); clears the strobe (read or write). Writes to $C011–$C01F also clear the strobe.</description></item>
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
/// <para>
/// Auto-repeat, when <see cref="AutoRepeat"/> is set, counts <see cref="IVideoDevice.VBlankOccurred"/>
/// on the machine thread. A matrix key that stays down sets the strobe again on the 40th vertical
/// blank and then every 4 blanks, with the same ASCII. Forty is the midpoint of the 32–48 blank
/// delay; the flash-counter phase that picks a point in that range is not modeled. Reset and
/// modifier keys do not repeat, and pressing another matrix key starts the delay over.
/// </para>
/// </remarks>
[DeviceType("keyboard")]
public sealed class AppleIIeKeyboard : IKeyboardDevice, IHostKeyboardSink, ISoftSwitchProvider, IResetReceiver, IProfileConfiguredDevice
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

    /// <summary>
    /// Vertical blanks a matrix key is held before the first auto-repeat strobe.
    /// </summary>
    /// <remarks>
    /// Sather's delay is 32–48 vertical scans, depending on the flash-counter phase.
    /// That phase is not modeled, so this is the midpoint. The first repeat falls on
    /// this blank, which is a multiple of <see cref="AutoRepeatIntervalVBlanks"/>.
    /// </remarks>
    private const int AutoRepeatDelayVBlanks = 40;

    /// <summary>
    /// Vertical blanks between auto-repeat strobes (vertical frequency divided by 4).
    /// </summary>
    private const int AutoRepeatIntervalVBlanks = 4;

    private readonly IKeyTranslator translator;
    private readonly KeystrokeLookup keystrokes;
    private readonly Dictionary<KeyboardKey, int> heldKeys = [];
    private IMachineDispatcher? dispatcher;
    private IVideoDevice? videoDevice;
    private KeyboardKey repeatKey;
    private int vblanksHeld;
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

    /// <summary>
    /// Gets or sets a value indicating whether a held matrix key auto-repeats.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to set the strobe again after <c>40</c> vertical blanks and then
    /// every <c>4</c>; <see langword="false"/> to latch the key once. The default is
    /// <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// A profile sets this from <c>config.autoRepeat</c>. The count comes from the video
    /// device's vertical blank, on the machine thread.
    /// </remarks>
    public bool AutoRepeat { get; set; } = true;

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
        AttachVideo(context.GetComponent<IVideoDevice>());
    }

    /// <summary>
    /// Applies <c>autoRepeat</c> from the keyboard's profile <c>config</c> object.
    /// </summary>
    /// <param name="config">
    /// The <c>devices.motherboard[].config</c> object, or <see langword="null"/> when the profile omits it.
    /// </param>
    /// <exception cref="InvalidOperationException"><c>autoRepeat</c> is present and is not a boolean.</exception>
    /// <remarks>
    /// Other config keys belong to later keyboard work and are ignored. An omitted <c>autoRepeat</c>
    /// leaves <see cref="AutoRepeat"/> as it is, which starts as <see langword="true"/>.
    /// </remarks>
    public void ApplyProfileConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } element
            || !element.TryGetProperty("autoRepeat", out JsonElement value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            throw new InvalidOperationException("Keyboard config 'autoRepeat' must be a boolean.");
        }

        AutoRepeat = value.GetBoolean();
    }

    /// <inheritdoc />
    public void RegisterHandlers(IOPageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        // $C000: Keyboard data (read-only for keyboard, write-only for 80STORE)
        dispatcher.RegisterRead(KeyboardDataOffset, ReadKeyboardData);

        // $C010: AKD and latched ASCII on read; either access clears the strobe.
        dispatcher.Register(KeyboardStrobeOffset, ClearStrobeRead, ClearStrobeWrite);

        // $C011–$C01F writes have no owner. The IOU, MMU, video, and character devices
        // register reads only (banking status, RDVBL–RDHIRES, RDALTCHAR, RD80COL).
        // RegisterWrite does not replace those reads, so the keyboard can clear
        // KEYSTROBE on W$C01X without taking the read. The reads themselves stay as they are.
        for (int strobeOffset = KeyboardStrobeOffset + 1; strobeOffset <= 0x1F; strobeOffset++)
        {
            dispatcher.RegisterWrite((byte)strobeOffset, ClearStrobeWrite);
        }

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
        repeatKey = KeyboardKey.None;
        vblanksHeld = 0;
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
        if (key == KeyboardKey.Reset)
        {
            resetKeyPressed = true;
            UpdateResetLineState();
            return;
        }

        UpdateResetLineState();

        byte? code = translator.Translate(key, modifiers);
        if (code is null)
        {
            // Modifier keys and Power update the snapshot and the soft switches only.
            return;
        }

        heldKeys[key] = heldKeys.GetValueOrDefault(key) + 1;
        lastKey = (byte)(code.Value & 0x7F);
        strobe = true;
        repeatKey = key;
        vblanksHeld = 0;
        SetKeyDownFlag();
    }

    /// <inheritdoc />
    public void OnHostKeyUp(KeyboardKey key, KeyboardModifiers modifiers)
    {
        this.modifiers = modifiers;
        if (key == KeyboardKey.Reset)
        {
            resetKeyPressed = false;
            UpdateResetLineState();
            return;
        }

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

        if (key == repeatKey && !heldKeys.ContainsKey(key))
        {
            repeatKey = KeyboardKey.None;
            vblanksHeld = 0;
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

        // The repeat counter lives in the IOU, which RESET' clears. A key that is still
        // held starts the 40-blank delay over instead of repeating immediately.
        vblanksHeld = 0;
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
        // Bit 7 is AKD. Bits 0–6 are the latched ASCII, which lastKey already stores without bit 7.
        byte result = keyDown ? (byte)(lastKey | KeyDownBit) : lastKey;

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

    private void AttachVideo(IVideoDevice? source)
    {
        if (ReferenceEquals(videoDevice, source))
        {
            return;
        }

        if (videoDevice is not null)
        {
            videoDevice.VBlankOccurred -= OnVBlank;
        }

        videoDevice = source;
        if (videoDevice is not null)
        {
            videoDevice.VBlankOccurred += OnVBlank;
        }
    }

    /// <summary>
    /// Counts one vertical blank toward auto-repeat. Runs on the machine thread from
    /// <see cref="IVideoDevice.VBlankOccurred"/> and does not allocate.
    /// </summary>
    private void OnVBlank()
    {
        if (!AutoRepeat || repeatKey == KeyboardKey.None)
        {
            return;
        }

        vblanksHeld++;
        int sinceDelay = vblanksHeld - AutoRepeatDelayVBlanks;
        if (sinceDelay < 0 || (sinceDelay % AutoRepeatIntervalVBlanks) != 0)
        {
            return;
        }

        strobe = true;
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