# CPU Core and Shared Register Model

This page documents the 65C02 implementation as built, the shared register file and
mode helpers used by the CPU family, and CPU fault/halt behavior that currently
exists in the emulator.

**Covers:** the implemented `Cpu65C02` execution path, the shared `Registers` and
register/mode helpers, `AbortReaction`, CPU construction, reset and PC access,
stop/halt coordination, and the audited compatibility, register-model, and CPU
fault sections.

**Does not cover:** the 65816 or 65832 execution pages, the bus/MMU implementation
itself, signal-bus topology, scheduler internals, or privileged architecture that
is not yet implemented. Those subjects are companions or are covered separately.

## Target layout

In the target layout, CPU implementations live in `BadMango.Emulator.Core` under
`BadMango.Emulator.Emulation.Cpu`, and shared CPU contracts and registers live in
`BadMango.Emulator.Core.Cpu`. Moved source and namespace names keep their existing
names; this page does not propose namespace renames.

> **Current location (until #562):** `src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs`,
> `src/BadMango.Emulator.Emulation/Cpu/Cpu65C02OpcodeTableBuilder.cs`, and the other
> CPU implementation files; target: `src/BadMango.Emulator.Core/Cpu/` (namespace unchanged).

The shared register and mode-helper files already live in the surviving Core
assembly:

- [`Registers`](../../src/BadMango.Emulator.Core/Cpu/Registers.cs) — shared 65C02,
  65816, and 65832 register storage.
- [`RegisterHelpers`](../../src/BadMango.Emulator.Core/Cpu/RegisterHelpers.cs) —
  architectural-mode and effective-width helpers.
- [`ArchitecturalMode`](../../src/BadMango.Emulator.Core/Cpu/ArchitecturalMode.cs) —
  65C02, 65816, and 65832 mode values.
- [`AbortReaction`](../../src/BadMango.Emulator.Core/Cpu/AbortReaction.cs) — the
  65C02's host-visible reaction to the `ABORT` signal.
- [`ICpu`](../../src/BadMango.Emulator.Core/Interfaces/Cpu/ICpu.cs) — the CPU
  contract.

The target Core assembly also absorbs the Bus assembly. `Bus.Tests` is the test
location that moves under #562, while CPU/Core/Emulation coverage currently in
`BadMango.Emulator.Tests` moves under #565.

> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`; target:
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).
>
> **Current location (until #565):** `tests/BadMango.Emulator.Tests` cross-suite
> CPU/Core/Emulation coverage; target: subject-specific test projects, including
> Core.Tests (namespace unchanged).

## 65C02 implementation

**Status: Partly implemented**

`Cpu65C02` is the only CPU implementation that currently executes instructions.
It derives from `CpuBase`, receives an `IEventContext`, builds a 256-entry opcode
table, and dispatches instructions through cached opcode dispatch delegates.
The table covers the 65C02 instruction set, includes the 65C02-specific addressing
and instruction entries, and leaves no unimplemented slot in the finished table:
unused entries are NOP instructions rather than illegal-opcode entries.

The implementation preserves WDC 65C02 behavior for opcode `$42`: it is dispatched
through `DispatchWdm`, not by the shared NOP dispatch. On this 65C02 table,
`DispatchWdm` is a 2-byte, 2-cycle NOP. The 65816 and 65832 classes are separate
companion implementations and do not replace this behavior.

Key implementation paths:

- [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs)
- [`Cpu65C02OpcodeTableBuilder`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02OpcodeTableBuilder.cs)
- [`Instructions`](../../src/BadMango.Emulator.Emulation/Cpu/Instructions.cs)
- [`Instructions.65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Instructions.65C02.cs)
- [`CpuBase`](../../src/BadMango.Emulator.Emulation/Cpu/CpuBase.cs)

