// <copyright file="KeyboardInput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Collections;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;

using Interfaces;

/// <summary>
/// Per-machine keyboard input. Host calls from any thread are queued and applied on the machine
/// thread; typed text is scheduled in emulated time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KeyDown"/>, <see cref="KeyUp"/>, <see cref="TypeText"/> and <see cref="CancelTyping"/>
/// append to a lock-protected FIFO and <see cref="IMachineDispatcher.Post"/> one drain. The drain
/// schedules a single <see cref="ScheduledEventKind.DeferredWork"/> event, and that event applies
/// the batch. Input is never applied directly from the calling thread. When
/// the dispatcher is <see langword="null"/>, the drain runs inline and still applies
/// the batch through <see cref="ScheduledEventKind.DeferredWork"/>.
/// </para>
/// <para>
/// <see cref="TypeText"/> steps (key down, hold, key up, gap, and strobe-wait re-check) are
/// <see cref="ScheduledEventKind.DeviceTimer"/> events. The next key starts only from its own
/// event, after <see cref="IHostKeyboardSink.IsReadyForTypedKey"/> when waiting is enabled.
/// Milliseconds become cycles as <c>(long)(ms * cpuClockHz / 1000)</c>, rounded down.
/// </para>
/// <para>
/// <see cref="CancelTyping"/> and RESET bump a generation counter, drop queued text, and release
/// the typed key. Device-timer callbacks from an older generation are no-ops. RESET itself flushes
/// device timers; this object implements <see cref="IResetReceiver"/> so the release still happens.
/// Live host keys are not released.
/// </para>
/// </remarks>
public sealed class KeyboardInput : IKeyboardInput, IResetReceiver
{
    private const string InputDrainTag = "keyboard-input";
    private const string TypingTag = "keyboard-type";

