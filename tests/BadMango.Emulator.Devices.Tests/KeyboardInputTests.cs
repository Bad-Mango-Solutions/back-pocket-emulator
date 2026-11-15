// <copyright file="KeyboardInputTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;

/// <summary>
/// Unit tests for <see cref="KeyboardInput"/> with a fake scheduler and dispatcher.
/// </summary>
[TestFixture]
public sealed class KeyboardInputTests
{
    private const double CpuClockHz = 1_020_484d;
    private const ulong HoldCycles = 51_024;
    private const ulong GapCycles = 10_204;
    private const ulong PollCycles = 1_020;

    /// <summary>
    /// Verifies that keys posted from a background thread are applied only after the dispatcher
    /// runs, in FIFO order, and on the dispatcher thread.
    /// </summary>
    [Test]
    public void KeyEvents_FromBackgroundThread_ApplyInsideDispatcherInFifoOrder()
    {
        var sink = new FakeSink();
        var scheduler = new ManualScheduler();
        var dispatcher = new QueueDispatcher();
        var input = new KeyboardInput(sink, scheduler, dispatcher, CpuClockHz);
        int backgroundThread = 0;

        var worker = new Thread(() =>
        {
            backgroundThread = Environment.CurrentManagedThreadId;
            input.KeyDown(KeyboardKey.A, KeyboardModifiers.None);
            input.KeyDown(KeyboardKey.B, KeyboardModifiers.None);
        });
        worker.Start();
        Assert.That(worker.Join(TimeSpan.FromSeconds(5)), Is.True);

        Assert.That(sink.Calls, Is.Empty, "Nothing is applied on the background thread");

        dispatcher.Run();
        Assert.That(sink.Calls, Is.Empty, "The drain only schedules DeferredWork");

        scheduler.DispatchDue();

        Assert.Multiple(() =>
        {
            Assert.That(sink.Calls.Select(call => call.Key).ToArray(), Is.EqualTo(new[] { KeyboardKey.A, KeyboardKey.B }));
            Assert.That(sink.Calls.All(call => call.ThreadId == dispatcher.ThreadId), Is.True);
            Assert.That(sink.Calls.All(call => call.ThreadId != backgroundThread), Is.True);
            Assert.That(input.Journal.Select(entry => entry.Event.Kind).ToArray(), Is.EqualTo(new[] { InputEventKind.KeyDown, InputEventKind.KeyDown }));
        });
    }

    /// <summary>
    /// Verifies that a repeated key-down without a key-up is ignored.
    /// </summary>
    [Test]
    public void KeyDown_RepeatedWithoutKeyUp_IsIgnored()
    {
        var harness = Create();

        harness.Input.KeyDown(KeyboardKey.A, KeyboardModifiers.None);
        harness.Input.KeyDown(KeyboardKey.A, KeyboardModifiers.Shift);
        harness.Scheduler.DispatchDue();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Sink.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Sink.Calls[0].Modifiers, Is.EqualTo(KeyboardModifiers.None));
            Assert.That(harness.Input.Journal, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// Verifies the exact cycle spacing of a two-character <see cref="KeyboardInput.TypeText"/> at 1,020,484 Hz.
    /// </summary>
    [Test]
    public void TypeText_AB_SchedulesHoldAndGapAtExactCycles()
    {
        var harness = Create();
        TypeTextReceipt receipt = harness.Input.TypeText("AB", new TypeTextOptions { HoldMs = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(receipt.Accepted, Is.EqualTo(2));
            Assert.That(harness.Input.IsTyping, Is.True);
            Assert.That(harness.Sink.Calls, Is.Empty);
        });

        harness.Scheduler.DispatchDue();
        harness.Scheduler.Advance(HoldCycles);
        harness.Scheduler.Advance(GapCycles);

        JournalEntry[] typed = harness.Input.Journal.Where(entry => entry.Event.Kind is InputEventKind.TypedKeyDown or InputEventKind.TypedKeyUp).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(typed.Select(entry => (entry.Cycle.Value, entry.Event.Kind, entry.Event.Key)).ToArray(), Is.EqualTo(new (ulong, InputEventKind, KeyboardKey)[]
            {
                (0, InputEventKind.TypedKeyDown, KeyboardKey.A),
                (HoldCycles, InputEventKind.TypedKeyUp, KeyboardKey.A),
                (HoldCycles + GapCycles, InputEventKind.TypedKeyDown, KeyboardKey.B),
            }));
        });

