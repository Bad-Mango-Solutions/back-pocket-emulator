// <copyright file="KeyboardInput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;

using Interfaces;

/// <summary>
/// Machine-scoped keyboard input multiplexer that accepts host input and typed text and applies
/// both on the machine thread through the scheduler.
/// </summary>
public sealed class KeyboardInput : IKeyboardInput, IResetReceiver
{
    private const string InputDrainTag = "keyboard-input";
    private const string TypingTag = "keyboard-type";

    private readonly object sync = new();
    private readonly Queue<InputEvent> pendingHostEvents = new();
    private readonly List<JournalEntry> entries = [];
    private readonly IHostKeyboardSink sink;
    private readonly IScheduler scheduler;
    private readonly IMachineDispatcher? dispatcher;
    private readonly double cpuClockHz;
    private readonly Dictionary<KeyboardKey, bool> liveHeldKeys = new();
    private readonly Queue<TypedKeyWorkItem> typingQueue = new();
    private KeyboardModifiers liveModifiers = KeyboardModifiers.None;
    private bool drainScheduled;
    private bool typingActive;
    private int typingGeneration;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardInput"/> class.
    /// </summary>
    /// <param name="sink">The keyboard sink that receives applied host and typed key state.</param>
    /// <param name="scheduler">The machine scheduler used to apply queued work in emulated time.</param>
    /// <param name="dispatcher">Optional dispatcher used to marshal work onto the machine thread.</param>
    /// <param name="cpuClockHz">The emulated CPU clock speed in hertz.</param>
    public KeyboardInput(IHostKeyboardSink sink, IScheduler scheduler, IMachineDispatcher? dispatcher, double cpuClockHz)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.dispatcher = dispatcher;
        this.cpuClockHz = cpuClockHz;
    }

    /// <inheritdoc />
    public bool IsTyping => typingActive;

    /// <inheritdoc />
    public IKeyboardInputJournal Journal => new KeyboardInputJournal(entries);

    /// <inheritdoc />
    public void KeyDown(KeyboardKey key, KeyboardModifiers modifiers)
    {
        QueueHostEvent(InputEvent.KeyDownEvent(key, modifiers));
    }

    /// <inheritdoc />
    public void KeyUp(KeyboardKey key, KeyboardModifiers modifiers)
    {
        QueueHostEvent(InputEvent.KeyUpEvent(key, modifiers));
    }

    /// <inheritdoc />
    public TypeTextReceipt TypeText(string text, TypeTextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var typedOptions = options ?? new TypeTextOptions();

        var skipped = new List<char>();
        var rejected = false;
        var sequence = BuildTypedSequence(text, typedOptions, skipped, ref rejected, out int acceptedCount);

        if (sequence.Count == 0)
        {
            return new TypeTextReceipt(acceptedCount, skipped, rejected);
        }

        int generation = ++typingGeneration;
        lock (sync)
        {
            typingQueue.Clear();
            foreach (var item in sequence)
            {
                typingQueue.Enqueue(item);
            }

            typingActive = true;
        }

        scheduler.ScheduleAfter(0, ScheduledEventKind.DeviceTimer, 0, _ => ProcessNextTypedKey(generation), TypingTag);
        return new TypeTextReceipt(acceptedCount, skipped, rejected);
    }

    /// <inheritdoc />
    public void CancelTyping()
    {
        lock (sync)
        {
            typingGeneration++;
            typingQueue.Clear();
            typingActive = false;
            sink.ReleaseAllTypedKeys();
            entries.Add(new JournalEntry(scheduler.Now, InputEvent.CancelTypingEvent()));
        }
    }

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
        CancelTyping();
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
    }

    private void QueueHostEvent(InputEvent inputEvent)
    {
        lock (sync)
        {
            pendingHostEvents.Enqueue(inputEvent);
            if (drainScheduled)
            {
                return;
            }

            drainScheduled = true;
        }

        void DrainPending()
        {
            ApplyPendingHostEvents();
        }

        if (dispatcher is null)
        {
            DrainPending();
            return;
        }

        dispatcher.Post(DrainPending);
    }

    private void ApplyPendingHostEvents()
    {
        List<InputEvent> batch;
        lock (sync)
        {
            if (pendingHostEvents.Count == 0)
            {
                drainScheduled = false;
                return;
            }

            batch = new List<InputEvent>(pendingHostEvents.Count);
            while (pendingHostEvents.Count > 0)
            {
                batch.Add(pendingHostEvents.Dequeue());
            }

            drainScheduled = false;
        }

        scheduler.ScheduleAfter(0, ScheduledEventKind.DeferredWork, 0, _ => ApplyHostBatch(batch), InputDrainTag);
    }

    private void ApplyHostBatch(List<InputEvent> batch)
    {
        foreach (var input in batch)
        {
            ApplyHostEvent(input);
        }
    }

    private void ApplyHostEvent(InputEvent input)
    {
        switch (input.Kind)
        {
            case InputEventKind.KeyDown:
                if (!liveHeldKeys.ContainsKey(input.Key))
                {
                    sink.OnHostKeyDown(input.Key, input.Modifiers);
                    liveHeldKeys[input.Key] = true;
                }

                liveModifiers = input.Modifiers;
                break;
            case InputEventKind.KeyUp:
                sink.OnHostKeyUp(input.Key, input.Modifiers);
                liveHeldKeys.Remove(input.Key);
                liveModifiers = input.Modifiers;
                break;
            default:
                break;
        }

        AppendJournalEntry(input);
    }

    private void AppendJournalEntry(InputEvent inputEvent)
    {
        lock (sync)
        {
            entries.Add(new JournalEntry(scheduler.Now, inputEvent));
        }
    }

    private void ProcessNextTypedKey(int generation)
    {
        if (generation != typingGeneration)
        {
            return;
        }

        TypedKeyWorkItem current;
        lock (sync)
        {
            if (typingQueue.Count == 0)
            {
                typingActive = false;
                RestoreModifierSnapshot();
                return;
            }

            current = typingQueue.Peek();
        }

        if (current.WaitForConsumption && !sink.IsReadyForTypedKey)
        {
            scheduler.ScheduleAfter(MillisecondsToCycles(current.StrobeWaitPollMs), ScheduledEventKind.DeviceTimer, 0, _ => ProcessNextTypedKey(generation), TypingTag);
            return;
        }

        lock (sync)
        {
            if (typingQueue.Count == 0)
            {
                typingActive = false;
                RestoreModifierSnapshot();
                return;
            }

            current = typingQueue.Dequeue();
        }

        var typedDownEvent = InputEvent.TypedKeyDownEvent(current.Key, current.Modifiers);
        sink.OnHostKeyDown(current.Key, current.Modifiers);
        AppendJournalEntry(typedDownEvent);

        scheduler.ScheduleAfter(current.HoldCycles, ScheduledEventKind.DeviceTimer, 0, _ => ReleaseTypedKey(generation, current), TypingTag);
    }

    private void ReleaseTypedKey(int generation, TypedKeyWorkItem item)
    {
        if (generation != typingGeneration)
        {
            return;
        }

        var typedUpEvent = InputEvent.TypedKeyUpEvent(item.Key, item.Modifiers);
        sink.OnHostKeyUp(item.Key, item.Modifiers);
        AppendJournalEntry(typedUpEvent);

        if (typingQueue.Count == 0)
        {
            typingActive = false;
            RestoreModifierSnapshot();
            return;
        }

        var gap = item.GapCycles;
        scheduler.ScheduleAfter(gap, ScheduledEventKind.DeviceTimer, 0, _ => ProcessNextTypedKey(generation), TypingTag);
    }

    private void RestoreModifierSnapshot()
    {
        if (liveModifiers == KeyboardModifiers.None)
        {
            return;
        }

        foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
        {
            if (key == KeyboardKey.None || key == KeyboardKey.Reset || key == KeyboardKey.Power)
            {
                continue;
            }

            var modifier = key switch
            {
                KeyboardKey.LeftShift or KeyboardKey.RightShift => KeyboardModifiers.Shift,
                KeyboardKey.LeftControl or KeyboardKey.RightControl => KeyboardModifiers.Control,
                KeyboardKey.CapsLock => KeyboardModifiers.CapsLock,
                KeyboardKey.LeftOption or KeyboardKey.RightOption => KeyboardModifiers.OpenApple,
                KeyboardKey.LeftCommand or KeyboardKey.RightCommand => KeyboardModifiers.ClosedApple,
                _ => KeyboardModifiers.None,
            };

            if (modifier != KeyboardModifiers.None && liveModifiers.HasFlag(modifier))
            {
                sink.OnHostKeyDown(key, liveModifiers);
            }
        }
    }

    private static List<TypedKeyWorkItem> BuildTypedSequence(
        string text,
        TypeTextOptions options,
        List<char> skipped,
        ref bool rejected,
        out int acceptedCount)
    {
        var sequence = new List<TypedKeyWorkItem>();
        acceptedCount = 0;

        for (var i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
                ch = '\r';
            }

            if (ch == '
' || ch == '
')
            {
                var evaluated = CharacterToKey(char.ToUpperInvariant('r'), options, out bool mapped);
                if (mapped)
                {
                    sequence.Add(evaluated);
                    acceptedCount++;
                }

                continue;
            }

            if (ch == '	')
            {
                var evaluated = CharacterToKey('	', options, out bool mapped);
                if (mapped)
                {
                    sequence.Add(evaluated);
                    acceptedCount++;
                }

                continue;
            }

            if (ch == '')
            {
                var evaluated = CharacterToKey('', options, out bool mapped);
                if (mapped)
                {
                    sequence.Add(evaluated);
                    acceptedCount++;
                }

                continue;
            }

            var mappedChar = ch;
            if (options.ForceUppercase && char.IsLetter(ch) && char.IsLower(ch))
            {
                mappedChar = char.ToUpperInvariant(ch);
            }

            var item = CharacterToKey(mappedChar, options, out bool resolved);
            if (resolved)
            {
                sequence.Add(item);
                acceptedCount++;
            }
            else if (options.Unmappable == UnmappableCharacterPolicy.Reject)
            {
                rejected = true;
            }
            else if (options.Unmappable == UnmappableCharacterPolicy.Replace)
            {
                var replacement = options.ReplacementChar;
                var replacementItem = CharacterToKey(replacement, options, out bool replacementMapped);
                if (replacementMapped)
                {
                    sequence.Add(replacementItem);
                    acceptedCount++;
                }
                else
                {
                    rejected = true;
                }
            }
            else
            {
                skipped.Add(ch);
            }
        }

        return sequence;
    }

    private static TypedKeyWorkItem CharacterToKey(char value, TypeTextOptions options, out bool resolved)
    {
        resolved = false;
        var holdCycles = MillisecondsToCycles(options.HoldMs);
        var gap = options.GapMs ?? Math.Max(1, options.HoldMs / 5);
        var gapCycles = MillisecondsToCycles(gap);

        KeyboardKey mappedKey = value switch
        {
            '
' => KeyboardKey.Return,
            '
' => KeyboardKey.Return,
            '	' => KeyboardKey.Tab,
            '' => KeyboardKey.Backspace,
            _ => KeyboardKey.None,
        };

        if (mappedKey != KeyboardKey.None)
        {
            resolved = true;
            return new TypedKeyWorkItem(mappedKey, KeyboardModifiers.None, holdCycles, gapCycles, options.StrobeWaitPollMs, true);
        }

        var performed = CharacterToHostKeystroke(value);
        if (!performed.HasValue)
        {
            return default;
        }

        resolved = true;
        return new TypedKeyWorkItem(performed.Value.Key, performed.Value.Modifiers, holdCycles, gapCycles, options.StrobeWaitPollMs, options.WaitForConsumption);
    }

    private static HostKeystroke? CharacterToHostKeystroke(char value)
    {
        if (value is '
' or '
' or '	' or '')
        {
            return null;
        }

        foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
        {
            if (key is KeyboardKey.None or KeyboardKey.Reset or KeyboardKey.Power)
            {
                continue;
            }

            foreach (var modifiers in new[] { KeyboardModifiers.None, KeyboardModifiers.Shift, KeyboardModifiers.Control, KeyboardModifiers.Control | KeyboardModifiers.Shift })
            {
                var translated = new AppleIIeKeyTranslator().Translate(key, modifiers);
                if (translated is not null && (char)translated.Value == value)
                {
                    return new HostKeystroke(key, modifiers);
                }
            }
        }

        return null;
    }

    private static ulong MillisecondsToCycles(int milliseconds)
    {
        return (ulong)Math.Max(0L, (long)Math.Floor(milliseconds * (1_020_484d / 1000.0)));
    }

    private readonly record struct TypedKeyWorkItem(
        KeyboardKey Key,
        KeyboardModifiers Modifiers,
        ulong HoldCycles,
        ulong GapCycles,
        int StrobeWaitPollMs,
        bool WaitForConsumption);

    private sealed class KeyboardInputJournal : IKeyboardInputJournal
    {
        private readonly IReadOnlyList<JournalEntry> entries;

        public KeyboardInputJournal(IReadOnlyList<JournalEntry> entries)
        {
            this.entries = entries;
        }

        public int Count => entries.Count;

        public JournalEntry this[int index] => entries[index];

        public IEnumerator<JournalEntry> GetEnumerator() => entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
