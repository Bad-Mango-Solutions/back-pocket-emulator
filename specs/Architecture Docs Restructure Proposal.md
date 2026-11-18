# Architecture Docs Restructure Proposal

Proposal only, for #536 (part of #501). This file does not move, rewrite, or delete any existing document, and it does not change code. The per-layer rewrites are the follow-up issues in [section 5](#5-proposed-follow-up-issues).

The audit follows the headings in the four v1.0 files. The table of contents in `specs/Architecture Spec v1.0 Part 1.md` describes a different Part 2 outline than the headings in `specs/Architecture Spec v1.0 Part 2.md` (its "7.1 Peripheral Interface" is this file's §7.3). Use the headings.

**Status**

| Status | Meaning |
| --- | --- |
| Implemented | The section's contract is in the code. |
| Partly implemented | A real type exists; the section is incomplete or its details differ. |
| Superseded | A later spec or the code replaced this contract. Do not copy it forward as normative text. |
| Aspirational | No implementation. |

**Code** is `project` / `type`. The namespace matches the project name unless a second name is given. `—` means there is no type to cite.

Checked against `main`. Claims below were read off the named types.

---

## 1. Audit

### Part I — Project hierarchy and target machines

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 1.1 Machine Taxonomy | Partly implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions`; `BadMango.Emulator.Emulation` / `Cpu65C02` | machine | Pocket2e is the built machine (128KB, `CpuFamily.Cpu65C02`). `Cpu65816` and `Cpu65832` throw `NotImplementedException` from `Step` and `Reset`. |
| 1.2 Compatibility Layers | Partly implemented | `BadMango.Emulator.Core` / `ArchitecturalMode` | cpu | `RegisterHelpers.GetArchitecturalMode` maps CP/E to `Mode65C02`, `Mode65816`, and `Mode65832`. There is no nested guest runtime. |
| 1.3 Historical Context | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | devices | Framing section. The behavior it points at is the I/O page, soft switches, floating bus, and seven slots below. |
| 1.3.1 Why Memory-Mapped I/O? | Implemented | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | Devices register on the $C000 page. Reads and writes are handlers, not array loads. |
| 1.3.2 The "Soft Switch" Concept | Implemented | `BadMango.Emulator.Bus` / `ISoftSwitchProvider` | devices | `VideoDevice` registers $C050/$C051 as any-access toggles. Side effects run on read and on write. |
| 1.3.3 The Floating Bus | Partly implemented | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | Unregistered I/O reads return `0xFF` (`FloatingBusValue`). There is no last-bus or video-fetch open-bus model. |
| 1.3.4 Why 7 Slots? | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | devices | `SlotCount` is 7. Slot 0 is the language card and is not in that array. |

### Part II — CPU architecture

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 2.1 CPU Family Inheritance | Partly implemented | `BadMango.Emulator.Core` / `ICpu`; `CpuFamily` | cpu | `CpuFamily` has the three chips. `ICpu` exposes `Capabilities`, `Registers`, `Step`, `Reset`, `SignalIRQ`, and `SignalNMI`, and does not expose `Family` or `CurrentMode`. Only `Cpu65C02.Step` runs. |
| 2.2 Register Models | Implemented | `BadMango.Emulator.Core` / `Registers` | cpu | One register file for every chip. The rows under this heading are the contract. |
| 2.2 / Design Philosophy: Unified Registers with Multi-Size Views | Implemented | `BadMango.Emulator.Core` / `Registers` | cpu | Width comes from E, CP, and the M/X flags via `RegisterHelpers`, not from a per-chip struct. |
| 2.2 / Unified Registers Structure | Partly implemented | `BadMango.Emulator.Core` / `Registers`; `SystemRegisters` | cpu | A, X, Y, D, SP, PC, P, E, CP, DBR, PBR, R0–R7, and `System` match the snippet. `SystemRegisters` (CR0, PTBR, VBAR, FAR, FSC, TLS) is stored and never consulted by a running CPU. |
| 2.2 / Register View Types | Implemented | `BadMango.Emulator.Core` / `RegisterAccumulator` | cpu | `RegisterIndex`, `RegisterStackPointer`, `RegisterDirectPage`, and `RegisterProgramCounter` are in the same file and expose the 8/16/32 views. |
| 2.2 / Mode Detection Helpers | Implemented | `BadMango.Emulator.Core` / `RegisterHelpers` | cpu | `GetArchitecturalMode`, `GetAccumulatorSize`, `GetIndexSize`, `Is65C02Mode`, `Is65816Mode`, and `Is65832Mode` match the snippet. |
| 2.2 / Mode Behavior Summary | Partly implemented | `BadMango.Emulator.Core` / `RegisterHelpers` | cpu | The size helpers match the table. `Cpu65816` does not force SP's high byte to $01, and no CPU executes the 65832 column. |
| 2.2 / Initialization Examples | Partly implemented | `BadMango.Emulator.Core` / `Registers` | cpu | `Registers(bool compat, Word resetVector)` sets E, CP, P, and PC. SP is `SetByte(0xFF)`; the $0100 page is `Cpu65C02Constants.StackBase` at push time. |
| 2.2 / Usage in Instructions | Partly implemented | `BadMango.Emulator.Emulation` / `Instructions` | cpu | `Instructions` and `Instructions.Transfer` call `GetAccumulatorSize`. Opcode entry is `Cpu65C02OpcodeTableBuilder`. The 65816 and 65832 do not run those handlers. |
| 2.2 / Privilege Levels (65832) | Partly implemented | `BadMango.Emulator.Core` / `PrivilegeLevel` | cpu-65832 | User=0, Kernel=1, Hypervisor=2 matches, and CR0 bits 0–1 are documented on `SystemRegisters`. Nothing traps a user-mode system-register access. `BadMango.Emulator.Bus.PrivilegeLevel` is a second enum (Ring0–Ring3) stored on `PageEntry` and not checked (`Bus Fast Path` §17.2, #507). |

### Part III — Memory and MMU

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 3.1 Address Space Models | Partly implemented | `BadMango.Emulator.Bus` / `MainBus` | bus | Every built machine uses a flat 4KB `PageEntry[]` (`address >> 12`). Pocket2e is not a soft-switch-only map. PocketGS (24-bit) and PocketME (two-level tables) are not built. |
| 3.2 Page Table Entry Format | Aspirational | — | cpu-65832 | No `PageTableEntry` type. Host routing is `PageEntry`, which is a different struct. The bit layout lives only in this section and in `reference/Privileged architecture spec v0.1 for 65832.md`. |
| 3.3 Device Page Classes | Partly implemented | `BadMango.Emulator.Bus` / `DevicePageClass` | observe | The enum exists. Its numbers do not match §3.3 or §9.2 (code: Storage=3, Timer=4, Input=5, Audio=6, Network=7, Framebuffer=8, Debug=0xF). |
| 3.4 Physical Memory Map (PocketME) | Aspirational | — | cpu-65832 | No 256KB boot ROM at physical 0 and no high-ROM alias. `PhysicalMemory` is a host byte pool sized by the machine profile. |
| 3.5 Fault Status Codes | Superseded | `BadMango.Emulator.Bus` / `FaultKind` | bus | `FaultStatusCode` was never added. The host latch is `MainBus.FaultStatus` (`FaultKind`). `SystemRegisters.FSC` is an unused `DWord`. Normative text is `specs/Bus Fast Path and Fault Registers Specification.md` §9–§10. |

### Part IV — Main bus

`specs/Bus Fast Path and Fault Registers Specification.md` is the normative bus text until the bus/MMU page exists. `reference/Main bus architecture.md` is the historical note that Part IV was written from. Its direct-`IBusTarget`, `Func<>`, and function-pointer fast path is superseded by that spec's `byte[]` index (§7 and §17.12).

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 4.1 Design Philosophy | Partly implemented | `BadMango.Emulator.Bus` / `MainBus` | bus | Data plane is the `PageEntry` fast path. Control plane is remap, swap groups, and `MachineBuilder`. Observability is `BusFaultRing`, which records faults, not every access. |
| 4.2 Access Context | Implemented | `BadMango.Emulator.Bus` / `BusAccess` | bus | `BusAccessMode`, `AccessIntent`, and `AccessFlags` match. The struct also carries `PrivilegeLevel` and `Pc`. |
| 4.3 Fault Model | Superseded | `BadMango.Emulator.Bus` / `FaultKind`; `BusFault` | bus | `FaultKind` is a `[Flags] ushort` with a high byte (`Write`, `Fetch`, `BankCrossing`, `Privilege`). `BusFault` is built on a fault. The v1.0 `byte` enum and always-present success fault are retired by the fast-path spec §9–§12. |
| 4.4 Bus Result Types | Partly implemented | `BadMango.Emulator.Bus` / `BusResult` | bus | `BusResult` and `BusResult<T>` still wrap `Try*`. `MainBus` success is a zero `BusFault`. `CpuBase` byte access indexes `FastBacking` and does not build a result. |
| 4.5 Page Table Structure | Partly implemented | `BadMango.Emulator.Bus` / `PageEntry` | bus | `TargetCaps`, `PagePerms`, and `PageEntry` exist, plus `FastBacking`, `FastBase`, `FastOps`, and privilege minimums. `RegionTag` is Ram/Rom/Io/Slot/…, not the spec's MainRAM/AuxRAM/ROM/IOPage list. Minimums are stored and not enforced. |
| 4.6 Bus Target Interface | Implemented | `BadMango.Emulator.Bus` / `IBusTarget` | bus | `Read8`/`Write8` are required. Wide ops default to byte decomposition. `ICompositeTarget.ResolveTarget` is what `MainBus` calls for composite pages. |
| 4.7 Memory Bus Interface | Partly implemented | `BadMango.Emulator.Bus` / `IMemoryBus` | bus | `Try*`, `RemapPage`, `RemapPageRange`, peek/poke, and the fault registers are on the interface. The CPU byte path does not call `Try*` when `FastOps` hits. |
| 4.8 Main Bus Implementation | Partly implemented | `BadMango.Emulator.Bus` / `MainBus` | bus | Routing lives in `MainBus`, `MainBus.PageTable`, `MainBus.WideAccess`, `MainBus.FastPath`, and `MainBus.FaultRegisters`. The sample that builds a success `BusFault` per byte is not the hot path. |
| 4.9 Access Policy Rules | Partly implemented | `BadMango.Emulator.Bus` / `MainBus.WideAccess` | bus | Unmapped, permission, cross-page decompose, `Decompose`, and `Atomic`+`SupportsWide` are in `TryRead16`. `FaultKind.Nx` is not raised on the 65C02 path: fetch uses `FastPathOps.Read` and `BusAccessMode.Decomposed` (fast-path spec §17.1). |
| 4.10 CPU Fault Handling Pattern | Partly implemented | `BadMango.Emulator.Emulation` / `Cpu65C02` | cpu | No `RaiseDataAbort`. An unmapped fetch can set `HaltState.Stp`. `AbortReaction` is `NoOp` unless the debugger selects `HostBreak` (`AbortCommand`). `BusResult.Cycles` is not added into `Registers.TCU` (fast-path spec §17.3). |

### Part V — Signal bus

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 5.1 Signal Model | Partly implemented | `BadMango.Emulator.Core` / `SignalBus` | signals | `Assert`/`Deassert` track a set of asserters. NMI is an edge (`ConsumeNmiEdge`). RESET latches a release edge. The spec's `Sync` line is `SignalLine.Abort`, driven by `MainBus.FaultRegisters`. `RDY` and `DmaReq` are enum members with no callers. |
| 5.1.1 Apple II Interrupt Model | Partly implemented | `BadMango.Emulator.Core` / `SignalBus` | signals | IRQ is a wired-OR of asserter ids. Emulated code cannot read those ids. No card in `src/` publishes the "status bit in $C0nX" convention. |
| 5.1.2 Interrupt Service Conventions | Aspirational | — | signals | SSC, mouse, and Mockingboard status offsets are not implemented. ProDOS MLI $40/$41 is not implemented. |
| 5.1.3 IRQ Vector Architecture | Partly implemented | `BadMango.Emulator.Emulation` / `Cpu65C02` | signals | Hardware IRQ and NMI load PC from `Cpu65C02Constants.IrqVector` ($FFFE) and `NmiVector` ($FFFA). $03F0 and $03FE are ordinary RAM in the Pocket2e map, not emulator-owned vectors. |

### Part VI — Scheduler and timing

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 6.1 Core Concepts | Implemented | `BadMango.Emulator.Core` / `Cycle`; `BadMango.Emulator.Bus` / `ScheduledEventKind` | sched | `Cycle` matches. `ScheduledEventKind` adds `None` and `VideoBlank` beside the spec's list. |
| 6.2 Scheduler Interface | Implemented | `BadMango.Emulator.Bus` / `Scheduler` | sched | `IScheduler` is a discrete-event queue: `ScheduleAt`, `Advance`, `DispatchDue`, `Reset`. It is single-threaded; host threads use `IMachineDispatcher.Post`. |
| 6.3 CPU-Scheduler Integration | Partly implemented | `BadMango.Emulator.Emulation` / `Cpu65C02` | sched | `Cpu65C02` advances the concrete `Scheduler` by the instruction's cycles. `Cpu65816` and `Cpu65832` do not. `VideoDevice` schedules VBL on the same scheduler. |

### Part VII — Devices and peripherals

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 7.1 The Apple II I/O Page | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | devices | $C000–$CFFF is one composite page: soft switches, slot ROM, expansion ROM. |
| 7.1.1 Why This Layout Exists | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | devices | The $Cxxx split is the dispatch inside `CompositeIOTarget`, not a comment. |
| 7.1.2 The Selection Protocol | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | devices | A $Cn00–$CnFF access calls `SelectExpansionSlot`. |
| 7.1.3 Read vs. Write Behavior | Implemented | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | Handlers are separate read and write delegates. A missing read returns `0xFF`. A missing write is a no-op. |
| 7.2 Soft Switch Sub-Regions | Implemented | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | $C000–$C0FF is the dispatcher. $C080–$C0FF is installed per slot through `InstallSlotHandlers`. |
| 7.2.1 System Switches ($C000–$C07F) | Partly implemented | `BadMango.Emulator.Devices` / `AppleIIeKeyboard` | devices | Keyboard ($C000, $C010), `VideoDevice` ($C050–$C05F), `SpeakerController` ($C030 only), and `GameIOController` ($C060–$C07F) are registered. Cassette ($C020) and the $C040 strobes have no handler. |
| 7.2.2 Language Card Switches | Implemented | `BadMango.Emulator.Devices` / `LanguageCardDevice` | devices | Sixteen read handlers at $C080–$C08F (slot 0). Writes are ignored. Write-enable is the double-read of an odd address. Banks swap through `MainBus`. |
| 7.2.2.1 Language Card and CPU Vectors | Implemented | `BadMango.Emulator.Devices` / `LanguageCardDevice` | devices | $D000–$FFFF swaps as whole pages. `Cpu65C02` reads $FFFC/$FFFA/$FFFE through the bus with no vector special case. |
| 7.2.3 Slot Device I/O | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | devices | Each slot gets 16 bytes at $C090+ via `IOPageDispatcher`. `DiskIIController` is a slot card. |
| 7.3 Peripheral Interface | Implemented | `BadMango.Emulator.Bus` / `IPeripheral` | devices | `IMotherboardDevice` and `ISlotCard` extend it. `Reset()` is the power-on hook `Machine` calls. |
| 7.4 Slot Manager | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | devices | Install, expansion-ROM select, default $C800 ROM, and reset. |
| 7.5 Apple II Memory Map | Partly implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions` | machine | The Pocket2e 128KB map (main, aux, language card, ROM, I/O) is what `ConfigurePocket2eMemory` builds. PocketGS and PocketME maps are not built. |
| 7.6 The Composite I/O Page Handler | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | devices | `MachineBuilderExtensions.RegisterCompositeIOHandler` installs it. INTCXROM and INTC8ROM overlays live here. |
| 7.7 Expansion ROM Lifecycle | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | rom | Select on $Cnxx, deselect on $CFFF, default ROM underneath. |
| 7.7.1 Selection Triggers | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | rom | Slot ROM reads call `SelectExpansionSlot`. |
| 7.7.2 Deselection Trigger | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | rom | `ReadExpansionRom` and `WriteExpansionRom` call `DeselectExpansionSlot` when the offset is $xFFF. |
| 7.7.3 Trap Handler Implications | Partly implemented | `BadMango.Emulator.Bus` / `TrapRegistry` | rom | Slot and INTCXROM checks exist (`RequiresExpansionRom`, `IsIntC8RomEnabled`). `MachineBuilder` installs an empty registry, so nothing is suppressed in a normal boot. |
| 7.7.4 Internal ROM Soft Switches | Implemented | `BadMango.Emulator.Devices` / `Extended80ColumnDevice` | devices | $C006/$C007 and $C00A/$C00B drive `IInternalRomHandler` on `CompositeIOTarget`. This is the IIe card, not an IIc machine. |
| 7.8 Apple IIc Differences | Aspirational | — | compat | No IIc profile under `profiles/` and no `AppleIIc` type. Profiles are `pocket2e*.json` and `simple-65c02*.json`. |
| 7.8.1 No Physical Slots | Aspirational | — | compat | `SlotManager` always allocates seven slots. |
| 7.8.2 Memory-Mapped ROM Selection | Aspirational | — | compat | No built-in serial, mouse, or memory-expansion ROM windows. |
| 7.8.3 Firmware Switching | Partly implemented | `BadMango.Emulator.Bus` / `IInternalRomHandler` | compat | INTCXROM/$C006/$C007 exists for the IIe 80-column card. There is no IIc firmware image switch. |
| 7.8.4 Emulation Implications | Aspirational | — | compat | `RegisterIIcTraps` does not exist. |

### Part VIII — Compatibility personalities (65832)

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 8.1 Compatibility Context | Aspirational | — | compat | No `CompatibilityPersonality` or `CompatibilityController`. Pocket2e is a real machine, not a 65832 guest window. |
| 8.2 Device Page Object Model | Aspirational | — | compat | No `IDevicePageObject`. Devices implement `IBusTarget` or register on `IOPageDispatcher`. |
| 8.3 ROM Routine Interception | Partly implemented | `BadMango.Emulator.Bus` / `TrapRegistry` | rom | The registry and the 65C02 fetch check exist. A built machine registers no traps. |
| 8.3.1 Why Trap ROM Routines? | Partly implemented | `BadMango.Emulator.Bus` / `TrapRegistry` | rom | The mechanism is there for ROM-call replacement. Shipping profiles load ROM images; they do not replace monitor entry points. |
| 8.3.2 The Trap Mechanism | Implemented | `BadMango.Emulator.Bus` / `ITrapRegistry` | rom | Read, write, and call traps, with `TrapResult` and context-specific registration. |
| 8.3.3 ROM Address Space Classification | Partly implemented | `BadMango.Emulator.Bus` / `MemoryContexts` | rom | ROM, language-card RAM, aux RAM, and custom contexts exist. `Cpu65C02` gates on `ContainsAddress`, not on the active context. |
| 8.3.4 Fixed ROM Traps | Partly implemented | `BadMango.Emulator.Bus` / `ITrapRegistry` | rom | `Register` accepts a fixed address. No monitor or BASIC trap is registered in `src/`. |
| 8.3.5 Slot ROM Traps | Partly implemented | `BadMango.Emulator.Bus` / `ITrapRegistry` | rom | `RegisterSlotDependent` and `UnregisterSlotTraps` exist. No caller registers a $Cn00 handler. |
| 8.3.6 Expansion ROM Traps | Partly implemented | `BadMango.Emulator.Bus` / `TrapRegistry` | rom | Addresses $C800–$CFFF are marked `RequiresExpansionRom` and checked against `ActiveExpansionSlot`. No such trap is registered. |
| 8.3.7 Trap Registry Lifecycle | Implemented | `BadMango.Emulator.Bus` / `ITrapRegistry` | rom | Register, unregister, enable, clear, and `ITrapRegistryObserver`. |
| 8.3.8 Handling the $CFFF Access | Implemented | `BadMango.Emulator.Bus` / `CompositeIOTarget` | rom | $xFFF deselects the expansion ROM on read and on write, which is the banking event traps have to follow. |
| 8.3.9 Complete Trap Registry Interface | Implemented | `BadMango.Emulator.Bus` / `ITrapRegistry` | rom | The live interface is the snippet plus context, slot, and language-card overloads. |
| 8.3.10 Well-Known Trap Points | Aspirational | — | rom | Catalog only. No `Register` call site in `src/` names PLOT, COUT, RDKEY, or the Disk II boot entry. |
| 8.3.10 / Monitor ROM ($F800–$FFFF) | Aspirational | — | rom | Same catalog. `TrapCategory.MonitorRom` exists and is unused by production registration. |
| 8.3.10 / Applesoft BASIC ($D000–$F7FF) | Aspirational | — | rom | No BASIC trap table is registered. |
| 8.3.10 / Disk II Controller | Aspirational | — | rom | `DiskIIController` and `DiskIIBootRom` emulate the card. They are not trap handlers. |
| 8.3.11 CPU Integration | Partly implemented | `BadMango.Emulator.Emulation` / `Cpu65C02` | rom | The 65C02 consults `TrapRegistry.ContainsAddress` on fetch. `Cpu65816` and `Cpu65832` do not step. |
| 8.3.12 ROM-Free Operation Mode | Partly implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions` | rom | `WithRom(..., "Stub ROM")` and `DiskIIBootRomStub` exist. There is no `StubRom` type and no native handler per monitor entry. |

### Part IX — Observability and tracing

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 9.1 Trace Events | Aspirational | `BadMango.Emulator.Bus` / `BusFaultRing` | observe | No `BusTraceEvent` or `TraceRingBuffer`. `BusFaultRing` records faults. `TraceCommand` drives `InstructionTrace` on the CPU, which is a different stream. |
| 9.2 Device Registry | Partly implemented | `BadMango.Emulator.Bus` / `DeviceRegistry` | observe | `DeviceInfo` carries `Id`, `PageId`, `Kind`, `Name`, and `WiringPath`. `Register`, `TryGet`, `GetByClass`, and `GenerateId` exist. Class numbers do not match the snippet (see §3.3). |
| 9.3 Device Page ID Encoding | Partly implemented | `BadMango.Emulator.Bus` / `DevicePageId` | observe | The type exists and is 24 bits: class at bits 23–20 (4), instance at 19–8 (12), page at 7–0 (8). The section's 20-bit, 8-bit-instance table is not what the struct stores. |

### Part X — Boot and reset

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 10.1 Pocket2e Boot Sequence | Partly implemented | `BadMango.Emulator.Bus` / `Machine`; `BadMango.Emulator.Emulation` / `Cpu65C02` | machine | `Machine.BootAsync` runs the machine. `Cpu65C02.Reset` loads PC from $FFFC. Disk autostart and the monitor prompt are ROM code, not a boot loader in this repo. |
| 10.2 PocketGS Boot Sequence | Aspirational | `BadMango.Emulator.Emulation` / `Cpu65816` | cpu-65816 | The CPU class is a placeholder. No Mega II, no ROM03 path, no slot scan. |
| 10.3 PocketME Boot Sequence | Aspirational | `BadMango.Emulator.Core` / `SystemRegisters` | cpu-65832 | CR0, PTBR, and VBAR fields exist. Nothing writes them on reset. `Cpu65832.Reset` throws. |
| 10.4 Boot Handoff Structure | Aspirational | — | cpu-65832 | No `BootHandoff` type and no magic `0x4F484D42`. |

### Part XI — Implementation roadmap

These checklists are still unchecked in the spec. The code has moved past Phases 1–4. They do not belong in a layer page. Home is [section 3](#3-homes-for-non-architecture-content).

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| Phase 1: Foundation | Implemented | `BadMango.Emulator.Bus` / `MainBus` | roadmap | `BusAccess`, `PageEntry`, `SignalBus`, `DeviceRegistry`, and the bus tests are in the tree. |
| Phase 2: Apple II Integration | Implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions` | roadmap | Composite I/O, `SlotManager`, `RamTarget`/`RomTarget`, and `Cpu65C02` are wired by `MachineBuilder`. |
| Phase 3: Devices & Scheduler | Partly implemented | `BadMango.Emulator.Bus` / `Scheduler` | roadmap | Scheduler, `VideoDevice` VBL, `AppleIIeKeyboard`, `SpeakerController`, and `DiskIIController` exist. The phase goal "timing-accurate emulation" is broader than what those types do. |
| Phase 4: Observability & Debug | Partly implemented | `BadMango.Emulator.Debug.Infrastructure` / `WatchpointManager` | roadmap | Debugger commands, peek/poke, and watchpoints exist. The bus-access trace ring in §9.1 does not. |
| Phase 5: 65C816 Extension | Aspirational | `BadMango.Emulator.Emulation` / `Cpu65816` | roadmap | Placeholder class. No 24-bit machine, Mega II, SHR, or Ensoniq. |
| Phase 6: 65832 Fantasy CPU | Aspirational | `BadMango.Emulator.Emulation` / `Cpu65832` | roadmap | Register file and mode helpers exist. The CPU throws. No prefix opcodes, page-table walk, or privilege trap. |

### Part XII — Coding standards

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| 12.1 Naming Conventions | Implemented | `CONTRIBUTING.md` | contrib | `CONTRIBUTING.md` already states the I-prefix, PascalCase, and `_camelCase` rules. This section duplicates them. |
| 12.2 Performance Guidelines | Partly implemented | `BadMango.Emulator.Bus` / `MainBus.FastPath` | contrib | The page table is an array. The hot path indexes `byte[]` and skips `BusAccess` on a fast hit. `CONTRIBUTING.md` does not yet say that. |
| 12.3 Testing Strategy | Implemented | `tests/BadMango.Emulator.Bus.Tests` | contrib | NUnit projects under `tests/` cover bus, CPU, devices, and UI. `CONTRIBUTING.md` "Testing Guidelines" is the living copy. |

### Appendix A — Quick reference

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| A.1 Key Types Summary | Partly implemented | `BadMango.Emulator.Bus` / `PageEntry` | split | The summary lists types that exist and types that drifted (`FaultKind`, `DevicePageId`, two `PrivilegeLevel` enums). Dissolve it into the layer that owns each type. |
| A.2 Memory Map Quick Reference | Partly implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions` | machine | The Pocket2e 64KB/128KB map is what the builder configures. The PocketGS 16MB and PocketME 4GB rows are aspirational and move to the CPU companions. |
| A.3 Soft Switch Summary | Partly implemented | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | The ranges below are the summary. Several rows have no device. |
| A.3 / Keyboard ($C000–$C01F) | Partly implemented | `BadMango.Emulator.Devices` / `AppleIIeKeyboard` | devices | The keyboard owns $C000 and $C010. $C019–$C01F are status reads on other devices. |
| A.3 / Cassette ($C020–$C02F) | Aspirational | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | No cassette device. An unregistered read returns `0xFF`. |
| A.3 / Speaker ($C030–$C03F) | Partly implemented | `BadMango.Emulator.Devices` / `SpeakerController` | devices | The toggle is registered at offset `0x30` only. $C031–$C03F are not mirrored. |
| A.3 / Utility Strobes ($C040–$C04F) | Aspirational | `BadMango.Emulator.Bus` / `IOPageDispatcher` | devices | No handler. Unregistered reads return `0xFF`. |
| A.3 / Graphics Mode Switches | Implemented | `BadMango.Emulator.Devices` / `VideoDevice` | devices | $C050–$C057 are TXTCLR through HIRES, read and write. |
| A.3 / Annunciator Outputs | Implemented | `BadMango.Emulator.Devices` / `VideoDevice` | devices | $C058–$C05F are AN0–AN3. $C05E is also the double-hi-res switch on the same device. |
| A.3 / Pushbutton / Joystick Inputs | Partly implemented | `BadMango.Emulator.Devices` / `GameIOController` | devices | $C060–$C063 are buttons and $C064–$C067 are paddles. $C068–$C06F has no handler. |
| A.3 / Paddle Trigger | Implemented | `BadMango.Emulator.Devices` / `GameIOController` | devices | $C070–$C07F all call `TriggerPaddles`. Timers use `CyclesPerPaddleUnit` of 11. |
| A.3 / Language Card / Bank Switching | Implemented | `BadMango.Emulator.Devices` / `LanguageCardDevice` | devices | $C080–$C08F reads select bank, read source, and write-enable (R×2). Writes do not change the switch. |
| A.3 / Slot I/O Space | Implemented | `BadMango.Emulator.Bus` / `SlotManager` | devices | $C090–$C0FF is per-slot handler installation. Empty slots read `0xFF`. |
| A.3 / Apple IIe Auxiliary Memory Switches (80-column/Double Hi-Res) | Implemented | `BadMango.Emulator.Devices` / `Extended80ColumnDevice` | devices | $C000–$C00D writes are on `Extended80ColumnDevice` (80STORE, RAMRD/RAMWRT, INTCXROM, ALTZP, SLOTC3ROM, 80COL). $C00E/$C00F are `CharacterDevice`. `AuxiliaryMemoryController` owns the main/aux bank. |
| A.3 / Apple IIe Status Reads | Partly implemented | `BadMango.Emulator.Devices` / `Extended80ColumnDevice` | devices | $C013–$C018 are on `Extended80ColumnDevice`. $C019–$C01D and $C01F are on `VideoDevice`. $C01E is `CharacterDevice`. `StatusFlag.RDLCBNK2` and `RDLCRAM` ($C011/$C012) have no dispatcher registration. |

### Appendix B — Building a Pocket2e machine

| Section | Status | Code | Moves to | Note |
| --- | --- | --- | --- | --- |
| B.1 Overview | Implemented | `BadMango.Emulator.Bus` / `MachineBuilder` | machine | A machine is a builder product: CPU factory, memory, devices, scheduler, signals. |
| B.2 Complete Build Example | Partly implemented | `BadMango.Emulator.Systems` / `Pocket2eMachineBuilderExtensions` | machine | The working construction path is the extension methods plus `profiles/pocket2e.json`. The long sample in the appendix is not the API `MachineBuilder` exposes today. |
| B.3 Machine Interface and Reset Sequence | Implemented | `BadMango.Emulator.Bus` / `Machine` | machine | `Machine.Reset` pulses `SignalLine.Reset`. Assert calls `Scheduler.FlushPending`, then devices reset. Power-on calls `Cpu.Reset`, which loads $FFFC. |
| B.4 Usage Example | Partly implemented | `BadMango.Emulator.Bus` / `Machine` | machine | `BootAsync` is the run entry. The sample's call shape should be re-copied from `Machine` when the page is written. |
| B.5 Identified Specification Gaps | Partly implemented | `BadMango.Emulator.Bus` / `MachineBuilder` | machine | Gaps 1–3 and 5–8 match the code. Gap 4 added `Scheduler.Reset`, which machine reset does not call. |
| Gap 1: Composite Page Dispatch | Implemented | `BadMango.Emulator.Bus` / `ICompositeTarget` | bus | `ResolveTarget` is how `MainBus` enters a composite page. |
| Gap 2: Dynamic Page Remapping | Implemented | `BadMango.Emulator.Bus` / `IMemoryBus` | bus | `RemapPage` and `RemapPageRange` are on the interface. Language card and aux memory call them. |
| Gap 3: CPU Interface for PC Access | Implemented | `BadMango.Emulator.Emulation` / `Cpu65C02` | cpu | The "not needed" resolution holds. `Reset` reads $FFFC itself. `ICpu` also has `GetPC` and `SetPC`. |
| Gap 4: Scheduler Reset | Partly implemented | `BadMango.Emulator.Bus` / `Scheduler` | sched | `Scheduler.Reset` clears the queue and zeroes `Now`. Machine reset does not call it. RESET assert calls `Scheduler.FlushPending`. |
| Gap 5: Machine-Level Abstraction | Implemented | `BadMango.Emulator.Bus` / `IMachine` | machine | `Machine` owns bus, CPU, scheduler, signals, and devices. |
| Gap 6: Device Initialization Order | Implemented | `BadMango.Emulator.Bus` / `MachineBuilder` | machine | `Build` maps ROM, then calls `InitializeDevices`. `LanguageCardDevice.Initialize` depends on that order to snapshot $D000–$FFFF. |
| Gap 7: CPU Construction Parameters | Implemented | `BadMango.Emulator.Bus` / `MachineBuilder` | cpu | `WithCpu` and `WithCpuFactory`. Pocket2e passes `IEventContext` into `new Cpu65C02(context)`. |
| Gap 8: Stop/Halt Coordination | Implemented | `BadMango.Emulator.Core` / `ICpu` | cpu | `RequestStop`, `HaltState` (`Wai`, `Stp`), and `Machine` state. `Cpu65C02` honors both. |

---

## 2. Proposed structure

Root index, required by #501, plus one page per design layer. CPU targets that are not the 65C02 get a companion so the 65C02 page stays the as-built contract. Pages live under `specs/architecture/`. Each rewrite starts with the summary, the covers / does-not-cover lines, and the code paths #501 asks for. This proposal does not add those files.

`Moves to` in the audit is the file stem below.

| File | Purpose | Audited sections |
| --- | --- | --- |
| `ARCHITECTURE.md` (repo root) | Entry point. Short system picture, layer list, and a "where to look" table (add an opcode, add a device, bus faults, soft switches). Points at the pages below. | None. It links them. Replaces the v1.0 "single source of truth" claim. |
| `specs/architecture/cpu.md` | 65C02 as built, plus the shared register file and mode helpers every chip uses. | 1.2, 2.1, 2.2 and its eight subsections, 4.10, Gap 3, Gap 7, Gap 8. |
| `specs/architecture/cpu-65816.md` | What a 65816 core must add. States that `Cpu65816` throws. | 10.2. Aspirational rows from 1.1 and A.2's PocketGS map. |
| `specs/architecture/cpu-65832.md` | Pointer into the privileged spec for privilege, paging, traps, and boot. Does not paste that file. | 2.2 privilege, 3.2, 3.4, 10.3, 10.4. Aspirational PocketME row of A.2. |
| `specs/architecture/bus-mmu.md` | Addressing, the host page table, targets, and access policy. The fast path, fault registers, `FaultKind`, and `ABORT` stay normative in the fast-path spec. This page links that spec and does not restate a second fast path. | 3.1, 3.5, 4.1–4.9, Gap 1, Gap 2. |
| `specs/architecture/signals.md` | Signal fabric and the 65C02 IRQ/NMI/RESET/`ABORT` reactions. | 5.1–5.1.3. |
| `specs/architecture/scheduler.md` | Cycle clock, the event queue, and who advances it. | 6.1–6.3, Gap 4. |
| `specs/architecture/devices.md` | I/O page, soft switches, slots, peripherals, and the composite page. | 1.3–1.3.4, 7.1–7.6, 7.7.4, A.3 and its twelve ranges. |
| `specs/architecture/rom-traps.md` | Expansion ROM banking and the trap registry, including the empty-registry fact. | 7.7–7.7.3, 8.3–8.3.12. |
| `specs/architecture/compat.md` | 65832 guest personalities and the IIc. Both are unbuilt. Short on purpose. | 7.8–7.8.4, 8.1, 8.2. |
| `specs/architecture/observability.md` | Debugger, instruction trace, fault ring, device ids. Records that the §9.1 bus trace ring was not built. | 3.3, 9.1–9.3. |
| `specs/architecture/machine.md` | Profiles, `MachineBuilder`, Pocket2e assembly, reset, and boot. | 1.1, 7.5, 10.1, A.2 (Pocket2e), B.1–B.5, Gap 5, Gap 6. |
| `specs/architecture/host-ui.md` | Host surfaces. The v1.0 specs have no host chapter, so this page points at `specs/Emulator UI Specification.md`, `BadMango.Emulator.UI`, and `BadMango.Emulator.Debug.UI`. | None from v1.0. |

### Bus layer source of truth

| Document | Role after the split |
| --- | --- |
| `specs/Bus Fast Path and Fault Registers Specification.md` | Normative for the fast path, fault registers, `FaultKind`, `ABORT`, and the §17 corrections. `bus-mmu.md` links it. Do not fork those rules into the layer page. The layer page waits until epic #499 closes, so it is not written against a moving contract. |
| `reference/Main bus architecture.md` | Stays. `bus-mmu.md` labels it historical. The three-plane idea (data, control, observability) and the signal/scheduler split are the parts that survived. The `Func<>` and function-pointer paragraphs are superseded by fast-path spec §6–§7. |
| `reference/Privileged architecture spec v0.1 for 65832.md` | Stays the 65832 draft. The file is a decision log: v0.1 through v0.6 deltas are concatenated, and a later delta wins where they disagree. `cpu-65832.md` cites it. It is not copied into `specs/architecture/`. |

`A.1` is not a page. Each type moves to the page of its project: bus types to `bus-mmu.md`, CPU types to `cpu.md`, signal types to `signals.md`, device types to `devices.md`.

When the layer pages exist, Part 1–3 and the appendix shrink to a pointer index. That deletion is a later #501 change, not this proposal.

---

## 3. Homes for non-architecture content

| Content | Home | What happens |
| --- | --- | --- |
| §12.1 Naming, §12.3 Testing | `CONTRIBUTING.md` | Already covered under "Coding Guidelines" and "Testing Guidelines". The layer rewrite deletes the copies from the architecture set and does not add a second standards file. |
| §12.2 Performance | `CONTRIBUTING.md` | Add the rules the code actually follows: the data plane does not allocate, the page table is an array, and a fast-path hit does not build `BusAccess`. |
| `specs/coding/` | Stays | PocketASM source format and the assembler/linker build. That is toolchain documentation, not the C# architecture. |
| Part XI Phases 1–6 | GitHub project #3 | https://github.com/orgs/Bad-Mango-Solutions/projects/3 is the living roadmap. Phases 1–4 are largely done and Phases 5–6 are not. The unchecked boxes in the spec are stale. |
| `specs/Unified Development Schedule.md`, `specs/Implementation Roadmap - Bus Architecture.md` | Project #3 | Historical schedules (dated 2025-12-26, with week estimates). Leave the files until a later docs pass. Do not copy them into `ARCHITECTURE.md`. A layer page may link the project for status. |

---

## 4. Wiki plan

`wiki/Architecture-Overview.md` is in this repo. Its banner already says the Applesoft projects moved to `back-pocket-basic-legacy`, and that `CONTRIBUTING.md` lists the emulator projects. The body still documents the interpreter (lexer, parser, AST, visitor) and a section 7 emulator sketch that names `IMemory` and treats `Cpu65816` as more than a class that throws.

Retire it as an architecture source. Do not delete the path.

| Wiki section | Action |
| --- | --- |
| Banner and "Project Structure" | Fold into the replacement pointer. Project list stays `CONTRIBUTING.md`. |
| Core components 1–6, execution pipeline, patterns, DI, the BASIC testing notes | Drop. They describe `back-pocket-basic-legacy`. |
| Section 7 "Advanced Emulator Framework" | Drop. It is the only section about this repo, and `ICpu` / `IMemory` in that section do not match `BadMango.Emulator.Core.Interfaces.Cpu.ICpu`. |
| Replacement body | After `ARCHITECTURE.md` exists: about fifteen lines. Architecture → `ARCHITECTURE.md`. Project list and C# standards → `CONTRIBUTING.md`. BASIC → the legacy repo. |

Do that replacement in the same change as `ARCHITECTURE.md`, so the wiki never points at a missing index and never keeps the BASIC body once the index is live.

`AGENTS.md` and `.github/copilot-instructions.md` should point at `ARCHITECTURE.md` first, as #501 requires. That edit belongs to the index issue, not to this proposal.

---

## 5. Proposed follow-up issues

One issue per page. Scrum size: **S = 2**, **M = 3**. Nothing here is larger than M. The devices layer is three pages because one page would be larger than M. The bus/MMU page waits until epic **#499** closes. File these after this proposal is accepted. Do not start the bus page while #499 is open.

| Issue | Page | Points | Waits on | Scope |
| --- | --- | --- | --- | --- |
| Architecture index and wiki pointer | `ARCHITECTURE.md`, rewrite `wiki/Architecture-Overview.md`, point `AGENTS.md` and `.github/copilot-instructions.md` at the index | S (2) | This proposal | Index plus the wiki retirement in section 4. No layer prose. |
| CPU core, 65C02 as built | `specs/architecture/cpu.md` | M (3) | Index | Sections in the structure table. Document `Cpu65C02`, `Registers`, and `AbortReaction`. Mark 65816/65832 as companions. |
| 65816 companion | `specs/architecture/cpu-65816.md` | S (2) | CPU core page | §10.2 and the PocketGS row. State that `Cpu65816.Step` throws. Do not design the Mega II here. |
| 65832 companion | `specs/architecture/cpu-65832.md` | M (3) | CPU core page | Link `reference/Privileged architecture spec v0.1 for 65832.md`. Record which types exist (`SystemRegisters`, `PrivilegeLevel`) and that `Cpu65832` throws. Do not paste the privileged spec. |
| Bus and MMU | `specs/architecture/bus-mmu.md` | M (3) | **#499 closed** | Narrative of §3.1 and §4, plus Gap 1 and Gap 2. Link the fast-path spec for the hot path and faults. Label `reference/Main bus architecture.md` historical. |
| Signal bus and interrupts | `specs/architecture/signals.md` | M (3) | Index | §5. Record `Abort` in place of `Sync`, and that `RDY` and `DmaReq` have no callers. |
| Scheduler and timing | `specs/architecture/scheduler.md` | M (3) | Index | §6 and Gap 4. Include `VideoDevice` VBL as a client of `IScheduler`. |
| Devices and soft switches | `specs/architecture/devices.md` | M (3) | Index | §7.1–§7.6, §7.7.4, §1.3, and A.3, using the status column above so cassette, $C040, speaker mirrors, and $C011/$C012 stay marked incomplete. |
| Expansion ROM and traps | `specs/architecture/rom-traps.md` | M (3) | Devices page | §7.7 and §8.3. Lead with "the registry is empty in a built machine." |
| IIc and 65832 personalities | `specs/architecture/compat.md` | S (2) | Devices page | §7.8, §8.1, §8.2. Short. Status is aspirational. |
| Observability and debugging | `specs/architecture/observability.md` | M (3) | Bus page, so fault-register names are stable | §9 and §3.3. Document `BusFaultRing`, `InstructionTrace`, `WatchpointManager`, and the `DevicePageId` bit layout the code uses. |
| Machine assembly and profiles | `specs/architecture/machine.md` | M (3) | Devices page | §1.1, §7.5, §10.1, A.2 Pocket2e, Appendix B. Profiles under `profiles/`. |
| Host and UI | `specs/architecture/host-ui.md` | S (2) | Index | Pointer page to `specs/Emulator UI Specification.md` and the UI projects. No v1.0 section to move. |
| Coding standards | `CONTRIBUTING.md` only | S (2) | Index | Fold §12.2's hot-path rules into Coding Guidelines. Leave §12.1 and §12.3 as the existing sections. Do not create a new file. `specs/coding/` stays. |

Part XI is not an issue. Project #3 is the roadmap. The index page says so in one line.

After these pages exist, a separate #501 change reduces `specs/Architecture Spec v1.0 Part 1.md`, `Part 2.md`, `Part 3.md`, and `Appendix.md` to a pointer index. That change is out of scope for every issue in this table.
