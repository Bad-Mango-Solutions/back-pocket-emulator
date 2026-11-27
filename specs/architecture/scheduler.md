# Scheduler and Timing

## Summary

This page describes the emulator's cycle clock, discrete-event queue, and the
components that advance or consume machine time. The scheduler is the
single source of simulated time: it advances only when CPU work or another
machine-thread operation explicitly supplies cycles.

## Covers

- [`Cycle`](#61-core-concepts--implemented) and scheduled-event kinds.
- [`IScheduler`](#62-scheduler-interface--implemented) and the concrete event queue.
- [`Cpu65C02`](#63-cpu-scheduler-integration--partly-implemented) as the implemented CPU-to-scheduler client.
- [`VideoDevice`](#63-cpu-scheduler-integration--partly-implemented) VBlank scheduling as a device client.
- [Scheduler reset Gap 4](#gap-4-scheduler-reset--partly-implemented).

## Does not cover

- The CPU instruction implementation or opcode timing details; see the CPU architecture page.
- Memory routing and MMU behavior.
- Signal-line semantics and interrupt sources.
- Device I/O and soft-switch registration.
- Wall-clock rendering or host-thread scheduling; host work is marshalled onto the machine thread.

## Status and target layout

The target source assembly is `BadMango.Emulator.Core` for the scheduler and
cycle-clock types. `Scheduler` and `IScheduler` keep their existing
`BadMango.Emulator.Bus` and `BadMango.Emulator.Bus.Interfaces` namespaces
during the assembly fold.

The status names below use the #536 audit. They describe the implementation
that exists now, not the broader text in the legacy v1.0 specification.

`BadMango.Emulator.Devices` retains `VideoDevice`; it is a scheduler client,
not part of the target `Core` fold. Its tests remain in
`BadMango.Emulator.Devices.Tests`.

## 6.1 Core Concepts — **Implemented**

[`Cycle`](../../src/BadMango.Emulator.Core/Cycle.cs) is the authoritative
machine-time value. It is a `ulong`-backed, type-safe cycle count with
arithmetic and comparison operators. `IScheduler.Now` and event callbacks
observe this same clock; no wall-clock timer advances it implicitly.

[`ScheduledEventKind`](../../src/BadMango.Emulator.Core/ScheduledEventKind.cs)
classifies scheduled work for profiling and diagnostics. The implementation
has the kinds represented in the v1.0 plan plus `None`, `VideoBlank`, and
`DeferredWork`. Deferred work is host-initiated machine-thread work, such as
a disk media change, and is distinct from device timer state.

The scheduler is a discrete-event arbiter. It owns a priority queue ordered
by due cycle, priority, and a monotonically assigned sequence number. Lower
priority values dispatch first, and equal-cycle/equal-priority events retain
deterministic schedule order. Events are one-shot: dispatch removes them from
the queue.

## 6.2 Scheduler Interface — **Implemented**

[`IScheduler`](../../src/BadMango.Emulator.Core/Interfaces/IScheduler.cs) and
[`Scheduler`](../../src/BadMango.Emulator.Core/Scheduler.cs) provide the
implemented queue.

The interface can:

- report `Now` and `PendingEventCount`;
- schedule absolute or relative events and return an `EventHandle`;
- cancel an event;
- advance by a cycle delta with `Advance`;
- dispatch work already due with `DispatchDue`;
- inspect or jump to the next event with `PeekNextDue` and
  `JumpToNextEventAndDispatch`;
- flush pending work with `FlushPending`;
- reset the clock and queue with `Reset`.

`Advance(delta)` moves to the target cycle and dispatches events whose due
cycle is reached, so callbacks execute at their scheduled cycle even when the
caller jumps across several cycles. `DispatchDue()` performs due-work dispatch
without moving the clock.

Callbacks receive [`IEventContext`](../../src/BadMango.Emulator.Core/Interfaces/IEventContext.cs),
which exposes the current cycle, scheduler, signal bus, memory bus, and
registered machine components. The context is set on the concrete scheduler
before dispatch.

The scheduler is single-threaded and owned by the machine thread. Scheduling,
cancellation, advancement, flushing, and callbacks must occur on that thread.
Host threads and UI or REPL code must post work through the machine
dispatcher rather than touching the scheduler directly.

## 6.3 CPU-Scheduler Integration — **Partly implemented**

[`Cpu65C02`](../../src/BadMango.Emulator.Core/Cpu/Cpu65C02.cs) is the
implemented integration. After an instruction, trap, interrupt, RESET hold
cycle, or RESET release sequence consumes cycles, the CPU adds those cycles to
the context scheduler through `IScheduler.Advance`. The CPU caches the concrete
`Scheduler` for its hot path, but falls back to the `IScheduler` interface
when another implementation supplies the event context.

The 65C02 obtains cycle consumption from its TCU and clears that per-step
account once it has advanced the scheduler. The machine run loop consequently
drives time by executing CPU work; the scheduler itself is not a wall-clock
clock and does not run continuously.

The placeholder `Cpu65816` and `Cpu65832` implementations do not participate
in scheduler advancement yet. A future CPU must use the same explicit
cycle-commit model rather than claim cycle accuracy before an implementation
exists.

`Cpu65C02Tests` and the related CPU integration tests live in
`tests/BadMango.Emulator.Core.Tests` (namespace `BadMango.Emulator.Tests` unchanged).

### Device client: VideoDevice VBlank

[`VideoDevice`](../../src/BadMango.Emulator.Devices/VideoDevice.cs) remains in
`BadMango.Emulator.Devices` and uses `IEventContext.Scheduler` to schedule
VBlank start and end edges with `ScheduledEventKind.VideoBlank`.

The device's initialization schedules the next VBlank phase. The VBlank-start
callback sets the vertical-blanking state and schedules VBlank end; the
VBlank-end callback clears the state and schedules the next frame. RESET
assertion flushes the pending edge and `VideoDevice` re-arms the original
start or end cycle, preserving the frame phase rather than resetting video time.

## Gap 4: Scheduler Reset — **Partly implemented**

[`IScheduler.Reset`](../../src/BadMango.Emulator.Core/Interfaces/IScheduler.cs)
and [`Scheduler.Reset`](../../src/BadMango.Emulator.Core/Scheduler.cs) are
implemented: reset clears the event queue and cancelled handles and rewinds
`Now` to cycle zero. Handle IDs and sequence numbers remain monotonic across
resets so stale cancellations cannot collide with newly scheduled events.

The machine-reset behavior is more nuanced than a full scheduler rewind.
RESET assertion calls `FlushPending`, preserving `DeferredWork` and discarding
periodic machine events. Device reset receivers then re-arm periodic work,
including video VBlank, while the timeline continues instead of being reset
to zero. Hard reset clears power-on state and can schedule post-reset work,
but [`Machine.Reset`](../../src/BadMango.Emulator.Core/Machine.cs) does not
currently call `IScheduler.Reset`. Therefore the Gap 4 API exists, but the
machine's ordinary/warm RESET path still does not mean "rewind every machine
clock"; the current implementation is partly implemented.

Relevant reset coverage includes:

- [`SchedulerTests`](../../tests/BadMango.Emulator.Core.Tests/SchedulerTests.cs)
- [`MachineTests.Reset_HardReset_DeviceEventsArePresentAfterReset`](../../tests/BadMango.Emulator.Core.Tests/MachineTests.cs)
- [`Pocket2eIntegrationTests`](../../tests/BadMango.Emulator.Core.Tests/Pocket2eIntegrationTests.cs)
- [`ResetSignalMachineTests`](../../tests/BadMango.Emulator.Core.Tests/ResetSignalMachineTests.cs)

## Source references

- [Architecture Spec v1.0 Part 2 — Part VI](../Architecture%20Spec%20v1.0%20Part%202.md#part-vi-scheduler--timing)
- [Architecture Spec v1.0 Appendix — Gap 4](../Architecture%20Spec%20v1.0%20Appendix.md#gap-4-scheduler-reset--resolved)
- [Architecture restructure audit proposal](../Architecture%20Docs%20Restructure%20Proposal.md)
