# Emulation speed options

Design for [#444](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/444), under epic [#443](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/443). This page is the design. Nothing in it is implemented yet. The sub-issue list at the end is a proposal; those issues are not filed.

The governing rule for disk acceleration is the 2026-11-24 requirement on #444. Acceleration follows the software motor **request**. A `$C0n9` access accelerates. A `$C0n8` access restores the previous pace at once, unless another drive's motor is still requested on. The NE556 one-second hold added in [#680](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/pull/680) keeps the platter spinning so a quick re-access skips spin-up. It does not extend acceleration. Comments on #444 that say the Disk II has no spin-down are out of date; `DiskIIController` now arms that hold. The comments that still stand are: any slot, both reads and writes, no DOS or RWTS trap, one shared control the debugger can drive, a profile default, and a deferred on/off toggle while a request is active.

## Summary

Speed options change wall-clock pacing only. The emulated clock stays the profile `cpu.clockSpeed` (`ProfileCpuClockHz`). Devices keep counting the same cycles at every pace, so a program sees the same Disk II bit rate, the same VBlank period, the same speaker toggle cycles, and the same keyboard typing delays. `Cpu.Step` and `RunSlice` do not read the pace. `RunAsync` and `ResumeAsync` apply it once per slice.

Disk acceleration, when enabled, selects unthrottled pacing on a motor-on request and restores the user's selected pace on the last motor-off request. Burst mode, which would deliver nibbles in fewer than 32 cycles, is not part of this design.

## Current state

### Throttle loop

[`IMachine.ClockSpeedHz`](../../src/BadMango.Emulator.Bus/Interfaces/IMachine.cs) is the throttle target in hertz. Zero means unthrottled. [`Machine.RunAsync`](../../src/BadMango.Emulator.Bus/Machine.cs) and `ResumeAsync` both enter `ExecuteLoopAsync`. That loop calls `RunSlice`, then, when `ClockSpeedHz > 0`, compares a `Stopwatch` with the cycles accumulated since the window opened:

- `expectedMs = throttleCycleBase * 1000.0 / clockSpeed`
- Sleep when the machine is at least 1 ms ahead (`Task.Delay`).
- When the machine falls more than 100 ms behind, restart the stopwatch and zero `throttleCycleBase` so a pause does not become a catch-up burst.

`RunSlice` returns after `YieldIntervalCycles` (10,000) cycles, or earlier on WAI-with-nothing-scheduled, STP/halt, or a stop request. [`Machine.Run`](../../src/BadMango.Emulator.Bus/Machine.cs) calls the same slice in a tight loop and never sleeps. `Step` advances one instruction. Neither path reads `ClockSpeedHz`.

### Profile clock versus throttle

[`MachineBuilder.FromProfile`](../../src/BadMango.Emulator.Bus/MachineBuilder.FromProfile.cs) copies `cpu.clockSpeed` twice:

- `clockSpeedHz = profile.Cpu.ClockSpeed ?? 0`, later assigned to `Machine.ClockSpeedHz`. Omitted `clockSpeed` leaves the run loop unthrottled.
- `profileCpuClockHz` is `cpu.clockSpeed` when that value is positive, otherwise `DefaultProfileCpuClockHz` (1,000,000). [`MachineBuilder.ProfileCpuClockHz`](../../src/BadMango.Emulator.Bus/MachineBuilder.cs) is the clock used to turn emulated milliseconds into cycles. It is fixed at build.

Shipped Pocket2e profiles set `clockSpeed` to 1,020,484. `simple-65c02` sets 1,000,000. The schema field is `cpu.clockSpeed` (`schemas/machine-profile.schema.json`); there is no separate pacing field and no disk-acceleration flag.

### Scheduler

[`Scheduler`](../../src/BadMango.Emulator.Bus/Scheduler.cs) is a cycle queue. `Now` advances only when the CPU, or another machine-thread caller, supplies cycles. It has no wall-clock timer. [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs) calls `IScheduler.Advance` with the cycles the instruction consumed. Device deadlines (VBlank, the NE556 hold, keyboard typing) are cycle counts on that queue. See [`scheduler.md`](scheduler.md).

### Speaker

[`SpeakerController`](../../src/BadMango.Emulator.Devices/Speaker/SpeakerController.cs) records a toggle on each `$C030` access, stamped with the scheduler cycle. PCM conversion is separate, in `AppendStateSegment`. `GetCyclesPerSecond` uses `IMachine.ClockSpeedHz` when it is positive, and 1,020,484 when it is zero. A finite throttle therefore changes pitch: the doubled-clock test expects 1,020 cycles at 2,040,968 Hz to become 22 samples instead of 44. An unthrottled run still converts at 1,020,484 and clamps the buffer to two seconds (`MaxAudioBufferSamples` = 88,200), dropping the rest. A long unthrottled stretch can fill that buffer with a stale segment.

### VBlank

[`VideoDevice`](../../src/BadMango.Emulator.Devices/VideoDevice.cs) schedules VBlank on the cycle queue. `CyclesPerFrame` is 17,030 and `VBlankDurationCycles` is 4,550. The schedule does not read `ClockSpeedHz`. `$C019` follows those edges. [`VideoWindow.OnVBlankOccurred`](../../src/BadMango.Emulator.Debug.UI/Views/VideoWindow.axaml.cs) runs on the machine thread. Every VBlank captures a display snapshot, then queues `ProcessVBlankFrame` only when `vblankPending` transitions from 0 to 1. Further VBlanks while that render is queued refresh the snapshot and do not post another `InvokeAsync`. `ProcessVBlankFrame` clears `vblankPending` before it paints, so a VBlank that arrives during the paint can queue the next one. There is no host-time cap. A UI that finishes each paint before the next VBlank presents every emulated frame. When VBlank outruns the UI, intermediate notifications are dropped and the paint shows the latest snapshot.

### Disk II motor and the NE556 hold

[`DiskIIController`](../../src/BadMango.Emulator.Devices/DiskIIController.cs) decodes sixteen soft switches at `$C0n0–$C0nF` (`n = slot + 8`). Motor-off is offset `0x08`, motor-on is offset `0x09`. Reads and writes both call `SetMotor`. Accesses with `BusAccess.IsSideEffectFree` do not.

`SetMotor(true)` cancels a pending hold. If the platter is already on, it returns. A real off-to-on transition sets `motorOn`, counts `motorOnCount`, and arms spin-up settle (`DefaultMotorSettleCycles` = 1,000).

`SetMotor(false)` does not clear `motorOn`. It arms `ArmMotorOffDelay`: a `DeviceTimer` `ScheduleAfter` of `motorOffDelayCycles` (`DefaultMotorOffDelayCycles` = 1,000,000, about one second at 1 MHz; Sather, p. 9-13). `motorOffPending` stays true until `OnMotorOffDue` calls `StopMotor`, or a later `$C0n9` cancels the event. A further `$C0n8` while the timer is running restarts the one-second hold from that access. A delay of 0, or a missing scheduler, stops the platter on the access. The device specification is [`Disk II Controller Device Specification.md`](../os/Disk%20II%20Controller%20Device%20Specification.md) §2.4.

One latch per controller. Drive select (`$C0nA` / `$C0nB`) steers that enable. During the hold the NE556 output is still asserted, so a select moves the spinning platter to the other drive and leaves the pending stop at its original deadline.

`OnResetAsserted` and power-on `Reset` clear `motorOn` and the pending hold immediately. RESET does not wait out the one-second timer.

Nibble timing is `CyclesPerByte` = 32 (4 µs × 8 at the nominal 1 MHz clock). Spin position is recomputed on data-register reads from elapsed cycles. Nothing in the controller reads `ClockSpeedHz`.

There is no motor-request flag distinct from `motorOn`. During the hold, `motorOn` and `motorOffPending` are both true, which is the physical coast, not the software request.

### Keyboard

[`AppleIIeKeyboard`](../../src/BadMango.Emulator.Devices/AppleIIeKeyboard.cs) is a strobe at `$C000` / `$C010`. It has no repeat timer. [`KeyboardInput`](../../src/BadMango.Emulator.Devices/KeyboardInput.cs) drops a duplicate host key-down and does not synthesize repeats. `TypeText` converts milliseconds with the captured `ProfileCpuClockHz`: `(long)(ms * cpuClockHz / 1000)`, rounded down. It does not read `ClockSpeedHz`. [`DeviceFactoryExtensions`](../../src/BadMango.Emulator.Devices/DeviceFactoryExtensions.cs) passes `ProfileCpuClockHz` for that reason: a zero throttle would turn every delay into zero cycles.

[`ResetCommand`](../../src/BadMango.Emulator.Debug.Infrastructure/Commands/ResetCommand.cs) converts a 500 ms Open-Apple sampling window with `ClockSpeedHz`, or 1,000,000 when the throttle is zero. That couples a wall-clock control to a cycle delay.

## Speed model

`cpu.clockSpeed` stays the emulated clock. Speed presets write the throttle only. A real Zip Chip or TransWarp changes φ2 and has to drop to 1 MHz around the Disk II sequencer. This epic does the other job: the same cycles happen sooner on the wall clock, including during a disk transfer. Cycle-accurate device behavior is identical at every speed. Only wall-clock pacing changes.

| Name | Role | Changes when the user picks a speed |
| --- | --- | --- |
| Emulated clock | `ProfileCpuClockHz`, from `cpu.clockSpeed`, or 1,000,000 when that value is omitted or not positive | No. Fixed for the life of the machine. |
| Selected pace | `IMachine.ClockSpeedHz`. Zero is max (unthrottled). | Yes. This is the user's speed. |
| Effective pace | What `ExecuteLoopAsync` samples. Zero while disk acceleration is active, otherwise the selected pace. | Derived. Disk acceleration does not write `ClockSpeedHz`. |

Initialization stays compatible. With the new fields omitted, `FromProfile` still sets `ClockSpeedHz` from `cpu.clockSpeed ?? 0` and `ProfileCpuClockHz` as it does today. Two optional fields cover a profile that wants a stock emulated clock and a different startup pace:

- `cpu.pacingHz` (integer, minimum 0). Present: initial `ClockSpeedHz`, including 0 for max. Absent: today's `clockSpeed ?? 0`.
- `cpu.accelerateOnDiskAccess` (boolean). Absent or false: the feature starts off.

Presets write `ClockSpeedHz` and nothing else:

| Preset | `ClockSpeedHz` |
| --- | --- |
| Stock | `ProfileCpuClockHz` for that machine (1,020,484 on shipped Pocket2e profiles, 1,000,000 on `simple-65c02`) |
| 2.8 MHz | 2,800,000 |
| 4 MHz | 4,000,000 |
| Custom | any positive hertz value |
| Max | 0 |

Display names follow the value: 0 is Max, a value equal to the emulated clock is Stock, 2,800,000 is 2.8 MHz, 4,000,000 is 4 MHz, and any other positive value is Custom. The labels are pacing names. They are not φ2 modes and they do not retune Disk II, VBlank, or typing delays.

### What each device does

| Device | Cycle behavior, identical at every pace | Wall clock |
| --- | --- | --- |
| Disk II | 32 cycles per nibble. Settle 1,000 / 30,000 cycles. NE556 hold 1,000,000 cycles from the `$C0n8` access. Byte-ready and `$C0nC` data stay on those cycle offsets, including during the hold. | A transfer finishes sooner while acceleration is active. The hold then runs at the restored pace. |
| Speaker | Toggle recorded on the access cycle. `$C030` handler unchanged. | Finite effective pace: PCM divisor is that pace, so pitch and tempo scale and one wall second of audio matches one wall second of cycles. Effective pace 0: mute. See [Video and audio](#video-and-audio). |
| VBlank | Next edge every 17,030 cycles, blank for 4,550. `$C019` unchanged. | The host paints the latest frame at the display rate. See [Video and audio](#video-and-audio). |
| Keyboard | No emulated auto-repeat is added. `TypeText` delays stay on `ProfileCpuClockHz`. | The same cycle delay elapses sooner when the throttle is faster. |
| Open-Apple reset | The 500 ms sampling window becomes cycles from the emulated clock, so a 4 MHz pace does not stretch the modifier-release deadline. | The release still lands on the same cycle. |

[`Sound Card Specification.md`](../Sound%20Card%20Specification.md) §11.3–11.4 already keys pitch to a finite `ClockSpeedHz` and silence to a zero clock. Production code reads **effective** pace for that split, so a disk burst mutes even when the selected pace is 1,020,484. The future mixer hold (`HoldPlayback`) is this same condition: effective pace is 0. There is no second flag.

## Accelerate on disk access

### Control

One machine component, always registered by `MachineBuilder`, implements `IDiskAccelerationControl` and `IEmulationPacing` in `BadMango.Emulator.Bus.Interfaces`.

`IEmulationPacing` exposes `EmulatedClockHz`, `SelectedClockSpeedHz` (the same value as `IMachine.ClockSpeedHz`), and `EffectiveClockSpeedHz`.

`IDiskAccelerationControl` exposes:

- `Enabled` — the committed feature flag. Profile `cpu.accelerateOnDiskAccess` sets the initial value. Built-in default is false, including machines built without a profile.
- `Active` — committed `Enabled` is true and at least one motor request is on. This is the condition that forces effective pace to 0.
- `HasDeferredChange` and `DeferredEnabled` — a toggle waiting for the last request to clear.
- `RequestedSlots` — slots whose request latch is on, whether or not the feature is enabled.
- `SetEnabled(bool)` — see the deferral rule below.
- `NotifyMotorRequest(object source, int slot, bool requested)` — idempotent per `source`.

Mutations run on the machine thread. The REPL, the debugger UI, and `IMachineIoSession` post through `IMachine.Post`. `NotifyMotorRequest` is called from the controller on that thread, so it does not post and it does not take a lock.

The initial target is every `DiskIIController`, in any slot. SmartPort and 3.5-inch controllers are out of this epic. They will call the same `NotifyMotorRequest` later. No Disk II-specific type appears on the interface.

### Request latch

The controller reports the request from the motor handlers it already has. Those handlers are the access monitor: they see every read and write of `$C0n8` and `$C0n9`, and they already ignore `IsSideEffectFree`. There is no second bus decoder and no DOS, ProDOS, or RWTS trap.

| Event | Request |
| --- | --- |
| Accepted `$C0n9` (read or write, not side-effect-free) | That controller's request turns on. |
| Accepted `$C0n8` | That controller's request turns off. |
| `OnResetAsserted` or power-on `Reset` | That controller's request turns off. RESET clears the latch without waiting for the NE556 hold. |
| `OnMotorOffDue`, `StopMotor`, drive select, phase switches, `$C0nC` | No notification. |

A repeated `$C0n9` while the request is already on leaves the request on. A `$C0n9` during the hold is a new request: acceleration starts again, and the existing `SetMotor(true)` path still cancels the hold with no spin-up settle. A repeated `$C0n8` restarts the NE556 timer inside the controller and leaves the request off.

Each controller is one source. Its two drives share one enable latch, so drive select during the hold does not create a second request and does not keep acceleration on. Two controllers are two sources. Effective acceleration stays on while any source is requested and the feature is enabled. Slot 6 turning off while slot 5 is still requested leaves the pace unthrottled.

### When the pace moves

While `Active` is true, effective pace is 0 (unthrottled). `ClockSpeedHz` still returns the selected pace, which is what the burst restores.

The `$C0n8` that clears the last request makes `Active` false before the handler returns. The slice loop's next pacing sample uses the selected pace. The NE556 callback is not a pacing event. After that `$C0n8`, `motorOn` and `motorOffPending` stay true for up to 1,000,000 cycles, `$C0nC` still returns live data, and the throttle is already the user's speed.

A user speed change during a burst writes `ClockSpeedHz` immediately and leaves the effective pace at 0 until the last request clears. The value written is the pace restored at the end. Selecting Max means the machine stays unthrottled after the burst, because that is now the selected pace.

### Deferred toggle

`SetEnabled` while any request is on stores the new value and leaves the committed flag, and therefore `Active`, unchanged. A second call replaces the pending value. A pending value equal to the committed flag clears the deferral.

The pending value is applied when the request set becomes empty: the `$C0n8` that clears the last request, or a RESET / power-on that clears the last request. It is not applied when the NE556 hold expires.

Consequences:

- Disable during a transfer: the transfer stays unthrottled, the flag turns off as the last request clears, and the next `$C0n9` does not accelerate.
- Enable during a transfer: the transfer stays at the selected pace, the flag turns on as the last request clears, and the next `$C0n9` accelerates.
- `SetEnabled` with no request on: the flag changes immediately.

### Debugger view

The `speed` command with no arguments, and one status-monitor row, show:

- selected preset and hertz
- effective pace
- emulated clock
- disk acceleration: `off`, `on`, `on, pending off`, or `off, pending on`
- requested slots

`motor-off-pending` on the existing Disk II status line stays the NE556 coast. The request row is a different bit. Showing the coast as "accelerating" would be wrong for the whole second after `$C0n8`.

## Burst mode

Burst mode is not offered. There is no profile flag, command, or nibble-period override that feeds a byte in fewer than `CyclesPerByte` (32) cycles.

That mode would move the data-latch ready bit earlier in the cycle stream. The loaders that depend on the 4 µs bit cell would break:

- The Disk II boot ROM at `$Cn00`, which polls the data register on a fixed instruction budget.
- DOS 3.3 RWTS, address-field and data-field loops included.
- The ProDOS Disk II driver, which uses the same sequencer timing.
- Fast DOS revisions with the delay loops tightened (Diversi-DOS, Pronto-DOS, David-DOS, EsDOS). They have the least slack and fail first.
- Copy-protection loaders that count nibbles or cycles between sync marks, including Electronic Arts spiral tracks, Brøderbund, SSI, Sierra self-sync, and nibble copiers such as Locksmith, EDD, and Copy II Plus.
- Format and write paths that use Q6/Q7 pre-write timing.

Disk acceleration is the fast path this epic provides. The sequencer still shifts a nibble every 32 cycles; those cycles take less wall time.

## Video and audio

### Video

`VideoDevice` keeps the 17,030-cycle frame at every pace. `VideoWindow` keeps the one-in-flight queue it has now (`vblankPending`). A snapshot captured on a later VBlank still replaces a frame that has not been painted. Add a host interval of 16 ms: a paint that would start sooner than 16 ms after the previous paint stays queued, and the next paint shows the latest snapshot. Stock VBlank is one frame per 17,030 cycles, about 60 Hz at 1,020,484 Hz, which is already about one frame per 16 ms, so a stock pace still presents every frame. A faster pace, where the UI would otherwise repaint as soon as each frame finishes, drops intermediate frames down to that host interval. Dropped frames are presentation only: `$C019` and the cycle schedule still run.

Headless runs have no presenter. `WaitForScreenText` keeps checking about once per 17,030 cycles. Frame skipping is not implemented in the device, and headless output does not depend on it.

### Audio

On a built machine the speaker converts cycles to samples from effective pace.

- Effective pace greater than zero: the divisor is that pace. Stock stays at the emulated clock and sounds as it does today. 2.8 MHz and 4 MHz play the same toggle stream in less wall time, at a proportionally higher pitch. The buffer stays on a wall-clock budget because sample count tracks wall time.
- Effective pace zero (Max, or an active disk burst): do not enqueue. Advance the render cursor to the current cycle and discard the samples for that span, leaving the 1-bit latch where the last toggle put it. The next finite pace starts from that cursor, so the burst does not flush a backlog or a catch-up glissando.

`$C030` only records the toggle, as it does now. The mute decision lives in the sample generator.

A speaker built without `IEmulationPacing` keeps today's `GetCyclesPerSecond` rule (`ClockSpeedHz` when positive, otherwise 1,020,484) so existing device tests that mock `IMachine` alone stay valid. The builder change in this design registers the pacing component on every machine, so a production unthrottled run and a disk burst take the mute path.

## Controls and API

### Profile

`cpu.clockSpeed` keeps its current meaning and JSON name. `cpu.pacingHz` and `cpu.accelerateOnDiskAccess` are optional. Shipped profiles omit both and keep today's throttle and a disabled feature. The schema descriptions say that `clockSpeed` is the emulated clock and that `pacingHz` is the initial throttle.

### REPL

One debugger command, `speed`, in `BadMango.Emulator.Debug.Infrastructure`:

- `speed` prints selected preset, selected hertz, effective pace, emulated clock, feature flag, deferred state, and requested slots.
- `speed stock`, `speed 2.8`, `speed 4`, `speed max`, and `speed <hertz>` set `ClockSpeedHz`.
- `speed disk on` and `speed disk off` call `SetEnabled`. The reply says whether the change was applied or deferred.

The command posts onto the machine thread. It does not step the CPU and it does not touch soft switches.

### Debugger UI

The running-machine surface is the debugger status monitor, which already prints Disk II `motor-off-pending`. Add the selected preset, the effective pace, and the disk-acceleration state (including a pending toggle) there, bound to the same component as `speed`. `bpui` does not grow a speed control in this epic. Its profile screen is a summary string. The profile JSON is the persistent default.

### Headless session

`IMachineIoSession` gains the same state the command edits:

- `EmulatedClockHz` (read)
- `SelectedClockSpeedHz` (read/write; 0 is max, same as `IMachine.ClockSpeedHz`)
- `EffectiveClockSpeedHz` (read)
- `DiskAccelerationEnabled` (read/write; a write may defer)
- `DiskAccelerationChangePending` and `PendingDiskAccelerationEnabled` (read)
- `DiskAccelerationActive` (read)

Writes post to the machine thread. `WaitForScreenText` and other session waits stay on cycle budgets (`RunBudget`). Headless and replay fixtures leave `accelerateOnDiskAccess` false unless the test is about acceleration. They compare cycles, memory, and device snapshots. They do not assert wall-clock duration, except tests whose subject is the throttle sleep.

`Run` and `Step` stay unpaced, so a fixture that steps a `$C0n9` updates the request latch and does not sleep. A later `RunAsync` observes that latch.

## Hot loop

Pacing changes add no per-instruction work.

- `Cpu65C02` step, `IScheduler.Advance`, and `RunSlice` do not read the selected pace, the effective pace, or the disk-acceleration flag.
- `ExecuteLoopAsync` reads `EffectiveClockSpeedHz` once after each slice returns, in the block that already reads `ClockSpeedHz`.
- `NotifyMotorRequest` stores a source identity and a bool. It does not allocate on the steady `$C0n9` / `$C0n8` path beyond what the controller already does for the NE556 event, and it does not sleep. The first notify for a source may record that source; a repeat is a field update.
- When the effective pace differs from the pace the current window was opened with, restart the stopwatch and zero `throttleCycleBase` before the delay math. Cycles run at the old pace are not divided by the new pace. A burst is not paid back as sleep after `$C0n8`: the restored window starts empty.
- The existing "more than 100 ms behind" reset stays for an unchanged pace.
- Effective pace 0 skips the delay, as `ClockSpeedHz == 0` does today.
- An edge that happens inside a slice is visible to the delay check at the end of that slice (at most 10,000 cycles later). The slice itself does not stop early for a speed change.

## Tests

Cycle-accurate device behavior is identical at every speed. The implementation proves that, and proves the pacing exceptions are wall-clock only.

1. One program, run by `Run` or by stepped cycles, at stock, 2.8 MHz, 4 MHz, max, and with disk acceleration enabled: same instruction effects, same memory, same scheduler deadlines, same VBlank cycle numbers, same speaker toggle cycles, same Disk II bytes and byte-ready cycles. `ClockSpeedHz` is not an input to those results.
2. `RunAsync` at a low positive `ClockSpeedHz` still sleeps (the existing throttle test). `RunAsync` at 0 does not. After a disk burst, the loop does not sleep off the cycles run while effective pace was 0.
3. Accepted `$C0n9` sets the request and, with the feature on, forces effective pace to 0 while `ClockSpeedHz` is unchanged. Accepted `$C0n8` clears it. With a second controller still requested, effective pace stays 0. With none left, effective pace equals `ClockSpeedHz` while `motorOffPending` is still true and a `$C0nC` read still returns live data.
4. A side-effect-free read of `$C0n9` / `$C0n8` changes neither the request nor the pace.
5. `$C0n9` during the hold cancels the hold (existing device behavior) and turns the request back on.
6. NE556 expiry does not change the pace and does not apply a deferred toggle.
7. `SetEnabled(false)` during a request stays deferred until the last `$C0n8` or RESET. `SetEnabled(true)` during a request does not accelerate that transfer.
8. RESET and power-on clear the request immediately and apply a deferred toggle.
9. A profile that omits `pacingHz` and `accelerateOnDiskAccess` builds the same `ClockSpeedHz` and `ProfileCpuClockHz` as today. `pacingHz: 0` with `clockSpeed: 1020484` builds an unthrottled throttle and an emulated clock of 1,020,484. The feature starts off.
10. `KeyboardInput` cycle delays for a given millisecond gap stay on the emulated clock when `ClockSpeedHz` changes. `GetResetAppleReleaseDelayCycles` does too.
11. With `IEmulationPacing` installed, a finite fast pace scales sample count, and effective pace 0 enqueues nothing across a large cycle delta and does not dump that delta when the pace returns. `VideoDevice` still schedules every 17,030 cycles at max pace.
12. Changing the pace does not add a branch to `Cpu.Step` or `RunSlice`. A regression check is the absence of pacing reads on those paths, not a new per-opcode counter.

## Proposed implementation sub-issues

File these under #443 when implementation starts. Sizes are S, M, or L.

1. **S — Profile pacing fields.** Add optional `cpu.pacingHz` and `cpu.accelerateOnDiskAccess` to the schema and `FromProfile`, and keep today's clock split when they are absent.
2. **M — Effective throttle in the slice loop.** `RunAsync` and `ResumeAsync` sample `EffectiveClockSpeedHz` and reset the throttle window when it changes. `RunSlice` and `Cpu.Step` do not read the pace.
3. **M — Disk II motor-request acceleration.** Add `IDiskAccelerationControl` / `IEmulationPacing`, report `$C0n8`, `$C0n9`, and RESET from every `DiskIIController`, and defer enable changes until the last request clears. The NE556 hold does not notify.
4. **S — `speed` command and debugger status.** Presets, `disk on|off` with a deferral reply, and a status row that shows the request separately from `motor-off-pending`.
5. **S — Headless pacing on `IMachineIoSession`.** The same selected pace, effective pace, emulated clock, and disk-acceleration state, posted onto the machine thread.
6. **M — Speaker and video presentation.** Mute while effective pace is 0, scale pitch with a finite pace, and hold `VideoWindow` to one paint per 16 ms on top of the existing `vblankPending` queue. The `VideoDevice` schedule stays on 17,030 cycles.
7. **S — Open-Apple reset delay uses the emulated clock.** `GetResetAppleReleaseDelayCycles` stops reading the throttle.
8. **M — Speed-invariant cycle tests.** Identical device-visible results at stock, 4 MHz, max, and disk acceleration, including `$C0n8` restoring pace while the NE556 hold is still counting.