        harness.Scheduler.Advance(HoldCycles);
        Assert.That(harness.Input.IsTyping, Is.False);
    }

    /// <summary>
    /// Verifies that the next key waits while the strobe is set, and that clearing it does not start the key.
    /// </summary>
    [Test]
    public void TypeText_WaitForConsumption_DoesNotStartNextKeyWhenStrobeClears()
    {
        var harness = Create();
        harness.Input.TypeText("AB", new TypeTextOptions { HoldMs = 50, WaitForConsumption = true, StrobeWaitPollMs = 1 });
        harness.Scheduler.DispatchDue();
        harness.Scheduler.Advance(HoldCycles);

        harness.Sink.Ready = false;
        harness.Scheduler.Advance(GapCycles);
        int callsWhileBlocked = harness.Sink.Calls.Count;

        harness.Sink.Ready = true;
        int callsAfterClear = harness.Sink.Calls.Count;

        Assert.That(callsAfterClear, Is.EqualTo(callsWhileBlocked), "Clearing the strobe does not start the next key");
        Assert.That(harness.Sink.Calls.Any(call => call.Key == KeyboardKey.B && call.Down), Is.False);

        harness.Scheduler.Advance(PollCycles);

        Assert.That(harness.Sink.Calls.Any(call => call.Key == KeyboardKey.B && call.Down), Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="KeyboardInput.CancelTyping"/> drops the queue, releases the typed key,
    /// and ignores events from the cancelled generation.
    /// </summary>
    [Test]
    public void CancelTyping_DropsQueueReleasesKeyAndIgnoresStaleEvents()
    {
        var harness = Create();
        harness.Input.TypeText("AB", new TypeTextOptions { HoldMs = 50 });
        harness.Scheduler.DispatchDue();
        Assume.That(harness.Sink.Held, Is.EqualTo(KeyboardKey.A));

        harness.Input.CancelTyping();
        harness.Scheduler.DispatchDue();
        harness.Scheduler.Advance(1_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Sink.Held, Is.Null);
            Assert.That(harness.Input.IsTyping, Is.False);
            Assert.That(harness.Sink.Calls.Any(call => call.Key == KeyboardKey.B), Is.False);
            Assert.That(harness.Input.Journal.Any(entry => entry.Event.Kind == InputEventKind.CancelTyping), Is.True);
        });
    }

    /// <summary>
    /// Verifies that RESET drops queued text, releases the typed key, and stale timers do not fire.
    /// </summary>
    [Test]
    public void Reset_DropsQueueReleasesKeyAndIgnoresStaleEvents()
    {
        var harness = Create();
        harness.Input.TypeText("AB", new TypeTextOptions { HoldMs = 50 });
        harness.Scheduler.DispatchDue();
        Assume.That(harness.Sink.Held, Is.EqualTo(KeyboardKey.A));

        harness.Input.OnResetAsserted(harness.Scheduler.Now);
        harness.Scheduler.Advance(1_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Sink.Held, Is.Null);
            Assert.That(harness.Input.IsTyping, Is.False);
            Assert.That(harness.Sink.Calls.Any(call => call.Key == KeyboardKey.B), Is.False);
            Assert.That(harness.Input.Journal.Any(entry => entry.Event.Kind == InputEventKind.CancelTyping), Is.True);
        });
    }

    /// <summary>
    /// Verifies that CR+LF is one Return and that an uppercase letter is typed with Shift.
    /// </summary>
    [Test]
    public void TypeText_CrLfIsOneReturn_AndUppercaseUsesShift()
    {
        var harness = Create();
        TypeTextReceipt newlines = harness.Input.TypeText("\r\n");
        TypeTextReceipt upper = harness.Input.TypeText("A");
        harness.Scheduler.DispatchDue();
        harness.Scheduler.Advance(HoldCycles);
        harness.Scheduler.Advance(GapCycles);

        JournalEntry[] downs = harness.Input.Journal.Where(entry => entry.Event.Kind == InputEventKind.TypedKeyDown).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(newlines.Accepted, Is.EqualTo(1));
            Assert.That(upper.Accepted, Is.EqualTo(1));
            Assert.That(downs, Has.Length.EqualTo(2));
            Assert.That(downs[0].Event.Key, Is.EqualTo(KeyboardKey.Return));
            Assert.That(downs[0].Event.Modifiers, Is.EqualTo(KeyboardModifiers.None));
            Assert.That(downs[1].Event.Key, Is.EqualTo(KeyboardKey.A));
            Assert.That(downs[1].Event.Modifiers, Is.EqualTo(KeyboardModifiers.Shift));
        });
    }

    /// <summary>
    /// Verifies Skip, Replace, and Reject receipts, including that Reject queues nothing.
    /// </summary>
    [Test]
    public void TypeText_UnmappablePolicies_ProduceReceipts()
    {
        const char Unmappable = '\u2603';

        var skip = Create();
        TypeTextReceipt skipped = skip.Input.TypeText($"A{Unmappable}B", new TypeTextOptions { Unmappable = UnmappableCharacterPolicy.Skip });
        RunTyping(skip.Scheduler);

        var replace = Create();
        TypeTextReceipt replaced = replace.Input.TypeText($"A{Unmappable}", new TypeTextOptions
        {
            Unmappable = UnmappableCharacterPolicy.Replace,
            ReplacementChar = '?',
        });
        RunTyping(replace.Scheduler);

        var reject = Create();
        TypeTextReceipt rejected = reject.Input.TypeText($"A{Unmappable}B", new TypeTextOptions { Unmappable = UnmappableCharacterPolicy.Reject });
        reject.Scheduler.DispatchDue();
        reject.Scheduler.Advance(1_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(skipped.Accepted, Is.EqualTo(2));
            Assert.That(skipped.Rejected, Is.False);
            Assert.That(skipped.Skipped, Is.EqualTo(new[] { Unmappable }));
            Assert.That(skip.Sink.Calls.Where(call => call.Down).Select(call => call.Key).ToArray(), Is.EqualTo(new[] { KeyboardKey.A, KeyboardKey.B }));

            Assert.That(replaced.Accepted, Is.EqualTo(2));
            Assert.That(replaced.Rejected, Is.False);
            Assert.That(replaced.Skipped, Is.Empty);
            Assert.That(replace.Sink.Calls.Where(call => call.Down).Select(call => call.Key).ToArray(), Is.EqualTo(new[] { KeyboardKey.A, KeyboardKey.Slash }));
            Assert.That(replace.Sink.Calls.First(call => call.Key == KeyboardKey.Slash).Modifiers, Is.EqualTo(KeyboardModifiers.Shift));

            Assert.That(rejected.Accepted, Is.EqualTo(0));
            Assert.That(rejected.Rejected, Is.True);
            Assert.That(rejected.Skipped, Is.EqualTo(new[] { Unmappable }));
            Assert.That(reject.Sink.Calls, Is.Empty);
            Assert.That(reject.Input.IsTyping, Is.False);
        });
    }

    /// <summary>
    /// Verifies that typing restores the live modifier snapshot when it finishes.
    /// </summary>
    [Test]
    public void TypeText_RestoresLiveModifierSnapshot()
    {
        var harness = Create();
        harness.Input.KeyDown(KeyboardKey.LeftOption, KeyboardModifiers.OpenApple);
        harness.Scheduler.DispatchDue();
        Assume.That(harness.Sink.Modifiers, Is.EqualTo(KeyboardModifiers.OpenApple));

        harness.Input.TypeText("A", new TypeTextOptions { HoldMs = 50 });
        harness.Scheduler.DispatchDue();
        Assume.That(harness.Sink.Modifiers, Is.EqualTo(KeyboardModifiers.Shift));

        harness.Scheduler.Advance(HoldCycles);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Sink.Held, Is.Null);
            Assert.That(harness.Sink.Modifiers, Is.EqualTo(KeyboardModifiers.OpenApple));
            Assert.That(harness.Input.IsTyping, Is.False);
        });
    }

    /// <summary>
    /// Verifies that <see cref="TypeTextOptions.ReturnDelayMs"/> is added to the gap after Return.
    /// </summary>
    [Test]
    public void TypeText_ReturnDelay_ExtendsGapAfterReturn()
    {
        var harness = Create();
        harness.Input.TypeText("\rA", new TypeTextOptions { HoldMs = 50, ReturnDelayMs = 10 });
        harness.Scheduler.DispatchDue();
        harness.Scheduler.Advance(HoldCycles);
        harness.Scheduler.Advance(GapCycles);

        int downsBeforeExtra = harness.Input.Journal.Count(entry => entry.Event.Kind == InputEventKind.TypedKeyDown);
        harness.Scheduler.Advance(GapCycles);
        JournalEntry next = harness.Input.Journal.Last(entry => entry.Event.Kind == InputEventKind.TypedKeyDown);

        Assert.Multiple(() =>
        {
            Assert.That(downsBeforeExtra, Is.EqualTo(1));
            Assert.That(next.Event.Key, Is.EqualTo(KeyboardKey.A));
            Assert.That(next.Cycle.Value, Is.EqualTo(HoldCycles + (GapCycles * 2)));
        });
    }

    /// <summary>
    /// Verifies that <see cref="TypeTextOptions.ForceUppercase"/> types lowercase letters with Shift.
    /// </summary>
    [Test]
    public void TypeText_ForceUppercase_TypesLettersWithShift()
    {
        var harness = Create();
        harness.Input.TypeText("ab", new TypeTextOptions { ForceUppercase = true });
        RunTyping(harness.Scheduler);

        InputEvent[] downs = harness.Input.Journal.Select(entry => entry.Event).Where(evt => evt.Kind == InputEventKind.TypedKeyDown).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(downs[0].Key, Is.EqualTo(KeyboardKey.A));
            Assert.That(downs[0].Modifiers, Is.EqualTo(KeyboardModifiers.Shift));
            Assert.That(downs[1].Key, Is.EqualTo(KeyboardKey.B));
            Assert.That(downs[1].Modifiers, Is.EqualTo(KeyboardModifiers.Shift));
        });
    }

    /// <summary>
    /// Verifies that a null dispatcher still applies the batch through deferred work.
    /// </summary>
    [Test]
    public void KeyDown_NullDispatcher_AppliesThroughDeferredWork()
    {
        var sink = new FakeSink();
        var scheduler = new ManualScheduler();
        var input = new KeyboardInput(sink, scheduler, dispatcher: null, CpuClockHz);

        input.KeyDown(KeyboardKey.C, KeyboardModifiers.Control);
        Assert.That(sink.Calls, Is.Empty);

        scheduler.DispatchDue();

        Assert.Multiple(() =>
        {
            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            Assert.That(sink.Calls[0].Key, Is.EqualTo(KeyboardKey.C));
            Assert.That(sink.Calls[0].Modifiers, Is.EqualTo(KeyboardModifiers.Control));
        });
    }

    private static void RunTyping(ManualScheduler scheduler)
    {
        scheduler.DispatchDue();
        scheduler.Advance(2_000_000);
    }

    private static (KeyboardInput Input, FakeSink Sink, ManualScheduler Scheduler) Create()
    {
        var sink = new FakeSink();
        var scheduler = new ManualScheduler();
        var input = new KeyboardInput(sink, scheduler, new InlineDispatcher(), CpuClockHz);
        return (input, sink, scheduler);
    }

    private sealed class FakeSink : IHostKeyboardSink
    {
        public List<Call> Calls { get; } = [];

        public KeyboardModifiers Modifiers { get; private set; }

        public KeyboardKey? Held { get; private set; }

        public bool Ready { get; set; } = true;

        public bool IsReadyForTypedKey => Ready;

        public void OnHostKeyDown(KeyboardKey key, KeyboardModifiers modifiers)
        {
            Calls.Add(new Call(key, modifiers, true, Environment.CurrentManagedThreadId));
            Modifiers = modifiers;
            if (CountsAsHeld(key))
            {
                Held = key;
            }
        }

        public void OnHostKeyUp(KeyboardKey key, KeyboardModifiers modifiers)
        {
            Calls.Add(new Call(key, modifiers, false, Environment.CurrentManagedThreadId));
            Modifiers = modifiers;
            if (Held == key)
            {
                Held = null;
            }
        }

        public bool TryGetKeystroke(char c, out HostKeystroke keystroke)
        {
            if (c is >= 'a' and <= 'z')
            {
                keystroke = new HostKeystroke(KeyboardKey.A + (c - 'a'), KeyboardModifiers.None);
                return true;
            }

            if (c is >= 'A' and <= 'Z')
            {
                keystroke = new HostKeystroke(KeyboardKey.A + (c - 'A'), KeyboardModifiers.Shift);
                return true;
            }

            if (c == '?')
            {
                keystroke = new HostKeystroke(KeyboardKey.Slash, KeyboardModifiers.Shift);
                return true;
            }

            keystroke = default;
            return false;
        }

        public void ReleaseAllTypedKeys()
        {
            Calls.Add(new Call(KeyboardKey.None, Modifiers, false, Environment.CurrentManagedThreadId));
        }

        private bool CountsAsHeld(KeyboardKey key) =>
            key is (>= KeyboardKey.A and <= KeyboardKey.Z)
                or KeyboardKey.Slash
                or KeyboardKey.Return
                or KeyboardKey.Tab
                or KeyboardKey.Backspace;

        public readonly record struct Call(KeyboardKey Key, KeyboardModifiers Modifiers, bool Down, int ThreadId);
    }

    private sealed class InlineDispatcher : IMachineDispatcher
    {
        public bool IsOnMachineThread => true;

        public void Post(Action action) => action();
    }

    private sealed class QueueDispatcher : IMachineDispatcher
    {
        private readonly Queue<Action> actions = new();

        public bool IsOnMachineThread => false;

        public int ThreadId { get; private set; }

        public void Post(Action action) => actions.Enqueue(action);

        public void Run()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            while (actions.Count > 0)
            {
                actions.Dequeue()();
            }
        }
    }

    private sealed class ManualScheduler : IScheduler
    {
        private readonly List<Scheduled> events = [];
        private ulong now;
        private ulong nextId = 1;

        public Cycle Now => now;

        public int PendingEventCount => events.Count(evt => !evt.Consumed);

        public EventHandle ScheduleAt(Cycle due, ScheduledEventKind kind, int priority, Action<IEventContext> callback, object? tag = null)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var handle = new EventHandle(nextId++);
            events.Add(new Scheduled(handle, due, kind, priority, callback));
            return handle;
        }

        public EventHandle ScheduleAfter(Cycle delta, ScheduledEventKind kind, int priority, Action<IEventContext> callback, object? tag = null) =>
            ScheduleAt(now + delta, kind, priority, callback, tag);

        public bool Cancel(EventHandle handle)
        {
            Scheduled? match = events.FirstOrDefault(evt => evt.Handle == handle && !evt.Consumed);
            if (match is null)
            {
                return false;
            }

            match.Consumed = true;
            return true;
        }

        public void Advance(Cycle delta)
        {
            if (delta == 0)
            {
                return;
            }

            ulong target = now + delta;
            DispatchUntil(target);
            now = target;
        }

        public void DispatchDue() => DispatchUntil(now);

        public Cycle? PeekNextDue()
        {
            Scheduled? next = FindNext(ulong.MaxValue);
            return next is null ? null : next.Due;
        }

        public bool JumpToNextEventAndDispatch()
        {
            Scheduled? next = FindNext(ulong.MaxValue);
            if (next is null)
            {
                return false;
            }

            if (next.Due > now)
            {
                now = next.Due;
            }

            DispatchUntil(now);
            return true;
        }

        public void FlushPending()
        {
            foreach (Scheduled evt in events)
            {
                if (!evt.Consumed && evt.Kind != ScheduledEventKind.DeferredWork)
                {
                    evt.Consumed = true;
                }
            }
        }

        public void Reset()
        {
            events.Clear();
            now = 0;
        }

        private void DispatchUntil(ulong target)
        {
            while (FindNext(target) is Scheduled next)
            {
                next.Consumed = true;
                if (next.Due > now)
                {
                    now = next.Due;
                }

                next.Callback(null!);
            }
        }

        private Scheduled? FindNext(ulong target)
        {
            Scheduled? next = null;
            foreach (Scheduled evt in events)
            {
                if (evt.Consumed || evt.Due > target)
                {
                    continue;
                }

                if (next is null
                    || evt.Due < next.Due
                    || (evt.Due == next.Due && evt.Priority < next.Priority)
                    || (evt.Due == next.Due && evt.Priority == next.Priority && evt.Handle.Id < next.Handle.Id))
                {
                    next = evt;
                }
            }

            return next;
        }

        private sealed class Scheduled(EventHandle handle, ulong due, ScheduledEventKind kind, int priority, Action<IEventContext> callback)
        {
            public EventHandle Handle { get; } = handle;

            public ulong Due { get; } = due;

            public ScheduledEventKind Kind { get; } = kind;

            public int Priority { get; } = priority;

            public Action<IEventContext> Callback { get; } = callback;

            public bool Consumed { get; set; }
        }
    }
}