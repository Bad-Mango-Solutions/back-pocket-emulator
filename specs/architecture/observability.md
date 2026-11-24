# Observability and Debugging

## Summary

The running machine is observed through debugger services, a CPU instruction
trace, a bus fault ring, and device ids. The v1.0 §9.1 bus-access trace ring
was not built: the tree has no `BusTraceEvent` type and no `TraceRingBuffer`
type.

**Covers:** `WatchpointManager` and the debug session that hosts it,
`InstructionTrace`, `BusFaultRing`, `DevicePageClass`, and the `DevicePageId`
bit layout the code stores. Audited sections
[3.3](#33-device-page-classes--status-partly-implemented),
[9.1](#91-trace-events--status-aspirational),
[9.2](#92-device-registry--status-partly-implemented), and
[9.3](#93-device-page-id-encoding--status-partly-implemented).

**Does not cover:** fault-register latch rules, `FaultKind` bit assignment, or
the `ABORT` line. Those names are stable on the
[bus and MMU page](bus-mmu.md) and are normative in the
[fast-path specification's fault registers](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md#9-fault-registers).
CPU execution is the [CPU page](cpu.md). Signal topology, the scheduler,
device I/O behavior, machine profiles, and the `bpui` shell are other pages.

## Code paths

- [`BusFaultRing`](../../src/BadMango.Emulator.Bus/BusFaultRing.cs) is the
  fixed ring of [`BusFault`](../../src/BadMango.Emulator.Bus/BusFault.cs)
  values. [`IBusFaultRecorder`](../../src/BadMango.Emulator.Bus/Interfaces/IBusFaultRecorder.cs)
  is the write side `MainBus` calls.
  [`IBusFaultRing`](../../src/BadMango.Emulator.Bus/Interfaces/IBusFaultRing.cs)
  is the read side the `fault` and `buslog` commands use.
- [`DevicePageId`](../../src/BadMango.Emulator.Bus/DevicePageId.cs),
  [`DevicePageClass`](../../src/BadMango.Emulator.Bus/DevicePageClass.cs),
  [`DeviceRegistry`](../../src/BadMango.Emulator.Bus/DeviceRegistry.cs), and
  [`DeviceInfo`](../../src/BadMango.Emulator.Bus/DeviceInfo.cs) are the device-id
  types. [`IDeviceRegistry`](../../src/BadMango.Emulator.Bus/Interfaces/IDeviceRegistry.cs)
  is the registry contract. [`IMemoryBus`](../../src/BadMango.Emulator.Bus/Interfaces/IMemoryBus.cs)
  exposes `FaultStatus`, `FaultAddress`, and `ClearFaultStatus`.
- [`InstructionTrace`](../../src/BadMango.Emulator.Core/Cpu/InstructionTrace.cs)
  is the CPU-side trace struct in `BadMango.Emulator.Core.Cpu`.
  [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs) fills it
  while a debug listener is attached.
  [`CpuBase.DetachDebugger`](../../src/BadMango.Emulator.Emulation/Cpu/CpuBase.cs)
  clears it.
- [`TracingDebugListener`](../../src/BadMango.Emulator.Debug.Infrastructure/TracingDebugListener.cs)
  and [`TraceRecord`](../../src/BadMango.Emulator.Debug.Infrastructure/TraceRecord.cs)
  are the debugger-side instruction log.
  [`TraceCommand`](../../src/BadMango.Emulator.Debug.Infrastructure/Commands/TraceCommand.cs)
  enables that listener.
- [`WatchpointManager`](../../src/BadMango.Emulator.Debug.Infrastructure/WatchpointManager.cs)
  and [`BreakpointManager`](../../src/BadMango.Emulator.Debug.Infrastructure/BreakpointManager.cs)
  are debugger services and do not move.
  [`MachineDebugSession`](../../src/BadMango.Emulator.Debug.Infrastructure/MachineDebugSession.cs)
  implements [`IDebugSession`](../../src/BadMango.Emulator.Debug.Abstractions/IDebugSession.cs).
- [`DebugWindowManager`](../../src/BadMango.Emulator.Debug.UI/Services/DebugWindowManager.cs)
  opens debugger windows. The text editor window is
  [`TextEditorWindow`](../../src/BadMango.Emulator.TextEditor/TextEditorWindow.axaml.cs).
- [`emudbg`](../../src/BadMango.Emulator.Debug/Program.cs) and
  [`emudbg-mcp`](../../src/BadMango.Emulator.Debug.Mcp/Program.cs) are the thin
  hosts. They stay separate processes.

## Target layout

[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
and the
[solution consolidation design](../Solution%20Consolidation%20Design.md) are
the target assembly layout. Namespaces stay as they are. This page does not
rename them.

Bus fault records, device-id types, and the CPU trace struct land in
`BadMango.Emulator.Core`. The bus types keep the `BadMango.Emulator.Bus`
namespace. `InstructionTrace` already lives in `BadMango.Emulator.Core` under
`BadMango.Emulator.Core.Cpu`. The 65C02 that writes the trace still lives in
the Emulation project under `BadMango.Emulator.Emulation.Cpu`.

> **Current location (until #562):** `src/BadMango.Emulator.Bus`; target:
> `src/BadMango.Emulator.Core` (namespace `BadMango.Emulator.Bus` unchanged).
>
> **Current location (until #562):** `src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs`
> and `src/BadMango.Emulator.Emulation/Cpu/CpuBase.cs`; target:
> `src/BadMango.Emulator.Core/Cpu/` (namespace `BadMango.Emulator.Emulation.Cpu`
> unchanged).
>
> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`; target:
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

Debugger services, including the contracts, land in
`BadMango.Emulator.Debug.Infrastructure`. `WatchpointManager`,
`BreakpointManager`, `TracingDebugListener`, `MachineDebugSession`, and the
debug commands already live there and do not move. `IDebugSession` and the
session DTOs stay in the `BadMango.Emulator.Debug.Abstractions` namespace.

> **Current location (until #560):** `src/BadMango.Emulator.Debug.Abstractions`;
> target: `src/BadMango.Emulator.Debug.Infrastructure` (namespace
> `BadMango.Emulator.Debug.Abstractions` unchanged).
>
> **Current location (until #560):** `tests/BadMango.Emulator.Debug.Abstractions.Tests`;
> target: `tests/BadMango.Emulator.Debug.Infrastructure.Tests` (namespace
> unchanged).

Debugger windows land in `BadMango.Emulator.Debug.UI`. The video, status, trap,
schedule, log, about, and glyph-editor windows already live there. The text
editor still lives in the TextEditor project under `BadMango.Emulator.TextEditor`.

> **Current location (until #561):** `src/BadMango.Emulator.TextEditor`; target:
> `src/BadMango.Emulator.Debug.UI` (namespace `BadMango.Emulator.TextEditor`
> unchanged).
>
> **Current location (until #561):** `tests/BadMango.Emulator.TextEditor.Tests`;
> target: `tests/BadMango.Emulator.Debug.UI.Tests` (namespace unchanged).

`BadMango.Emulator.Debug` (`emudbg`) and `BadMango.Emulator.Debug.Mcp`
(`emudbg-mcp`) stay separate thin hosts and do not move. The target direct
references are `Debug.Infrastructure` and `Debug.UI` for `emudbg`, and
`Debug.Infrastructure` only for `emudbg-mcp`. The MCP project already has that
single reference. The `emudbg` project file still also references Core, Bus,
and Emulation.

CPU trace tests and mapping-trace tests still live in the cross-suite test
project.

> **Current location (until #565):** `tests/BadMango.Emulator.Tests` CPU trace
> and mapping-trace coverage; target: subject-specific test projects, including
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

## Debugger

`WatchpointManager` is an `IActivatableDebugStepListener`. After each
instruction it classifies the mnemonic and compares
`DebugStepEventArgs.EffectiveAddress` with the registered addresses. A hit
can request `ICpu.RequestStop` when the watchpoint was added with
`stopOnHit`. With a bus attached, the manager reads the watched byte through
a debug access (`AccessFlags.NoSideEffects`); a failed read leaves the value
unset.

`ClassifyAccess` treats these mnemonics as accesses:

- Write: `STA`, `STX`, `STY`, `STZ`.
- Read: `LDA`, `LDX`, `LDY`, `BIT`, `CMP`, `CPX`, `CPY`, `AND`, `ORA`, `EOR`,
  `ADC`, `SBC`.
- Read and write: `ASL`, `LSR`, `ROL`, `ROR`, `INC`, `DEC`, `TRB`, `TSB`.

Every other mnemonic, including `JSR` and `JMP`, returns `WatchAccess.None`.
The class remarks also name JSR and JMP. Opcode fetch, stack push and pop, and
interrupt-vector reads are not watchpoint hits. The manager is active while
any watchpoint is registered, including a disabled one.
[`WatchCommand`](../../src/BadMango.Emulator.Debug.Infrastructure/Commands/WatchCommand.cs)
adds, removes, enables, and saves those entries.

The step listener's `WatchAccess` is a flags enum in
`BadMango.Emulator.Debug.Infrastructure`, declared in `WatchpointManager.cs`
(`None`, `Read`, `Write`, `ReadWrite`). The session DTO is a different type:
[`Watchpoint`](../../src/BadMango.Emulator.Debug.Abstractions/Watchpoint.cs)
and [`WatchAccess`](../../src/BadMango.Emulator.Debug.Abstractions/WatchAccess.cs)
in `BadMango.Emulator.Debug.Abstractions` (`Read`, `Write`, `ReadWrite`).
`WatchCommand` maps between them.

`BreakpointManager` registers a call trap at the breakpoint address and stops
before that instruction executes. Resume consumes a one-shot skip so the
instruction runs, then the trap is armed again. Disabled breakpoints stay
registered and do not stop the CPU.

`IDebugSession` is the host-facing contract: registers, `ReadMemory` and
`WriteMemory`, disassembly, step and run, breakpoints, watchpoints,
`FaultStatus`, `FaultAddress`, `ClearFaultStatus`, and `AbortReaction`.
`MachineDebugSession` is the implementation and reads those fault registers
from the bus. Debug memory access does not latch them. The `abort` command
changes `AbortReaction` and does not pulse `SignalLine.Abort`. The
[CPU page](cpu.md) owns the 65C02 `NoOp` and `HostBreak` reactions.
`peek`, `poke`, and `mem` are the memory commands. `fault` and `buslog` read
the fault ring.

`DebugWindowManager` opens the video, status, trap, schedule, log, about,
character-preview, and glyph-editor windows, and the text editor. `emudbg`
is the console host.
`emudbg-mcp` is the stdio MCP host and references only
`Debug.Infrastructure`, so it does not take the debugger UI.

## Instruction trace

`InstructionTrace` is a readonly record struct: `StartPC`, `OpCode`,
`Instruction`, `AddressingMode`, `OperandSize`, `Operands`,
`EffectiveAddress`, `StartCycle`, and `InstructionCycles`. `ICpu.Trace`
exposes it. `Cpu65C02` writes a new value at the start of an instruction, and
addressing modes and handlers update it with `with`, only while
`DebugListener` is non-null. `DetachDebugger` assigns `default`.

`TracingDebugListener` is the listener `TraceCommand` controls. On the step
callback it copies the executed instruction into a `TraceRecord` (PC, opcode,
mnemonic, operands, effective address, registers, cycles, and halt state).
The command's subcommands are `on`, `off`, `status`, `buffer`, `file`,
`filter`, `dump`, `tail`, and `clear` (alias `tr`). Buffering and file output
default off. An address filter limits records to a PC range. This stream is
the CPU instruction log. It is the stream the
[#536 audit](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
names when it says `TraceCommand` drives `InstructionTrace`. The listener and
`TraceRecord` sit between the command and the CPU struct.

## Fault ring

`BusFaultRing` implements both `IBusFaultRecorder` and `IBusFaultRing`. The
default capacity is 256 (`DefaultCapacity`). `MachineBuilder` constructs one,
passes it to `MainBus`, and registers it as a machine component.
`Record` ignores a `BusFault` whose `Kind` is `FaultKind.None`. The buffer
keeps the newest faults, drops the oldest when it wraps, and counts every
recorded fault in `TotalFaults`, including ones that have wrapped out of the
window. `Snapshot` returns oldest first. `Clear` zeroes the count and
`TotalFaults`.

`IMemoryBus.FaultRecordingEnabled` defaults to false. The first `fault` or
`buslog` command turns it on, so earlier faults are absent from the ring.
`buslog off` turns it off. While recording is on, `MainBus` appends a
`BusFault` for a non-debug failed `Try*` access and can append a silent
record for a floating-bus hole. Debug accesses are not recorded. The fast
path does not consult the flag.

The host-visible registers are `FaultStatus` (`FaultKind`) and `FaultAddress`
(`Addr`), cleared together by `ClearFaultStatus`. `IDebugSession` forwards
the same two values as `ushort` and `uint`. The
[bus page](bus-mmu.md) leaves the latch rules, `FaultKind`, and `ABORT`
(`SignalLine.Abort`) in the
[fast-path specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md#9-fault-registers).
The ring stores `BusFault` values. It is not a trace of every bus access.
[Section 9.1](#91-trace-events--status-aspirational) records that gap.

## 3.3 Device Page Classes — **Status: Partly implemented**

`DevicePageClass` is a `byte` enum. The values in code are:

| Value | Name | Role in the enum remarks |
| --- | --- | --- |
| `0x0` | `Invalid` | Unassigned |
| `0x1` | `CompatIO` | Apple II soft switches and slot I/O |
| `0x2` | `SlotROM` | `$Cn00`–`$CnFF` and `$C800`–`$CFFF` |
| `0x3` | `Storage` | Disk and storage controllers |
| `0x4` | `Timer` | Timer and clock devices |
| `0x5` | `Input` | Keyboard, mouse, joystick |
| `0x6` | `Audio` | Speaker and sound devices |
| `0x7` | `Network` | Ethernet and serial |
| `0x8` | `Framebuffer` | Video |
| `0xF` | `Debug` | Debug and diagnostic devices |

The [#536 audit](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
records this status because those numbers differ from both v1.0 sketches.
[§3.3](../Architecture%20Spec%20v1.0%20Part%201.md#33-device-page-classes-65832)
assigns Framebuffer `0x3`, Storage `0x4`, Network `0x5`, Timer `0x6`, and
Debug `0x7`, and reserves `0x8`–`0xF`.
[§9.2](../Architecture%20Spec%20v1.0%20Part%203.md#92-device-registry) assigns
Framebuffer `0x3`, Storage `0x4`, Network `0x5`, Timer `0x6`, Debug `0x7`,
Audio `0x8`, Input `0x9`, Dma `0xA`, and SystemControl `0xB`. The code has no
`Dma` or `SystemControl` member. `Input` and `Audio` are `0x5` and `0x6`.
`Debug` is `0xF`.

The enum is the classification stored in a `DevicePageId`. Built Pocket2e
registration does not choose pages with it. See
[§9.2](#92-device-registry--status-partly-implemented).

## 9.1 Trace Events — **Status: Aspirational**

[§9.1](../Architecture%20Spec%20v1.0%20Part%203.md#91-trace-events) specifies
`BusTraceEvent` and `TraceRingBuffer`, a ring of every bus access. Neither
type exists. The §9.1 bus trace ring was not built.

What the tree has instead:

- [`BusFaultRing`](../../src/BadMango.Emulator.Bus/BusFaultRing.cs) records
  faults. Recording defaults off. A successful access does not enter the ring.
- [`InstructionTrace`](../../src/BadMango.Emulator.Core/Cpu/InstructionTrace.cs)
  plus [`TraceCommand`](../../src/BadMango.Emulator.Debug.Infrastructure/Commands/TraceCommand.cs)
  are the CPU instruction stream described above.
- [`MappingTraceRing`](../../src/BadMango.Emulator.Bus/MappingTraceRing.cs) is
  a third ring. `MainBus` owns one. `MappingTraceEnabled` defaults to false,
  and the CPU fast path does not write the ring. When trace is on, the bus
  appends remap records and slow-path byte accesses. The `maptrace` command
  reads it. That behavior is the fast-path specification's
  [mapping trace](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md#13-mapping-trace).
  The [#536 audit](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
  does not name this type. It is not `BusTraceEvent` or `TraceRingBuffer`, so
  this section stays **Aspirational**.

## 9.2 Device Registry — **Status: Partly implemented**

`DeviceRegistry` maps a structural integer id to a `DeviceInfo`. `DeviceInfo`
carries `Id`, `PageId`, `Kind`, `Name`, and `WiringPath`. The integer-only
constructor stores a default `PageId`.

The methods the audit names are present: `Register`, `TryGet`, `GetByClass`,
and `GenerateId`. The implementation also has `Get`, `TryGetByPageId`,
`GetByPageId`, `GetAll`, `Contains`, `ContainsPageId`, and `Unregister`.
`GetByClass` walks devices that were registered with a valid page id.

Built machines call the integer `Register` overload. Pocket2e registers main
RAM, auxiliary RAM, and the I/O page that way.
[`MachineBuilder.MapRom`](../../src/BadMango.Emulator.Bus/MachineBuilder.cs)
registers ROM the same way, and slot-card insert registers `DeviceType`,
`Name`, and a wiring path. `PageEntry.DeviceId` is that integer. The page-id
overload is implemented and covered by the registry tests. Class numbers
still follow the code enum in
[§3.3](#33-device-page-classes--status-partly-implemented), which is why this
section stays **Partly implemented**.

## 9.3 Device Page ID Encoding — **Status: Partly implemented**

`DevicePageId` is a readonly struct. The raw value is masked to 24 bits
(`0xFFFFFF`). The fields the struct stores are:

| Bits | Field | Width | Range |
| --- | --- | --- | --- |
| 23–20 | Class | 4 | 0–15, the `DevicePageClass` value |
| 19–8 | Instance | 12 | 0–4095 |
| 7–0 | Page | 8 | 0–255 |

`Class`, `Instance`, and `Page` read those fields. `IsValid` is false when
the class is `Invalid`. Factories are `CreateCompatIO`, `CreateSlotROM`,
`CreateStorage`, `CreateTimer`, `CreateDebug`, and `Create`. The factory
arguments that are `byte` values occupy the low 8 bits of the 12-bit instance
field.

[§9.3](../Architecture%20Spec%20v1.0%20Part%203.md#93-device-page-id-encoding)
specifies a 20-bit id: class at bits 19–16, an 8-bit instance at bits 15–8,
and page at bits 7–0. That table is not what the struct stores. The status
stays **Partly implemented**. Nothing in the built page table interprets
`PageEntry.DeviceId` as this encoding.

## Tests and evidence

Bus fault and device-id tests live in `BadMango.Emulator.Bus.Tests`.

> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`; target:
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

Representative files:

- [`BusFaultRingTests`](../../tests/BadMango.Emulator.Bus.Tests/BusFaultRingTests.cs)
- [`MainBusFaultRecordingTests`](../../tests/BadMango.Emulator.Bus.Tests/MainBusFaultRecordingTests.cs)
- [`DevicePageIdTests`](../../tests/BadMango.Emulator.Bus.Tests/DevicePageIdTests.cs)
- [`DeviceRegistryTests`](../../tests/BadMango.Emulator.Bus.Tests/DeviceRegistryTests.cs)

Debugger command and listener tests already live in
`BadMango.Emulator.Debug.Infrastructure.Tests` and do not move:

- [`DebugCommandsTests.Watch`](../../tests/BadMango.Emulator.Debug.Infrastructure.Tests/DebugCommandsTests.Watch.cs)
- [`DebugCommandsTests.BusFaults`](../../tests/BadMango.Emulator.Debug.Infrastructure.Tests/DebugCommandsTests.BusFaults.cs)
- [`TracingDebugListenerTests`](../../tests/BadMango.Emulator.Debug.Infrastructure.Tests/TracingDebugListenerTests.cs)
- [`MachineDebugSessionFaultRegisterTests`](../../tests/BadMango.Emulator.Debug.Infrastructure.Tests/MachineDebugSessionFaultRegisterTests.cs)

CPU instruction-trace coverage and the mapping-trace tests live in the
cross-suite project.

> **Current location (until #565):** `tests/BadMango.Emulator.Tests`; target:
> subject-specific test projects, including `tests/BadMango.Emulator.Core.Tests`
> (namespace unchanged).

- [`CpuDebugSupportTests`](../../tests/BadMango.Emulator.Tests/CpuDebugSupportTests.cs)
- [`MappingTraceTests`](../../tests/BadMango.Emulator.Tests/MappingTraceTests.cs)

Session DTO tests and the text-editor tests are still beside the projects
that fold later:

> **Current location (until #560):** `tests/BadMango.Emulator.Debug.Abstractions.Tests`;
> target: `tests/BadMango.Emulator.Debug.Infrastructure.Tests` (namespace
> unchanged).
>
> **Current location (until #561):** `tests/BadMango.Emulator.TextEditor.Tests`;
> target: `tests/BadMango.Emulator.Debug.UI.Tests` (namespace unchanged).

- [`DebugAbstractionsJsonGoldenTests`](../../tests/BadMango.Emulator.Debug.Abstractions.Tests/DebugAbstractionsJsonGoldenTests.cs)
- [`TextEditorWindowTests`](../../tests/BadMango.Emulator.TextEditor.Tests/TextEditorWindowTests.cs)

## Source of truth

- [Architecture index](../../ARCHITECTURE.md) — system picture and the
  current-location convention.
- [#536 audit](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
  and the
  [restructure proposal](../Architecture%20Docs%20Restructure%20Proposal.md) —
  section status for 3.3 and 9.1–9.3.
- [Architecture Spec v1.0 Part 1](../Architecture%20Spec%20v1.0%20Part%201.md)
  §3.3 and
  [Part 3](../Architecture%20Spec%20v1.0%20Part%203.md) §9.1–§9.3 — historical
  sketches. Where they disagree with the code, the code and the audit win.
- [Bus and MMU](bus-mmu.md) and the
  [fast-path specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md#9-fault-registers)
  — `FaultStatus`, `FaultAddress`, `FaultKind`, and `ABORT`.
- [CPU core](cpu.md) — `AbortReaction` and 65C02 execution.
- [#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
  and the
  [solution consolidation design](../Solution%20Consolidation%20Design.md) —
  target assemblies. Namespaces stay unchanged.