    private readonly Lock gate = new();
    private readonly Queue<WorkItem> pending = new();
    private readonly Queue<TypedKey> typingQueue = new();
    private readonly List<JournalEntry> entries = [];
    private readonly HashSet<KeyboardKey> liveHeldKeys = [];
    private readonly IHostKeyboardSink sink;
    private readonly IScheduler scheduler;
    private readonly IMachineDispatcher? dispatcher;
    private readonly double cpuClockHz;
    private KeyboardModifiers liveModifiers = KeyboardModifiers.None;
    private KeyboardKey? heldTypedKey;
    private bool drainPosted;
    private bool chainRunning;
    private int typingGeneration;
    private int unappliedTyping;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardInput"/> class.
    /// </summary>
    /// <param name="sink">The keyboard that receives applied host and typed keys. Called only on the machine thread.</param>
    /// <param name="scheduler">The machine scheduler. Events are scheduled only on the machine thread.</param>
    /// <param name="dispatcher">Marshals the drain onto the machine thread. <see langword="null"/> runs the drain inline.</param>
    /// <param name="cpuClockHz">The profile CPU clock in hertz, used to convert typing delays into cycles.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> or <paramref name="scheduler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cpuClockHz"/> is not a positive finite number.</exception>
    public KeyboardInput(IHostKeyboardSink sink, IScheduler scheduler, IMachineDispatcher? dispatcher, double cpuClockHz)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (double.IsNaN(cpuClockHz) || double.IsInfinity(cpuClockHz) || cpuClockHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuClockHz), cpuClockHz, "CPU clock must be a positive finite number of hertz.");
        }

        this.sink = sink;
        this.scheduler = scheduler;
        this.dispatcher = dispatcher;
        this.cpuClockHz = cpuClockHz;
    }

    private enum WorkKind
    {
        KeyDown,
        KeyUp,
        Cancel,
        Type,
    }

    /// <inheritdoc />
    public bool IsTyping
    {
        get
        {
            lock (gate)
            {
                return chainRunning || heldTypedKey is not null || typingQueue.Count > 0 || unappliedTyping > 0;
            }
        }
    }

    /// <inheritdoc />
    public IKeyboardInputJournal Journal
    {
        get
        {
            lock (gate)
            {
                return new JournalView(entries.ToArray());
            }
        }
    }

    /// <inheritdoc />
    public void KeyDown(KeyboardKey key, KeyboardModifiers modifiers) =>
        Enqueue(WorkItem.KeyDown(key, modifiers));

    /// <inheritdoc />
    public void KeyUp(KeyboardKey key, KeyboardModifiers modifiers) =>
        Enqueue(WorkItem.KeyUp(key, modifiers));

    /// <inheritdoc />
    public TypeTextReceipt TypeText(string text, TypeTextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        TypeTextOptions effective = options ?? new TypeTextOptions();
        Validate(effective);

        TypingPlan plan = Plan(text, effective);
        if (plan.Rejected || plan.Keys.Length == 0)
        {
            return plan.Receipt;
        }

        Enqueue(WorkItem.Type(plan.Keys));
        return plan.Receipt;
    }

    /// <inheritdoc />
    public void CancelTyping()
    {
        lock (gate)
        {
            typingGeneration++;
            typingQueue.Clear();
            chainRunning = false;
        }

        Enqueue(WorkItem.Cancel());
    }

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
        lock (gate)
        {
            typingGeneration++;
            typingQueue.Clear();
            chainRunning = false;
        }

        // RESET has already flushed device timers. Release the typed key here, on the machine
        // thread, so it is up before the reset handler returns. Live keys stay down.
        ReleaseCancelledTyping();
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
    }

    private void Enqueue(WorkItem item)
    {
        bool post;
        lock (gate)
        {
            if (item.Kind == WorkKind.Type)
            {
                item = item.WithGeneration(typingGeneration);
                unappliedTyping++;
            }

            pending.Enqueue(item);
            post = !drainPosted;
            drainPosted = true;
        }

        if (post)
        {
            PostDrain();
        }
    }

    private void PostDrain()
    {
        if (dispatcher is null)
        {
            Drain();
            return;
        }

        dispatcher.Post(Drain);
    }

    private void Drain()
    {
        WorkItem[] batch;
        lock (gate)
        {
            batch = pending.ToArray();
            pending.Clear();
            drainPosted = false;
        }

        scheduler.ScheduleAfter(0, ScheduledEventKind.DeferredWork, 0, _ => ApplyBatch(batch), InputDrainTag);
    }

    private void ApplyBatch(WorkItem[] batch)
    {
        foreach (WorkItem item in batch)
        {
            switch (item.Kind)
            {
                case WorkKind.KeyDown:
                    ApplyKeyDown(item.Key, item.Modifiers);
                    break;
                case WorkKind.KeyUp:
                    ApplyKeyUp(item.Key, item.Modifiers);
                    break;
                case WorkKind.Cancel:
                    ReleaseCancelledTyping();
                    break;
                case WorkKind.Type:
                    ApplyType(item);
                    break;
                default:
                    break;
            }
        }

        ArmTypingChain();
    }

    private void ApplyKeyDown(KeyboardKey key, KeyboardModifiers modifiers)
    {
        lock (gate)
        {
            if (!liveHeldKeys.Add(key))
            {
                // Host auto-repeat. The machine keyboard generates its own repeat.
                return;
            }

            liveModifiers = modifiers;
        }

        sink.OnHostKeyDown(key, modifiers);
        AppendJournal(InputEvent.KeyDownEvent(key, modifiers));
    }

    private void ApplyKeyUp(KeyboardKey key, KeyboardModifiers modifiers)
    {
        lock (gate)
        {
            liveHeldKeys.Remove(key);
            liveModifiers = modifiers;
        }

        sink.OnHostKeyUp(key, modifiers);
        AppendJournal(InputEvent.KeyUpEvent(key, modifiers));
    }

    private void ApplyType(WorkItem item)
    {
        lock (gate)
        {
            if (unappliedTyping > 0)
            {
                unappliedTyping--;
            }

            if (item.Generation != typingGeneration || item.Keys is null)
            {
                return;
            }

            foreach (TypedKey key in item.Keys)
            {
                typingQueue.Enqueue(key);
            }
        }
    }

    private void ArmTypingChain()
    {
        int generation;
        lock (gate)
        {
            if (chainRunning || typingQueue.Count == 0)
            {
                return;
            }

            chainRunning = true;
            generation = typingGeneration;
        }

        scheduler.ScheduleAfter(0, ScheduledEventKind.DeviceTimer, 0, _ => ProcessNext(generation), TypingTag);
    }

    private void ProcessNext(int generation)
    {
        TypedKey item;
        lock (gate)
        {
            if (generation != typingGeneration)
            {
                return;
            }

            if (typingQueue.Count == 0)
            {
                chainRunning = false;
                return;
            }

            item = typingQueue.Peek();
        }

        if (item.WaitForConsumption && !sink.IsReadyForTypedKey)
        {
            lock (gate)
            {
                if (generation != typingGeneration)
                {
                    return;
                }
            }

            scheduler.ScheduleAfter(item.PollCycles, ScheduledEventKind.DeviceTimer, 0, _ => ProcessNext(generation), TypingTag);
            return;
        }

        lock (gate)
        {
            if (generation != typingGeneration || typingQueue.Count == 0)
            {
                return;
            }

            item = typingQueue.Dequeue();
            heldTypedKey = item.Key;
        }

        sink.OnHostKeyDown(item.Key, item.Modifiers);
        AppendJournal(InputEvent.TypedKeyDownEvent(item.Key, item.Modifiers));
        scheduler.ScheduleAfter(item.HoldCycles, ScheduledEventKind.DeviceTimer, 0, _ => ReleaseTyped(generation, item), TypingTag);
    }

    private void ReleaseTyped(int generation, TypedKey item)
    {
        lock (gate)
        {
            if (generation != typingGeneration)
            {
                return;
            }

            if (heldTypedKey == item.Key)
            {
                heldTypedKey = null;
            }
        }

        sink.OnHostKeyUp(item.Key, item.Modifiers);
        AppendJournal(InputEvent.TypedKeyUpEvent(item.Key, item.Modifiers));
        RestoreLiveModifiers();

        bool more;
        int currentGeneration;
        lock (gate)
        {
            if (generation != typingGeneration)
            {
                return;
            }

            more = typingQueue.Count > 0;
            currentGeneration = typingGeneration;
            if (!more)
            {
                chainRunning = false;
            }
        }

        if (!more)
        {
            return;
        }

        ulong delay = item.GapCycles + item.ReturnExtraCycles;
        scheduler.ScheduleAfter(delay, ScheduledEventKind.DeviceTimer, 0, _ => ProcessNext(currentGeneration), TypingTag);
    }

    private void ReleaseCancelledTyping()
    {
        KeyboardKey? held;
        KeyboardModifiers snapshot;
        lock (gate)
        {
            held = heldTypedKey;
            heldTypedKey = null;
            snapshot = liveModifiers;
        }

        // Only a key TypeText is holding is released. Live keys and modifiers, including ones
        // set through the legacy device API, stay as they are. Pushing the input layer's
        // snapshot when it is not typing would clear them.
        if (held is KeyboardKey key)
        {
            sink.OnHostKeyUp(key, snapshot);
            sink.OnHostKeyUp(KeyboardKey.None, snapshot);
        }

        sink.ReleaseAllTypedKeys();
        AppendJournal(InputEvent.CancelTypingEvent());
    }

    private void RestoreLiveModifiers()
    {
        KeyboardModifiers snapshot;
        lock (gate)
        {
            snapshot = liveModifiers;
        }

        // Push the last live snapshot back. KeyboardKey.None is not a held key, so this does not
        // change AKD; it only restores modifier soft switches after a typed key's own snapshot.
        sink.OnHostKeyUp(KeyboardKey.None, snapshot);
    }

    private void AppendJournal(InputEvent inputEvent)
    {
        var entry = new JournalEntry(scheduler.Now, inputEvent);
        lock (gate)
        {
            entries.Add(entry);
        }
    }

    private TypingPlan Plan(string text, TypeTextOptions options)
    {
        var skipped = new List<char>();
        var keys = new List<TypedKey>();
        bool rejected = false;
        int gapMs = options.GapMs ?? Math.Max(1, options.HoldMs / 5);
        ulong holdCycles = MillisecondsToCycles(options.HoldMs);
        ulong gapCycles = MillisecondsToCycles(gapMs);
        ulong pollCycles = MillisecondsToCycles(options.StrobeWaitPollMs);
        ulong returnExtra = MillisecondsToCycles(options.ReturnDelayMs);

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
                keys.Add(MakeTyped(KeyboardKey.Return, KeyboardModifiers.None, holdCycles, gapCycles, pollCycles, returnExtra, options));
                continue;
            }

            if (ch is '\n' or '\r')
            {
                keys.Add(MakeTyped(KeyboardKey.Return, KeyboardModifiers.None, holdCycles, gapCycles, pollCycles, returnExtra, options));
                continue;
            }

            if (ch == '\t')
            {
                keys.Add(MakeTyped(KeyboardKey.Tab, KeyboardModifiers.None, holdCycles, gapCycles, pollCycles, returnExtra, options));
                continue;
            }

            if (ch == '\b')
            {
                keys.Add(MakeTyped(KeyboardKey.Backspace, KeyboardModifiers.None, holdCycles, gapCycles, pollCycles, returnExtra, options));
                continue;
            }

            char mapped = options.ForceUppercase && char.IsLetter(ch) && char.IsLower(ch)
                ? char.ToUpperInvariant(ch)
                : ch;

            if (sink.TryGetKeystroke(mapped, out HostKeystroke keystroke))
            {
                keys.Add(MakeTyped(keystroke.Key, keystroke.Modifiers, holdCycles, gapCycles, pollCycles, returnExtra, options));
                continue;
            }

            if (options.Unmappable == UnmappableCharacterPolicy.Reject)
            {
                rejected = true;
                skipped.Add(ch);
                continue;
            }

            if (options.Unmappable == UnmappableCharacterPolicy.Replace)
            {
                char replacement = options.ForceUppercase && char.IsLetter(options.ReplacementChar) && char.IsLower(options.ReplacementChar)
                    ? char.ToUpperInvariant(options.ReplacementChar)
                    : options.ReplacementChar;
                if (replacement != ch && sink.TryGetKeystroke(replacement, out HostKeystroke replaced))
                {
                    keys.Add(MakeTyped(replaced.Key, replaced.Modifiers, holdCycles, gapCycles, pollCycles, returnExtra, options));
                    continue;
                }

                skipped.Add(ch);
                continue;
            }

            skipped.Add(ch);
        }

        if (rejected)
        {
            return new TypingPlan(new TypeTextReceipt(0, skipped, true), [], true);
        }

        return new TypingPlan(new TypeTextReceipt(keys.Count, skipped, false), keys.ToArray(), false);
    }

    private TypedKey MakeTyped(
        KeyboardKey key,
        KeyboardModifiers modifiers,
        ulong holdCycles,
        ulong gapCycles,
        ulong pollCycles,
        ulong returnExtra,
        TypeTextOptions options) =>
        new(
            key,
            modifiers,
            holdCycles,
            gapCycles,
            pollCycles,
            key == KeyboardKey.Return ? returnExtra : 0,
            options.WaitForConsumption);

    private ulong MillisecondsToCycles(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return 0;
        }

        // Round down, but keep at least one cycle. A 0-cycle delay is due on the current cycle,
        // and DispatchEventsUntil would run it again immediately.
        long cycles = (long)(milliseconds * cpuClockHz / 1000.0);
        return cycles < 1 ? 1UL : (ulong)cycles;
    }

    private void Validate(TypeTextOptions options)
    {
        if (options.HoldMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.HoldMs, "HoldMs cannot be negative.");
        }

        if (options.GapMs is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.GapMs, "GapMs cannot be negative.");
        }

        if (options.StrobeWaitPollMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.StrobeWaitPollMs, "StrobeWaitPollMs must be at least 1.");
        }

        if (options.ReturnDelayMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ReturnDelayMs, "ReturnDelayMs cannot be negative.");
        }
    }

    private readonly record struct WorkItem(
        WorkKind Kind,
        KeyboardKey Key,
        KeyboardModifiers Modifiers,
        int Generation,
        TypedKey[]? Keys)
    {
        public static WorkItem KeyDown(KeyboardKey key, KeyboardModifiers modifiers) =>
            new(WorkKind.KeyDown, key, modifiers, 0, null);

        public static WorkItem KeyUp(KeyboardKey key, KeyboardModifiers modifiers) =>
            new(WorkKind.KeyUp, key, modifiers, 0, null);

        public static WorkItem Cancel() =>
            new(WorkKind.Cancel, KeyboardKey.None, KeyboardModifiers.None, 0, null);

        public static WorkItem Type(TypedKey[] keys) =>
            new(WorkKind.Type, KeyboardKey.None, KeyboardModifiers.None, 0, keys);

        public WorkItem WithGeneration(int generation) => this with { Generation = generation };
    }

    private readonly record struct TypedKey(
        KeyboardKey Key,
        KeyboardModifiers Modifiers,
        ulong HoldCycles,
        ulong GapCycles,
        ulong PollCycles,
        ulong ReturnExtraCycles,
        bool WaitForConsumption);

    private readonly record struct TypingPlan(TypeTextReceipt Receipt, TypedKey[] Keys, bool Rejected);

    private sealed class JournalView : IKeyboardInputJournal
    {
        private readonly JournalEntry[] items;

        public JournalView(JournalEntry[] items) => this.items = items;

        public int Count => items.Length;

        public JournalEntry this[int index] => items[index];

        public IEnumerator<JournalEntry> GetEnumerator() => ((IEnumerable<JournalEntry>)items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }
}