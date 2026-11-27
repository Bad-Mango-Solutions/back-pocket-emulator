# Host API

**Summary:** Version 1 of the emulator host API. Clients power, see, hear, and drive a machine only through `BadMango.Emulator.Host.Contracts`. The stub loopback used by tests is not that assembly. The real in-process loopback is the #735 binding onto a real machine. IPC and the network come later, on the same contracts.

**Covers:** Where the contracts live, machine addressing, threading, versioning, the control, presentation, and input planes, the debugger extension, and which parts of `IDebugSession` and `IMachineIoSession` stay on the debug session.

**Does not cover:** Rewiring `emudbg` or `bpui`, an out-of-process transport, the AppleMouse card, the #738 wire encoding, or deleting anything on the #566 list.

This is the decision record for [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734), under [#733](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/733). The UI rules it implements are in [`bpui Three-Hub Design.md`](../ui/bpui%20Three-Hub%20Design.md) section 5.3 and the host API table in section 13. The issue's ".NET 10" line is stale. The repo targets `net11.0`.

## Where the contracts live

`src/BadMango.Emulator.Host.Contracts`. Namespace `BadMango.Emulator.Host.Contracts`. No project references and no package references. It holds the interfaces, the DTOs, `HostApi`, and `HostApiVersionException`.

The stub loopback is test support: `tests/BadMango.Emulator.Host.Contracts.Testing`, namespace `BadMango.Emulator.Host.Contracts.Testing`. It is a non-packable library, not a test suite. `BadMango.Emulator.Host.Contracts.Tests` references it. That stub is not the production transport. [#735](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/735) is the real in-process loopback: the binding that posts onto a live machine through `IMachineDispatcher`.

[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478) and [`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md) reduce the count of assemblies that already have a home. These contracts do not have one that stays free of `IMachine`, `VideoDevice`, and `IAudioOutput`:

- Core now holds `IMachine` and `IMachineDispatcher`. A reference to Core is a reference to the machine.
- Devices holds the video device, `IAudioOutput`, `IHostKeyboardSink`, and `IGamePortDevice`.
- Debug session contracts live in Debug.Infrastructure (namespace `BadMango.Emulator.Debug.Abstractions`, under `src/BadMango.Emulator.Debug.Infrastructure`). That project references the machine.
- UI is the `bpui` shell. If the contracts lived there, `emudbg` and a later browser client would reference that executable.
- Rendering holds `IVideoSession` and Avalonia.

[#733](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/733) asks for a new host-contracts surface so #477 does not have to move it. One leaf with zero project references is that surface. It is not a fold target, and it does not undo a fold. Clients of the contract do not reference `IMachine`, `VideoDevice`, or `IAudioOutput`.

## Addressing

A host runs more than one machine. `MachineId` is an opaque string. `IHostControl.CreateInstance` returns a new id. `ListInstances` returns every machine on that host. Commands take a `MachineId`. Two connects are two hosts. Two machines are two instances on one session.

## Threading

Client threads call the planes. The machine thread stays exclusive. The stub takes the machine's `Lock` once per call, at this edge, and returns. It does not poll anything per cycle, and the contracts assembly is not referenced from the CPU loop or the bus.

Input is appended to a log (capped at 1024). The stub's `ILoopbackMachineEdge.DrainInput` takes that log. The #735 binding posts one drained batch through `IMachineDispatcher` at an instruction boundary.

Presentation keeps the latest sample by reference. A slow client does not block the publisher. Audio is fanned out to each subscription, capped at 8 blocks, dropping the oldest. Publish is a frame tap or an audio-block tap, not a per-cycle speaker toggle, and it does not call `IAudioOutput.Play`.

## Versioning

`HostApi.Version` is 1. `HostApi.VersionName` is `host-api/v1`. `IHostTransport.Connect` refuses any other version with `HostApiVersionException`.

The stub returns the session object. It does not serialize and it does not copy frames or PCM. The #735 binding does the same in-process, against a real machine. A later IPC or network transport implements the same interfaces, checks the version at connect, and copies only at that boundary.

Adding an optional field with a default stays inside version 1. Removing, renaming, or changing the meaning of a member is a new version.

Control commands that may wait (`PowerOnAsync`, disk mount, throttle, attach) return `Task`. The stub completes that task on the calling thread. Input calls and presentation reads stay synchronous and non-blocking, so a shared-memory transport can implement them as local buffer access.

## Control

`IHostControl` is the power menu and the instance list.

| Command | Rule |
| --- | --- |
| Power On | From Off only. Runs, or pauses when the instance was created with start-paused. |
| Pause | From Running only. |
| Resume | From Paused or Sleeping. |
| Sleep | From Running only, and only when `PowerCapabilities.Sleep` is set. |
| Reset | While the machine is on. The run state does not change. |
| Hard Reset | While the machine is on, when the record is missing or `HardReset` is set. Cycles power. The run state does not change. |
| Shut Down | Soft-power path. State passes through Shutting down, then Off. |
| Power Off | Cuts power. No shutting-down step. |

`PowerCapabilities` is `SoftPower`, `Sleep`, and `HardReset`. Shut Down is only for a soft-power machine. Power Off is the action otherwise. Sleep is only when `Sleep` is set. When machine info carries no record, clients use the conservative set: Pause or Resume, Reset, Hard Reset, and Power Off. The stub throws for an action outside that set.

`HostRunState.Sleeping` is the state after Sleep. The UI spec does not define the menu while sleeping. Resume wakes the machine. Reset, Hard Reset, and Shut Down or Power Off stay available. Pause does not.

Throttle is `Half`, `Normal`, `Double`, or `Max`. `Max` sets the effective clock to 0, which is unthrottled. Measured speed is a separate ratio. The nominal clock defaults to 1,020,484 Hz.

Profiles are opaque JSON documents. The stub stores them and does not validate `schemas/machine-profile.schema.json`. An instance overlay can change only while the machine is off. Replacing a template does not rewrite a running instance. Applying a template on the next power-on is #799. The #735 binding is what applies it to a real machine.

Disks are `MountDisk`, `EjectDisk`, `ListDisks`, and `SetWriteProtect`. The UI drive panel toggles the host write-protect flag, so that flag is part of the contract even though #734 names only the three `IMachineIoSession` methods. The stub records the path and does not open it. Its topology is a Disk II in slot 6 with drives 1 and 2. Drive 1 is the boot drive (`S6 D1`). `ListSlots` is the read-only strip (aux and slots 1–7). Slot edits are profile-document writes while Off.

Attach and detach are on the control plane. They are idempotent.

## Presentation

`IHostPresentation.Subscribe` opens one window on one machine.

| Output | What is published | Monitor choice |
| --- | --- | --- |
| Composite | A raw `HostCompositeSignal`, a finished `HostBgraFrame`, or both | Mono, Green, Amber, NTSC. Per window. |
| RGB | A finished `HostBgraFrame` only | RGB, locked |

Two windows on one composite source can show different monitors and still share the latest sample. Latest-wins is the last publish, not the greatest sequence number. The stub hands back the same object. It does not copy pixels.

The logical composite layout, once #410 lands, is `Composite560Signal`: 192 lines by 560 dots plus 192 line-flag bytes, from VIDEO-PIPELINE-PRD §7.4. This contract does not require those dimensions. [#738](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/738) owns the shared-memory and network encoding, and where monitor decode runs. Until then the in-process value is `HostPresentationSample`.

`GetScreenText` and `GetVideoState` are snapshots for an agent. They are not the pixel stream and they do not reference the video device.

Audio is the #691 line-out: `HostAudioFormat` plus cycle-stamped `HostAudioBlock` values. The stub default is 44,100 Hz, mono, signed 16-bit little-endian (`HostAudioDefaults.LineOut` in the testing assembly). A tap may replace the format. The host does not call `IAudioOutput.Play`.

## Input

`IHostInput` takes keyboard down and up with a modifier snapshot, text injection (paste), joystick, paddle, button, mouse move, mouse button, and pointer capture and release.

`HostKey` uses the same member names and order as `KeyboardKey`. This assembly does not reference Devices. A later adapter maps the two enums by name. `HostKeyModifiers` uses the same bit values as `KeyboardModifiers`.

Mouse motion is `Relative` (a delta) or `Absolute` (a position clamped to 0–1). Buttons are left, middle, and right. `CapturePointer` records the window. A later capture replaces the owner. `ReleasePointer` clears capture only when that window holds it. The AppleMouse card is [#451](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/451). The events are in the contract now so that card does not revise the API.

`SetJoystick` also writes the mapped paddles. Index 0 writes paddles 0 and 1. Index 1 writes paddles 2 and 3. Axis -1 is paddle 0 and axis 1 is paddle 255.

`InjectText` accepts letters, digits, space, and CR or LF. Anything else is counted as skipped.

## Debugger extension

`IHostDebugger` is breakpoints, watches, memory, and disassembly. The shell can power a machine without it. These methods throw when the debugger is not attached. The stub keeps 64 KiB of memory and reports each byte as a `DB` line. It does not decode opcodes and it does not run a CPU.

## What stays on the debug session

#560 folded Debug.Abstractions into Debug.Infrastructure. The members this API did not take moved with that project and stay on `IDebugSession` and `IMachineIoSession` (namespace `BadMango.Emulator.Debug.Abstractions`, under `src/BadMango.Emulator.Debug.Infrastructure`).

| Today | Where it goes |
| --- | --- |
| `MountDisk`, `EjectDisk`, `ListDisks` | Control. `SetWriteProtect` is added for the drive panel. |
| `TypeKeys` | Input `InjectText`. |
| `GetScreenText`, `GetVideoState` | Presentation snapshots. |
| `GetMachineInfo` | Control `HostMachineInfo`, including `PowerCapabilities`. |
| `Pause`, `Reset` | Control. Reset is warm. Hard Reset is separate. |
| Breakpoints, watches, memory, disassembly | `IHostDebugger`. |
| `WaitForScreenText` | Stays on `IMachineIoSession`. It runs the machine under a budget. |
| `Step`, `RunUntil`, `StartRun`, `StopRun`, `GetRunStatus` | Stay on `IDebugSession`. They are the REPL's budgeted runs, not the power menu. |
| `AbortReaction`, fault status, mapping trace | Stay on `IDebugSession`. They name the bus. |

## #566

Nothing on the verify-before-removing list was deleted.

| #566 row | What this change does |
| --- | --- |
| `IVideoSession` | Superseded in role by `IPresentationSubscription`. The Rendering type stays. Deletion still waits on #566's check and on clients moving off it. |
| `BreakpointHitEvent` | The host signal is `HostBreakpointNotice`. The UI event stays. A later client may publish it from a sink. |
| `DisplayModeChangedEvent` | The host signal is the per-window monitor choice. The UI event stays. |
| `MachineStateChangedEvent` | The host signal is `HostStateNotice`. The UI event stays. |
| `WindowFocusRequestEvent` | Not a host-API event. The row is unchanged. |

## Stub loopback

`BadMango.Emulator.Host.Contracts.Testing` is the stub. It is test support, not the production host. The real in-process loopback is the #735 binding onto a real machine, and that binding is the only place that posts through `IMachineDispatcher`.

The stub is not a 65C02. It does not open disk images, load ROMs, scan video, or mix audio. Frames and PCM in tests are synthetic. Register reads are zeros. Disassembly is one data byte per line. The slot strip and the Disk II topology are fixed. `HostAudioDefaults` and `HostDriveStatus` live with the stub because only the stub publishes them.
