# Signal Bus and Interrupts

**Summary:** This page describes the emulator's signal fabric and the 65C02 reactions to
IRQ, NMI, RESET, and `ABORT`. It records the implemented edge and level behavior separately
from the Apple II interrupt conventions that are still aspirational.

**Covers:** Signal-line contracts and implementation, multiple-asserter behavior, attention
checks, 65C02 IRQ/NMI/RESET/`ABORT` reactions, Apple II interrupt polling, service conventions,
and IRQ vectors.

**Does not cover:** 65816/65832 interrupt and abort implementations, bus routing and fault
register internals beyond their signal interface, scheduler timing, or changes to the CPU or
consolidated assembly layout.

Coverage and status are audited by
[#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536) and
[`Architecture Docs Restructure Proposal.md`](../Architecture%20Docs%20Restructure%20Proposal.md).
Target layout follows [#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
and [`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md); the namespace
names below are existing namespaces and do not change during the pending project fold.

## Target module placement

Signal contracts and the fabric belong in `BadMango.Emulator.Core`:

- `BadMango.Emulator.Core.Interfaces.Signaling.ISignalBus`
- `BadMango.Emulator.Core.Signaling.SignalLine`
- `BadMango.Emulator.Core.Signaling.SignalBus`
- `BadMango.Emulator.Emulation.Cpu` 65C02 reactions

The existing Core signal files already occupy the target assembly:

- [`ISignalBus`](../../src/BadMango.Emulator.Core/Interfaces/Signaling/ISignalBus.cs)
- [`SignalLine`](../../src/BadMango.Emulator.Core/Signaling/SignalLine.cs)
- [`SignalBus`](../../src/BadMango.Emulator.Core/Signaling/SignalBus.cs)

The 65C02 implementation is not yet folded into Core.

> **Current location (until #562):** `src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs`;
> target: `src/BadMango.Emulator.Core/Cpu/` (namespace unchanged).

Bus fault code that drives `ABORT` is also pending the Core fold.

> **Current location (until #562):** `src/BadMango.Emulator.Bus/MainBus.cs` and
> `src/BadMango.Emulator.Bus/MainBus.FaultRegisters.cs`; target: `src/BadMango.Emulator.Core/`
> (namespaces unchanged).
>
> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`; target:
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).
>
> **Current location (until #565):** `tests/BadMango.Emulator.Tests` cross-suite CPU
> tests; target: subject-specific test projects (namespace unchanged).

## 5.1 Signal Model

**Status: Partly implemented**

The signal line contract contains `IRQ`, `NMI`, `Reset`, `RDY`, `DmaReq`, and `Abort`.
The original `Sync` member is replaced by `Abort`: `Sync` is not an instruction-fetch marker
in the implementation. `MainBus` fault registers drive `SignalLine.Abort` on the machine's
signal bus when non-debug fault status is latched.

The existing implementation tracks a set of asserting device IDs for each line. A line remains
asserted until all relevant devices deassert it. `Assert` and `Deassert` raise
`SignalChanged` only when the aggregate line state changes. `RDY` and `DmaReq` are enum
members with no callers in the current implementation.

NMI is edge-triggered. A deasserted-to-asserted transition records a pending edge, and
`ConsumeNmiEdge()` clears it when the CPU consumes the edge. Keeping the line asserted does
not generate another edge; releasing and reasserting it does.

RESET is level-sensitive while asserted and edge-sensitive on release. While held, the 65C02
does not execute instructions. Releasing it consumes the latched release edge and runs the
reset entry sequence.

`AttentionPending` is the 65C02's per-instruction fast check. It is set for a pending NMI
edge, asserted IRQ or RESET, latched RESET release, asserted `ABORT`, or a queued service
request. The CPU checks the more specific lines only at an instruction boundary.

Relevant implementation and tests:

- [`ISignalBus`](../../src/BadMango.Emulator.Core/Interfaces/Signaling/ISignalBus.cs)
- [`SignalBus`](../../src/BadMango.Emulator.Core/Signaling/SignalBus.cs)
- [`SignalLine`](../../src/BadMango.Emulator.Core/Signaling/SignalLine.cs)
- [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs)
- [`SignalBusTests`](../../tests/BadMango.Emulator.Bus.Tests/SignalBusTests.cs)
- [`MainBusAbortSignalTests`](../../tests/BadMango.Emulator.Bus.Tests/MainBusAbortSignalTests.cs)

### 5.1.1 Apple II Interrupt Model

**Status: Partly implemented**

The Apple II model is a wired-OR IRQ line: any asserting device holds the shared IRQ line
asserted, and the CPU sees only that aggregate state. The signal bus records asserting device
IDs, but that information is not available to emulated software. A guest interrupt handler
must poll peripheral status registers as the hardware convention requires.

No card in `src/` currently publishes the proposed `$C0n0-$C0nF` status-bit convention by
itself. Thus the generic wired-OR behavior and device-ID observability are implemented, while
a completed Apple II card-identification and dispatch model is not.

Relevant tests include [`InterruptAndHaltTests`](../../tests/BadMango.Emulator.Tests/InterruptAndHaltTests.cs)
and [`SignalBusTests`](../../tests/BadMango.Emulator.Bus.Tests/SignalBusTests.cs).

### 5.1.2 Interrupt Service Conventions

**Status: Aspirational**

Peripheral cards do not have one standardized status-register offset or interrupt handler.
Card-specific status offsets and handlers remain outside the implemented generic model.
The examples in older specifications for Super Serial Card, mouse, and Mockingboard behavior
are descriptive or proposed; their status offsets and dispatch code are not implemented.

Likewise, the ProDOS MLI INSTALL_INTERRUPT ($40) and REMOVE_INTERRUPT ($41) service is not
implemented. The generic emulator may support guest software that performs its own interrupt
polling, but it does not yet maintain or dispatch a ProDOS interrupt-handler table.

### 5.1.3 IRQ Vector Architecture

**Status: Partly implemented**

The 65C02 loads hardware vectors through:

| Reaction | Vector address | Implementation |
| --- | --- | --- |
| NMI | $FFFA-$FFFB | Implemented |
| IRQ | $FFFE-$FFFF | Implemented |
| RESET | $FFFC-$FFFD | Implemented |

Hardware IRQ and NMI entry each load `PC` from the corresponding vector after the normal
65C02 interrupt sequence. The implemented 65C02 vector constants are
[`Cpu65C02Constants`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02Constants.cs).

Apple II $03F0 and $03FE addresses are ordinary RAM in the Pocket2e map; the emulator does
not treat them as special-owned interrupt vectors. Guest ROM or software may use those page-3
locations according to the host memory map.

The 65C02 `ABORT` reaction is separate from IRQ and NMI. `AbortReaction.NoOp` is the default:
the line remains ignored as a host reaction, while fault status remains latched and an
unmapped fetch can set `HaltState.Stp`. `AbortReaction.HostBreak` is selected by the debugger
and makes the next instruction boundary stop before interrupt or reset entry. Reset leaves the
debugger setting unchanged.

Relevant implementation and tests:

- [`AbortReaction`](../../src/BadMango.Emulator.Core/Cpu/AbortReaction.cs)
- [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs)
- [`Cpu65C02AbortReactionTests`](../../tests/BadMango.Emulator.Tests/Cpu65C02AbortReactionTests.cs)
- [`InterruptAndHaltTests`](../../tests/BadMango.Emulator.Tests/InterruptAndHaltTests.cs)
- [`Cpu65C02Tests`](../../tests/BadMango.Emulator.Tests/Cpu65C02Tests.cs)

The 65816/65832 abort-vector behavior described by reference specifications is not claimed
as implemented here. No 65816 or 65832 CPU implementation is implied by the target module
description.