> **Current location (until #562):** `src/BadMango.Emulator.Emulation/Cpu/`;
> target: `src/BadMango.Emulator.Core/Cpu/` (namespaces unchanged).

### Execution and cycle behavior

The 65C02 executes one instruction in `Step`, advances the scheduler, handles
RESET and interrupts at instruction boundaries, and dispatches the opcode table.
The opcode metadata supplies operand length and base cycle count; page-cross,
branch-taken, decimal-mode, and other instruction-specific penalties remain
handled by the execution path and opcode metadata.

The implementation supports WAI and STP:

- WAI establishes `HaltState.Wai`; IRQ and NMI can resume execution.
- STP establishes `HaltState.Stp`; the stopped state cannot be resumed by an
  interrupt and requires reset.
- Interrupt handling gives NMI priority over IRQ, and IRQ is maskable through the
  I status flag.

The implemented reset path reads the reset vector at `$FFFC` and initializes the
shared register file with compatibility mode selected. The machine-level reset
signal is separate and holds RESET while the line is asserted.

### Companion CPU families

**Status: Partly implemented**

`CpuFamily` names `Cpu65C02`, `Cpu65816`, and `Cpu65832`. `Cpu65816` and
`Cpu65832` are placeholders: `Step` and `Reset` throw
`NotImplementedException`. They must not be described as runnable CPU
implementations.

- [65816 companion issue](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/540)
- [65832 companion issue](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541)

## 1.2 Compatibility Layers

**Status: Audit: Partly implemented**

The shared mode values are a compatibility-model implementation, not nested guest
runtimes. `RegisterHelpers.GetArchitecturalMode` derives a mode from CP and E:

- CP=true, E=true → `Mode65C02`
- CP=true, E=false → `Mode65816`
- CP=false → `Mode65832`

There is no built PocketGS or PocketME compatibility/runtime stack. Pocket2e is
the built machine and uses `Cpu65C02`.

## 2.1 CPU Family Inheritance

**Status: Audit: Partly implemented**

`ICpu` is the shared family contract. It exposes capabilities, registers,
`Step`, `Reset`, IRQ/NMI signaling, halt state, and stop coordination. The audit
form's `Family` and `CurrentMode` properties are not present on `ICpu`.
`CpuFamily` is available separately and lists all three chips.

Only `Cpu65C02.Step` executes instructions. `Cpu65816` and `Cpu65832` remain
explicitly unimplemented.

## 2.2 Register Models

**Status: Audit: Implemented**

Every CPU variant shares one `Registers` struct. Effective widths are selected
from the mode flags and status flags rather than by separate per-chip register
structures.

### Design Philosophy: Unified Registers with Multi-Size Views

**Status: Implemented**

The file has maximum-width backing storage and typed multi-size views. The
effective register width is selected at runtime by CP, E, and, for 65816
compatibility/native behavior, M/X.

### Unified Registers Structure

**Status: Audit: Partly implemented**

The shared file contains `A`, `X`, `Y`, `D`, `SP`, `PC`, `P`, `E`, `CP`, `DBR`,
`PBR`, `R0` through `R7`, and `System`. These fields match the unified-register
contract.

`SystemRegisters` stores CR0, PTBR, VBAR, FAR, FSC, and TLS, but a running CPU
does not consult or enforce those privileged/system fields. `ControlRegisterHelpers`
is only a review candidate and is not a running privileged CPU implementation.

> **Pending removal candidate (until #566):** `ControlRegisterHelpers` in
> `src/BadMango.Emulator.Core/Cpu/ControlRegisterHelpers.cs` (namespace unchanged);
> removal is conditional on #566's verification.

### Register View Types

**Status: Implemented**

`RegisterAccumulator`, `RegisterIndex`, `RegisterStackPointer`,
`RegisterDirectPage`, and `RegisterProgramCounter` provide 8-, 16-, and 32-bit
views where applicable. They retain full backing storage while each accessor
selects or sets the relevant width.

### Mode Detection Helpers

**Status: Implemented**

The shared `RegisterHelpers` extension methods implement:

- `GetArchitecturalMode`
- `GetAccumulatorSize`
- `GetIndexSize`
- `Is65C02Mode`
- `Is65816Mode`
- `Is65832Mode`

### Mode Behavior Summary

**Status: Audit: Partly implemented**

The effective-width helpers implement the 65C02, 65816, and 65832 columns for
accumulator and index sizes. This does not make the companion CPUs runnable.
`Cpu65816` does not implement forced SP high-byte behavior for 65816 emulation
mode, and no CPU executes the 65832 column.

### Initialization Examples

**Status: Audit: Partly implemented**

`Registers(bool compat, Word resetVector)` sets PC to the supplied reset vector,
P to reset flags, and CP/E to compatibility/emulation values. It sets SP to
`$FF`; stack pushes use the 65C02 stack page, `$0100`, from `Cpu65C02Constants`.

The constructor is not a 65816 or 65832 bootstrap. The 65832 default constructor
sets native flag values but does not implement a running native CPU.

### Usage in Instructions

**Status: Audit: Partly implemented**

Instruction handlers use shared mode-aware register access, including
`GetAccumulatorSize`, and the 65C02 table is the implementation that currently
closes over concrete 65C02 behavior. `Cpu65816` and `Cpu65832` do not run these
handlers.

### Privilege Levels (65832)

**Status: Audit: Partly implemented**

The 65832 `PrivilegeLevel` enum defines User, Kernel, and Hypervisor values, and
CR0 control bits are represented in `SystemRegisters`. No running CPU checks
user-mode access to system registers or traps a privileged access.

## 4.10 CPU Fault Handling Pattern

**Status: Audit: Partly implemented**

The 65C02 implementation does not contain `RaiseDataAbort` or `RaiseInstructionAbort`.
The normative bus fault and fast-path behavior is in
[`specs/Bus Fast Path and Fault Registers Specification.md`](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).

In the built CPU path, fetches and data accesses use the memory bus and fast-path
operations. An unmapped instruction fetch can set `HaltState.Stp` in the default
65C02 configuration. Fault status and address remain latched on the bus.
`BusResult.Cycles` is not added into `Registers.TCU`.

`AbortReaction` is a host/65C02 behavior:

- `NoOp` is the boot default. It ignores the asserted `ABORT` line and leaves bus
  fault state latched; execution may still stop on an unmapped fetch.
- `HostBreak` is debugger-selected. It requests a host stop at the next
  instruction boundary without setting `Stp`, pushing a vector, or changing
  registers.

Reset, including machine reset, does not change `AbortReaction`.

## Gap 3: CPU Interface for PC Access

**Status: Audit: Implemented**

The separate PC-access API considered in the v1.0 text is not needed:
`Cpu65C02.Reset` reads `$FFFC` itself and sets PC. `ICpu` also provides `GetPC`
and `SetPC` for inspection and debugger use.

## Gap 7: CPU Construction Parameters

**Status: Audit: Implemented**

Machine construction uses `WithCpu` / `WithCpuFactory`; Pocket2e passes the
machine event context to `Cpu65C02(context)`. The CPU therefore receives its bus,
signal bus, scheduler, and trap infrastructure through the event context rather
than a direct unrelated constructor shape.

## Gap 8: Stop/Halt Coordination

**Status: Audit: Implemented**

`ICpu` exposes stop coordination and `Cpu65C02` honors the stop request.
`HaltState` distinguishes `Wai` and `Stp`, and `Machine` and the CPU share stop
state for run-loop and debugger control:

- `Wai` stops instruction execution temporarily and can resume from an interrupt.
- `Stp` is a permanent CPU halt until reset.
- `RequestStop` requests a host/debugger boundary stop without changing normal
  register or vector behavior.

## Tests and evidence

CPU behavior is covered by CPU, opcode, bus-cycle, hardware-vector, interrupt,
halt, reset, and fast-path tests in `BadMango.Emulator.Tests`.

> **Current location (until #565):** `tests/BadMango.Emulator.Tests` CPU and
> 65C02 test files; target: subject-specific test projects, including
> `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

Representative current test files include:

- [`Cpu65C02Tests`](../../tests/BadMango.Emulator.Tests/Cpu65C02Tests.cs)
- [`Cpu65C02AbortReactionTests`](../../tests/BadMango.Emulator.Tests/Cpu65C02AbortReactionTests.cs)
- [`Cpu65C02HardwareVectorTests`](../../tests/BadMango.Emulator.Tests/Cpu65C02HardwareVectorTests.cs)
- [`InterruptAndHaltTests`](../../tests/BadMango.Emulator.Tests/InterruptAndHaltTests.cs)
- [`W65C02OpcodeMetadataTests`](../../tests/BadMango.Emulator.Tests/W65C02OpcodeMetadataTests.cs)

