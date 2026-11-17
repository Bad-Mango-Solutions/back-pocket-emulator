# Bus Fast Path and Fault Registers Specification

| Field | Value |
|---|---|
| Status | Normative |
| Owner | Josh |
| Tracking issue | #500 |
| Epic | #499 |
| Baseline | Bus behavior below is `main` at `66ad4e7`. #489 has since merged (`a6feca5`, on `main` at `2cb72e0`). Its numbered fault mask is current behavior in section 3, and A5 migrates it. |
| Related | `reference/Main bus architecture.md`, `reference/WDC 65816 Emulator Reference.md`, `specs/Architecture Spec v1.0 Part 1.md` through `Part 3.md`, #501 |

---

## 1. Summary

Every 65C02 byte access currently builds a `BusAccess`, calls `IMemoryBus.TryRead8` or `TryWrite8`, and comes back as a `BusResult<T>` whose `BusFault` was filled in from that access even when the access worked. PR #498 measured the load/store dispatch migration as speed-neutral and attributed the time to this bus path. Epic #499 puts a direct backing-array index on plain RAM and ROM pages, and moves fault reporting onto host-visible registers that drive an `ABORT` line.

This document is the contract for the implementation sub-issues in Appendix A. It does not change code, and it does not file those issues.

## 2. Normative language

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY are used as in RFC 2119.

"Current behavior" means what `main` does at the baseline above. Those statements are descriptive. Requirements use MUST or SHOULD.

## 3. Current behavior

`CpuBase` (`src/BadMango.Emulator.Emulation/Cpu/CpuBase.cs`) holds an `IMemoryBus` taken from `IEventContext.Bus`. `Read8`, `Write8`, `Peek8`, `Poke8`, `Read16`, `Write16`, `Read32`, and `Write32` each allocate a `BusAccess` through `CreateReadAccess`, `CreateWriteAccess`, `CreateDebugReadAccess`, or `CreateDebugWriteAccess`, then call the matching `Try*` method. `Cpu65C02.InstructionFetch8` does the same with `CreateInstructionFetchAccess` and `AccessIntent.InstructionFetch`.

Each of those factories calls the virtual `GetEmulationFlag()` (the 65C02 override returns `true`) and reads `context.Now` (an interface call) plus `Registers.TCU` for `BusAccess.Cycle`. The struct is `BusAccess` in `src/BadMango.Emulator.Bus/BusAccess.cs`: address, value, width, `BusAccessMode`, emulation flag, `AccessIntent`, source id, cycle, `AccessFlags`, and `PrivilegeLevel` (default `Ring0`). CPU factories pass `BusAccessMode.Decomposed` and `AccessFlags.None`. Debug factories add `AccessFlags.NoSideEffects`.

`MainBus.TryRead8` (`src/BadMango.Emulator.Bus/MainBus.cs`) then:

1. Bounds-checks the 4 KB page index (`PageShift` is 12, `PageMask` is `0xFFF`).
2. Rejects a null `PageEntry.Target` as `FaultKind.Unmapped`.
3. Rejects a missing `PagePerms.Read` unless the access is a debug access.
4. Rejects `FaultKind.Nx` only when `Intent` is `InstructionFetch`, `Mode` is `BusAccessMode.Atomic`, and `PagePerms.Execute` is clear. CPU fetches set `Decomposed`, so this branch does not run for `Cpu65C02`.
5. If `Target` implements `ICompositeTarget`, calls `ResolveTarget`. A null sub-target records a silent unmapped fault when `FaultRecordingEnabled` is set, and returns `BusResult<byte>.Success(0xFF, ...)`.
6. Otherwise calls `IBusTarget.Read8`. `RamTarget.Read8` and `RomTarget.Read8` index `Memory<byte>.Span` or `ReadOnlyMemory<byte>.Span`.

The success return is `BusResult<byte>.Success(value, access, deviceId, regionTag, cycles: 1)`, which calls `BusFault.Success(in BusAccess, ...)`. That builds a 9-field `BusFault` (`Kind`, `Address`, `WidthBits`, `Intent`, `Mode`, `SourceId`, `DeviceId`, `RegionTag`, `Cycle`) with `Kind == FaultKind.None`. `BusFault` is a `readonly record struct` in `src/BadMango.Emulator.Bus/BusFault.cs`. The same pattern is on `TryWrite8` and on the wide `Try*` methods in `MainBus.WideAccess.cs`.

`IMemoryBus` also has non-`Try` `Read8` / `Write8` / `Read16` / `Write16` / `Read32` / `Write32`. The remarks call those the hot path and `Try*` the checked path. `CpuBase` does not call the non-`Try` methods. The non-`Try` byte methods still branch on `ICompositeTarget` and still call `IBusTarget.Read8` through the interface. `reference/Main bus architecture.md` ("The page table is the spine", "Read and write entry points you'll actually use") called for one page lookup and a direct call, with tracing on a separate plane ("Observability that won't ruin performance": one flag check when tracing is off). The same note later suggests `Func<>` delegates on `PageEntry` and, further down, replacing `IBusTarget` calls with function pointers. Those two mechanisms are not the implementation (section 6).

`PageEntry` (`src/BadMango.Emulator.Bus/PageEntry.cs`) is a `readonly record struct`: `DeviceId`, `RegionTag`, `PagePerms`, `TargetCaps`, `IBusTarget Target`, `PhysicalBase`, three `PrivilegeLevel` minimums (default `Bus.PrivilegeLevel.Ring0`), and `IsSealed`. It does not carry a backing array. `MainBus.TryRead8` does not read the privilege fields.

`PhysicalMemory` owns a `byte[]` and hands out `Memory<byte>` slices. `RamTarget` and `RomTarget` are sealed views of those slices. `RomTarget` has two constructors: `ReadOnlyMemory<byte>` (`isWritable` false) and `Memory<byte>` (debug writes allowed). Normal `RomTarget.Write8` ignores a non-debug write.

Wide `TryRead16` / `TryRead32` decompose when `CrossesPageBoundary` is true (the 4 KB bus page, not the 6502's 256-byte page), when `AccessFlags.Decompose` is set, or when `Mode` is `Decomposed`. CPU accesses are `Decomposed`, so a CPU `Read16` is two `TryRead8` calls. `DecomposeTryRead16` returns on the first failed byte and does not start the second. `DecomposeTryWrite16` keeps a successful first write if the second byte faults. `CpuBase` does not read `BusResult.Cycles`. Instruction cycles live in `Registers.TCU`: `Cpu65C02.FetchByte` adds 1, and the handlers in `Instructions.*.cs` and the 256-byte page-cross tests in `AddressingModes` and `Instructions.Branch` add the rest. `BusResult.Cycles` is summed inside `MainBus.WideAccess` and then discarded by the CPU.

On an unmapped `Read8` or instruction fetch, `CpuBase` / `Cpu65C02` set `HaltReason = HaltState.Stp` and return `0xFF` (`0xFFFF` / `0xFFFFFFFF` for the wide helpers). An unmapped `Write8` does not set `Stp`. Any other read fault returns the floating-bus value and does not set `Stp`.

Traps are not a page property. `Cpu65C02.Step` calls `ITrapRegistry.TryExecute` before `FetchByte`, and skips that call when the registry is `NullTrapRegistry` or an empty concrete `TrapRegistry`. Watchpoints are not a page property either. `WatchpointManager` is an `IDebugStepListener` that looks at `DebugStepEventArgs.EffectiveAddress` after the instruction. Its remarks say opcode fetch, stack traffic, and interrupt vector reads are not watched.

Bank switching rewrites `pageTable` in place. `LanguageCardDevice.ApplyState` activates or deactivates the high and low layers and calls `SelectSwapVariant`. `$C081` / `$C089` split mode installs `LanguageCardSplitTarget`, an `ICompositeTarget` that reads ROM and writes language-card RAM. Full RAM (`$C083` family) and full ROM (`$C082` family) install `RamTarget` or the base ROM. `Extended80ColumnDevice` and `AuxiliaryMemoryController` activate layers for 80STORE, aux RAM, and aux zero page. `SelectSwapVariant`, `RemapPage`, `RemapPageRange`, `ActivateLayer`, and `DeactivateLayer` replace `PageEntry` values. The comments say the update is atomic. `PageEntry` is a large struct, and the store is an ordinary array element write. Soft-switch handlers run on the CPU thread inside the write that touched `$C0xx`, so the CPU is not reading that entry concurrently with the store.

`FaultKind` (`src/BadMango.Emulator.Bus/FaultKind.cs`) is a `byte` enum, not `[Flags]`: `None = 0`, `Unmapped = 1`, `Permission = 2`, `Nx = 3`, `Misaligned = 4`, `DeviceFault = 5`. #489 has merged. `MachineDebugSession.ReadMemory` stores `(byte)read.Fault.Kind` in `MemoryBlock.Faults` and continues. `00` means the data byte is valid. `MemoryBlock`'s remarks, `DebugOutputRenderer.FaultText` (the arms are the raw bytes 1 through 5), and `emudbg.md` (the `mem` / `peek` `--json` paragraph) document the same numbering: 1 Unmapped, 2 Permission, 3 Nx, 4 Misaligned, 5 DeviceFault. `SignalLine` (`src/BadMango.Emulator.Core/Signaling/SignalLine.cs`) is `IRQ`, `NMI`, `Reset`, `RDY`, `DmaReq`. There is no abort line. `MainBus` does not take an `ISignalBus`.

There is no MCP server in the tree. `emudbg-agent-plan.md` and `PRD-emudbg-agent.md` name MCP over stdio as later work (#333, #337) on top of `IDebugSession`. `MemCommand`, `PeekCommand`, `PokeCommand`, and `WriteCommand` call `Try*` and format `BusFault` with a `switch` on `FaultKind`. `ReadMemory` does not throw on a fault.

`benchmarks/BadMango.Emulator.Benchmarks` is on `main`. `WorkloadMhzDispatchBenchmark` steps `MixWorkload`, `RamWorkload`, and `FlagsTransferWorkload` on `BareCpuFixture` (a `Cpu65C02` and a flat 64 KB RAM bus, no devices). The project has no `Bus/` benchmark and no machine fixture. PR #496 specifies that layout and a 5% compare threshold. It is not merged. PR #498, also not merged, reports the mix workload at 96.42 effective MHz (46.88 ns/instruction, 113 cycles / 25 instructions) and 0 bytes allocated per instruction, on one KVM guest, and says the mix did not move because the time is in the bus.

## 4. Goals

| # | Goal | Acceptance |
|---|---|---|
| G1 | A plain RAM or ROM byte is an array index from the live page entry. | A `BareCpuFixture` mix run does not call `TryRead8` / `TryWrite8` for those bytes. Devices, composite pages, and permission-restricted pages still do. |
| G2 | Soft-switch remaps are visible to the next byte. | Language-card, aux, and 80STORE tests read the bank the switch just selected, with no stale `PageEntry` copy. |
| G3 | Faults are registers, not a success payload. | A successful access leaves fault status unchanged and does not construct a `BusFault`. A fault latches status and address, first fault wins, and constructs one `BusFault` only because status is nonzero. |
| G4 | `FaultKind` is a flags value whose low byte is what a 65C02 fault uses, and whose low-byte encoding stays readable by the PR #489 mask. | Section 10. The mask's `00` still means valid. |
| G5 | A nonzero status asserts `SignalLine.Abort`. | 65C02 reaction defaults to `NoOp`. The debug session can switch it to `HostBreak`. 65816/65832 reaction is the abort behavior in `reference/WDC 65816 Emulator Reference.md`. |
| G6 | Wrong-bank diagnosis does not tax the fast path. | Trace off: the fast path does not load the flag. Trace on, no range: remap events and slow-path accesses are recorded, and fast RAM indexes are not. A debugger range forces only the covering pages onto the slow path. |
| G7 | Callers of `Try*` keep compiling, and the existing CPU tests stay green, across the stages in section 14. | No stage deletes `TryRead8` or makes a success `Kind` anything other than `None`. |
| G8 | The mix workload gets faster and still allocates nothing. | Section 15. |

## 5. Non-goals

- A 65816 or 65832 CPU. Section 11 states the abort behavior that CPU MUST implement. This epic adds the line and the registers, not that CPU.
- IIgs shadowing. `RegionTag.Shadow` exists. When shadowing is built, it MUST go through the same page-entry rebuild as a language-card remap. This epic does not add shadow registers.
- Enforcing `PageEntry.MinReadPrivilege` / `MinWritePrivilege` / `MinExecutePrivilege`. The slow path does not enforce them today. The fast path MUST NOT start.
- Changing 65C02 unmapped reads from `HaltState.Stp` plus `0xFF`, or making unmapped writes set `Stp`.
- Making `TryRead8` treat a composite hole (null `ResolveTarget`) as `Failed`. It returns success and `0xFF` today. That stays a floating bus, not a latched fault.
- Putting traps or watchpoints on the page table. They stay in `Cpu65C02.Step` and `WatchpointManager`.
- `delegate*`, `unsafe`, `AllowUnsafeBlocks`, or a `Func<>` / function-pointer page entry. The architecture note's suggestion is rejected.
- Rewriting `specs/Architecture Spec v1.0`. Section 16 says how #501 absorbs this document.
- Filing the Appendix A issues. This document proposes them.

## 6. Project rules

Implementations of this spec follow the repository rules:

- .NET 10.
- Locks are `System.Threading.Lock`. The fast path MUST NOT take `MainBus`'s `swapGroupLock` or any other lock. That lock already surrounds `SelectSwapVariant`.
- No static mutable state. Fast-path arrays are instance fields of the memory the target already owns.
- No empty catch. A failed fast-path eligibility check is a slow-path call, not a swallowed exception.
- No `unsafe`, no `delegate*`, no `AllowUnsafeBlocks`. `MemoryMarshal.TryGetArray` is the allowed way to recover a `byte[]` and an offset from `Memory<byte>` or `ReadOnlyMemory<byte>`.

`reference/Main bus architecture.md` says the page entry may hold `Func<uint, byte>` and that `IBusTarget` calls may later become function pointers. Those are not permitted here. The fast path is a `byte[]` index.

## 7. Page-entry fast path

### 7.1 Representation

`PageEntry` gains three fields, with defaults so existing positional construction still compiles:

| Field | Meaning |
|---|---|
| `byte[]? FastBacking` | The array behind this page, or null when the page is not eligible. |
| `int FastBase` | Index in `FastBacking` of target-local physical address 0. |
| `FastPathOps FastOps` | Which operations may index the array. |

`FastPathOps` is a `[Flags]` byte: `None = 0`, `Read = 1 << 0`, `Write = 1 << 1`, `Fetch = 1 << 2`.

For an eligible page, the byte at CPU address `A` is

```text
FastBacking[FastBase + (int)PhysicalBase + (int)(A & 0xFFF)]
```

`PhysicalBase` stays the target-local address of the page, which is what `MainBus.Read8` adds today. `FastBase` is the array index of that target's offset 0, so a `Memory<byte>` slice that does not start at index 0 still works. `RamTarget` and `RomTarget` MUST expose the slice to the bus through a method on those types. The bus MUST NOT reflect into private fields.

Map time MUST reject a fast window that does not fit. The closed interval of target offsets `[PhysicalBase, PhysicalBase + 0xFFF]` MUST lie inside the slice. A short target, or a `PhysicalBase` that would run off the end, keeps `FastBacking` null and uses the slow path. The CPU index MUST NOT do a per-byte length check.

### 7.2 Eligibility

A page is a candidate only when all of the following hold:

- `Target` is a `RamTarget`, or a `RomTarget`, and is not an `ICompositeTarget`. `LanguageCardSplitTarget` is an `ICompositeTarget` and stays slow in `$C081` split mode even though the bytes behind it are RAM and ROM.
- `MemoryMarshal.TryGetArray` succeeds for that target's slice. `PhysicalMemory` is a `byte[]`, so the machine's ordinary RAM and ROM succeed. A slice that is not array-backed stays slow.
- `TargetCaps` does not include `HasSideEffects` or `TimingSensitive`.
- `MinReadPrivilege`, `MinWritePrivilege`, and `MinExecutePrivilege` are all `BadMango.Emulator.Bus.PrivilegeLevel.Ring0`, the default. Any other value keeps the page slow so a later privilege check has a place to live. The fast path does not perform that check.
- `RegionTag` is not an input. A `RamTarget` tagged `Shadow` or `Io` is still eligible when the other rules pass. A composite I/O page is ineligible because of its target type, not because of `RegionTag.Io`.

`FastOps` is then:

- `Read` when `PagePerms.Read` is set.
- `Write` when `PagePerms.Write` is set and the target is a `RamTarget`. A `RomTarget` MUST NOT set `Write`. Normal ROM writes are a slow-path permission fault when `Write` is missing, and a slow-path ignored write when the page somehow has `Write` but the target ignores it. Debug ROM pokes stay on `RomTarget.Write8`.
- `Fetch` when `Read` is set and, for a page the native CPU may execute, `PagePerms.Execute` is set. The 65C02 path in section 8 treats fetch as a read. It MUST NOT consult `Fetch` and MUST NOT raise `Nx`. That matches current behavior: NX is ignored in compat mode (`PagePerms` remarks), and the `TryRead8` NX branch requires `BusAccessMode.Atomic`, which the CPU never sets.

Device pages, composite pages (the `$C000` I/O page, slot ROM, `LanguageCardSplitTarget`), and pages missing the permission bit for the operation take the slow path. A read-only language-card RAM page (`$C080`, `ReadExecute` without `Write`) MAY set `Read` and MUST NOT set `Write`.

### 7.3 Rebuild

Every bus operation that stores a `PageEntry` MUST recompute `FastBacking`, `FastBase`, and `FastOps` for each stored entry before it returns. When a mapping-trace range is active (section 13.1), the published `FastOps` is the natural value from section 7.2 with `None` forced on every page the range covers. The rebuild uses that same function, so a remap cannot put a covered page back on the fast path. The list is `MapPage`, `MapPageRange`, `MapRegion`, `MapPageAt`, `SetPageEntry`, both `RemapPage` overloads, `RemapPageRange`, `ActivateLayer`, `DeactivateLayer`, `SetLayerPermissions`, and `SelectSwapVariant`. `Clear` zeroes target storage and MUST NOT change fast-path fields.

`LanguageCardDevice.ApplyState`, `Extended80ColumnDevice`'s layer updates (80STORE, PAGE2, HIRES, RAMRD, RAMWRT, ALTZP), and `AuxiliaryMemoryController.ActivateLayer` are covered because they call those bus methods. No device maintains its own copy of the backing pointer.

The CPU MUST read the live `pageTable` slot on every byte. It MUST NOT reuse a copied `PageEntry` (the type is a record struct; a copy freezes `FastBase` and `PhysicalBase`). It MUST NOT cache `FastBacking` across a slow-path access, because a `$C0xx` write inside that access may have rebuilt the entry. Indexing the live slot on every byte satisfies this. A generation counter MAY exist for the mapping trace. The fast path MUST NOT read it.

There is no extra memory barrier and no lock. Soft-switch writes run on the CPU thread, inside the slow-path write, before the next byte. A host thread that remaps while the CPU runs is already undefined on `main`. This spec does not define it.

## 8. CPU fast and slow paths

### 8.1 Who decides

`CpuBase` fast-paths only when `IEventContext.Bus` is a `MainBus` (the concrete page table). Any other `IMemoryBus` uses today's `Try*` calls. The fast path is a method on `CpuBase` used by `Read8`, `Write8`, and by `Cpu65C02.InstructionFetch8`. It takes the address and the intent (`DataRead`, `DataWrite`, or `InstructionFetch`).

`Peek8` and `Poke8` are debug accesses (`AccessIntent.DebugRead` / `DebugWrite`, `NoSideEffects`). A debug read of a page with `FastOps.Read` MAY index the array, because that page has no side effects. A debug write MAY index the array only when `FastOps.Write` is set (RAM). A debug write to ROM, and every debug access to a page that is not eligible, MUST use `TryRead8` / `TryWrite8` so `RomTarget`'s debug-write rule and device `NoSideEffects` behavior stay where they are.

Traps stay in front of `FetchByte`. Watchpoints stay on the step listener. Neither one clears `FastOps`. A trap handler's later `Read8` / `Write8` uses the same fast or slow choice as any other access.

### 8.2 Byte path

For a data read or a 65C02 instruction fetch:

1. If the page index is in range and `FastOps` contains `Read`, return the indexed byte. Do not build a `BusAccess`. Do not call `GetEmulationFlag`. Do not read `IEventContext.Now`. Do not construct a `BusFault`. Do not change `HaltReason`.
2. Otherwise call `TryRead8` as today.

For a data write:

1. If the page index is in range and `FastOps` contains `Write`, store the byte and return.
2. Otherwise call `TryWrite8` as today.

The slow path's observable results MUST stay:

| Result of `Try*` | CPU behavior that stays |
|---|---|
| `Read8` or fetch, `FaultKind.Unmapped` | Return `0xFF`. Set `HaltReason = HaltState.Stp`. |
| `Read8` or fetch, any other fault | Return `0xFF`. Do not set `Stp`. |
| `Write8`, `Unmapped` | Return. Do not set `Stp`. |
| `Write8`, any other fault | Return. The write did not happen. |
| Composite hole, success `0xFF` | Return `0xFF`. Do not latch a fault. Do not set `Stp`. |

A native CPU (emulation flag clear) MUST NOT take the fetch fast path unless `FastOps.Fetch` is set. A fetch that has `Read` but not `Fetch` uses the slow path, which is where `Nx` can be raised once that CPU exists and once it issues an access the NX branch can see. The 65C02 MUST NOT gain a new `Nx` halt. Fixing the dead `Atomic`-only NX check is not part of this epic (section 17).

### 8.3 Multi-byte accesses

`Read16`, `Write16`, `Read32`, and `Write32` MUST produce the same bytes and the same faults as today's decomposed `Try*` calls.

- The bus page is 4 KB. `CrossesPageBoundary` in `MainBus.WideAccess` is that test: `((address & 0xFFF) + (bytes - 1)) > 0xFFF`. An access that crosses a 4 KB boundary MUST be one byte operation per byte, each of which independently takes the fast or slow path. The two bytes of a same-page `Read16` MAY be two indexes into the same `FastBacking` with no `BusAccess`.
- The second byte's address is `address + 1` as a `uint` (`Addr` is `uint` in `GlobalUsings.cs`), matching `DecomposeTryRead16`. This spec does not add 16-bit wrap inside `Read16`. A `Read16` at `$FFFF` on a 16-bit bus still addresses `$10000` for the high byte, which is out of range and unmapped. Zero-page pointer wrap stays in `AddressingModes`, which already loads the high byte with `(pointerAddr & 0xFF00) | ((pointerAddr + 1) & 0xFF)` through `Read8`.
- A 65816 bank is 64 KB, in the data bank or the program bank. `Cpu65C02` does not have those registers. When a 65816 CPU is written, a multi-byte access that would leave its bank MUST split into byte accesses at the bank boundary. `FaultKind.BankCrossing` (section 10) is detail on a fault that happens on the far side of that boundary. Crossing a bank successfully MUST NOT latch fault status.
- First fault stops the rest of that multi-byte access. A `Read16` whose first byte succeeds and whose second faults returns the fault of the second byte and does not invent a value from the first. A `Write16` whose first byte was stored and whose second faults leaves the first byte stored. That is the current `DecomposeTryWrite16` torn write. The latched fault address is the first faulting byte, not the base of the wide access, and a later byte of the same access MUST NOT replace it (section 9).

The non-`Try` wide methods on `MainBus` MUST use the same split. A same-page fast index MUST NOT call `RamTarget.Read16` through the interface.

### 8.4 Cycles

Cycle truth for an instruction is `Registers.TCU`. `FetchByte` adds 1. Opcode handlers add their counts. A 256-byte page cross adds 1 in `AddressingModes` and in the branch handlers by comparing `(addr & 0xFF00)`. That is a different page from the bus's 4 KB page, and it MUST keep running. The fast path MUST NOT add to `TCU`, and MUST NOT start adding `BusResult.Cycles` into `TCU`. `Cpu65C02BusCycleAndPcWrapTests` and the load/store page-cross assertions are the regression net.

`BusAccess.Cycle` (`context.Now + Registers.TCU`) is a trace stamp. The fast path does not build a `BusAccess`, so it has no stamp. A slow-path fault stamps the latch with that cycle, as `TryRead8` does today.

## 9. Fault registers

### 9.1 Where they live

The host-visible registers live on `MainBus`, which is the MMU. They are not 6502 registers. No 65C02, 65816, or 65832 instruction reads or writes them. `IMemoryBus` exposes them:

| Register | Type | Model |
|---|---|---|
| Fault status | `FaultKind` | x86 error code, ARM ESR. Zero means no latched fault. |
| Fault address | `Addr` | x86 CR2, ARM FAR. The byte that faulted. |

`ICpu` / the debug register snapshot MAY forward the same two values. The snapshot MUST NOT grow a 6502 opcode to read them.

The bus MAY keep a private latch beside the two registers (width, intent, mode, source id, device id, region tag, cycle) so it can build one `BusFault` without a second walk of the access. That latch is not a third architectural register. Program counter is not known to the bus. `BusFault` gains a `Pc` field, set by the CPU when the CPU asks for the record. A fault observed by a non-CPU `Try*` caller stores `Pc = 0`.

### 9.2 When they latch

A non-debug access that `TryRead8` / `TryWrite8` returns as `Failed` latches, subject to first-fault-wins. That is `Unmapped` (null target or page index out of range), `Permission`, `Nx`, `Misaligned`, and `DeviceFault`.

These do not latch:

- A successful access, including a composite hole that returns `0xFF`.
- A fast-path index.
- A debug access (`IsDebugAccess`), including `MemCommand`, `PeekCommand`, `MachineDebugSession.ReadMemory`, and `Peek8` / `Poke8`.
- A silent ring record that the non-`Try` `Read8` writes for a floating-bus hole while `FaultRecordingEnabled` is set. That remains a debugger trace, not a status update. The CPU does not use the non-`Try` methods for the access it is about to retire.

First-fault-wins: if status is already nonzero, a new fault MUST NOT change status, address, or the private latch. The abort line is already asserted. The host clears status to see the next fault.

A successful access MUST NOT write status. Zero stays zero. A latched fault stays latched. The phrase "success leaves status at zero" in #499 means the success path does not store a nonzero status and does not build a `BusFault`. It does not mean every success clears a previous fault. Last-fault-wins is rejected: the address that raised abort would be replaced by a later, unrelated byte.

### 9.3 How they clear

- The host writes status to `FaultKind.None` through `IMemoryBus` (and through the debug session, once section 14's session work lands). That write also clears the address register and the private latch, and deasserts `SignalLine.Abort` if this bus asserted it.
- Reset of the machine clears both registers and deasserts the line.
- No emulated instruction clears them.
- A fast-path access does not clear them.

### 9.4 Multi-byte accesses

The first `Failed` byte latches, and the remaining bytes of that `TryRead16` / `TryRead32` / `TryWrite16` / `TryWrite32` are not issued. This is already how `DecomposeTryRead16` and `DecomposeTryRead32` return. Writes that completed before the failing byte stay written. Status records the kind of that first byte only. A wide access does not OR the kinds of several bytes together, because the later bytes do not run.

## 10. FaultKind layout

`FaultKind` becomes a `[Flags]` enum with underlying type `ushort`. The low byte is the generic fault. It is the only byte a 65C02 status register needs to interpret, and it is the byte PR #489 stores in `MemoryBlock.Faults`. The high byte is 65816/65832 detail. A 65C02 latch MUST NOT set high-byte bits. Readers that only understand the 65C02 mask (`(byte)kind`) ignore the high byte.

Current values are a discriminant, not flags: `Nx = 3` would be `Unmapped | Permission` if those stay 1 and 2. A flags enum cannot keep 3, 4, and 5 as the names PR #489's prose lists. The compatible assignment keeps the two values that are already single bits, keeps `None = 0` so `00` still means "this data byte is valid", and moves the other three kinds onto their own bits.

### 10.1 Low byte (65C02 and everyone)

| Bit | Value | Name | PR #489 prose, today |
|---|---|---|---|
| — | `0x0000` | `None` | `00`, data byte valid |
| 0 | `0x0001` | `Unmapped` | 1, unchanged |
| 1 | `0x0002` | `Permission` | 2, unchanged |
| 2 | `0x0004` | `Nx` | was 3 |
| 3 | `0x0008` | `Misaligned` | was 4 |
| 4 | `0x0010` | `DeviceFault` | was 5 |
| 5 | `0x0020` | reserved | — |
| 6 | `0x0040` | reserved | — |
| 7 | `0x0080` | reserved | — |

`Unmapped` and `Permission` stay `0x01` and `0x02`, so a mask byte #489 already emits for those two faults still decodes. `Nx`, `Misaligned`, and `DeviceFault` change. #489 is merged, and the numbered kinds 1–5 are what `main` documents today. A5 owns the migration of that mask: `MemoryBlock` remarks, `DebugOutputRenderer.FaultText`, `emudbg.md`'s `--json` paragraph, and the tests that lock the bytes. The mask stays one hex byte per data byte, the low byte of `FaultKind`, omitted when every byte is `00`. Combined low-byte bits are legal. The 65C02 bus sets one low-byte bit per fault, not a combination, except that the high byte may be set by a later CPU. The mask MUST NOT keep emitting `03`, `04`, or `05` for `Nx`, `Misaligned`, and `DeviceFault` after A5.

`switch (fault.Kind)` in `MemCommand`, `PeekCommand`, `PokeCommand`, and `WriteCommand` MUST become bit tests. A combined value would miss every arm and fall through to "unknown".

### 10.2 High byte (65816 and 65832)

| Bit | Value | Name | Set when |
|---|---|---|---|
| 8 | `0x0100` | `Write` | The faulting access was a write. Absent means a read. |
| 9 | `0x0200` | `Fetch` | The faulting access was an instruction fetch. |
| 10 | `0x0400` | `BankCrossing` | The faulting byte sits in a different 64 KB bank from the access's starting address. |
| 11 | `0x0800` | `Privilege` | Reserved for a ring check. Not set by this epic. |
| 12–15 | | reserved | MUST stay clear. |

`Write` and `Fetch` are detail, not a reason to fault. `Permission` remains the low-byte bit for a `PagePerms` failure. `Privilege` is the high-byte bit for a ring failure that does not exist yet. `BankCrossing` without a low-byte bit is not a fault and MUST NOT be latched on its own.

`BadMango.Emulator.Bus.PrivilegeLevel` and `BadMango.Emulator.Core.PrivilegeLevel` are separate enums. Unifying them is #507. This spec does not rename either enum and does not choose which definition survives. The reserved `Privilege` bit is not a license to compare the two enums.

## 11. ABORT

`SignalLine` gains `Abort`. A transition of fault status from zero to nonzero MUST `ISignalBus.Assert(SignalLine.Abort, sourceId, cycle)` on the signal bus the machine gave `MainBus`. `sourceId` is `BusAccess.SourceId` of the faulting access (`CpuSourceId` is 0). Clearing status to zero MUST `Deassert` that assertion. `MainBus`'s constructor takes an optional `ISignalBus`. When it is null, the registers still latch and no line is driven. `MachineBuilder.Build` creates the `SignalBus` immediately before `new MainBus(...)` and MUST pass that instance in. Tests that construct a `MainBus` directly MAY omit it.

`SignalBus.AttentionPending` is the 65C02's per-instruction check. An asserted `Abort` MUST make `AttentionPending` true, the same way `Reset` does, so a `NoOp` reaction is a deliberate ignore and not an accident of the fast boundary check.

### 11.1 65C02

`Cpu65C02` has an `AbortReaction` property:

| Value | Behavior |
|---|---|
| `NoOp` | Default. The line is ignored. Status and address stay latched for the host. Execution continues, including the existing `HaltState.Stp` on unmapped fetch. |
| `HostBreak` | At the next instruction boundary, call `RequestStop`. Do not set `HaltState.Stp`. Do not push a vector. Do not change registers. |

`NoOp` is the default so current tests, which expect `Stp` or a floating-bus byte and do not expect a host stop, stay green. Neither value is a 6502 instruction.

`HostBreak` is a debugger setting, not a boot default. It lives in two places that stay in lockstep:

- `IDebugSession` gains `AbortReaction AbortReaction { get; set; }`. `MachineDebugSession` reads and writes `Cpu65C02.AbortReaction`. The getter returns `NoOp` when the attached CPU is not a `Cpu65C02`. The setter MUST throw `InvalidOperationException` when the CPU is not a `Cpu65C02` and the new value is `HostBreak`, because a 65816 uses section 11.2 instead of a host stop.
- The emudbg command `abort` calls that property. `abort` with no argument prints the current value. `abort noop` sets `NoOp`. `abort hostbreak` sets `HostBreak`. With `--json`, the result is one `emudbg/v1` envelope from `DebugSessionJsonContext`, the same path `regs` and `mem` use. The command MUST NOT pulse `SignalLine.Abort` itself. It only changes how the 65C02 treats a line the bus already asserted.

A session that never touches the property leaves the CPU at `NoOp`. Reset of the machine MUST NOT clear the setting. The host chose it, and a guest reset is not that choice.

### 11.2 65816 and 65832

`reference/WDC 65816 Emulator Reference.md` is the behavior, with the conflict in section 17 called out:

- `ABORTB` aborts the current instruction without modifying the registers that instruction was about to write.
- The address pushed is the address of the aborted instruction. `RTI` returns to that instruction.
- Vectors, bank 0: emulation `$FFF8` / `$FFF9`, native `$FFE8` / `$FFE9`.

There is no 65816 CPU type in the tree. The implementation sub-issue for the line MUST land the signal, the 65C02 property, and a test that a listener observes `Assert`. The vector sequence is a requirement on the 65816 CPU, not a requirement to create that CPU here.

## 12. BusFault only on a fault

`BusFault.Success(in BusAccess, int deviceId, RegionTag)` MUST NOT be called from `MainBus` read or write paths, `Try*` or non-`Try`. A successful `BusResult<T>` MUST use a zero `BusFault` (`Kind == None`, every other field at its default) or the parameterless `BusFault.Success()`. `BusResult<T>.Ok` stays `Fault.Kind == FaultKind.None`.

A `BusFault` MUST be constructed only when fault status is being set, or when a `Try*` method is returning `Failed` to a caller. Those are the same event for a non-debug CPU access: one record, built from the latch plus the CPU's PC, returned to the caller and, when `FaultRecordingEnabled` is true and the access is not debug, passed to `IBusFaultRecorder.Record`. Debug accesses still MUST NOT be recorded, which is the current `RecordAndReturnFault` rule.

`BusFaultRing` stays the ring. It stores `BusFault` values. It does not grow a second record type. The success path does not touch it.

`FaultRecordingEnabled` default remains false. The fast path does not load it.

## 13. Mapping trace

Wrong-bank reads under Apple II banking are often successful RAM reads. They are not faults, and once section 7 lands they are fast-path indexes. The default trace does not put a flag check on the fast path. A debugger who needs one instruction's resolved bank can force just those pages onto the slow path. Every other page stays fast.

`MainBus` gains `MappingTraceEnabled`, default false, and an optional trace range, default empty.

- When trace is false and the range is empty, the slow path MAY load the flag once per slow access, next to the existing `faultRecordingEnabled` load. The fast path MUST NOT load the flag or the range. Remap methods MUST NOT snapshot soft switches.
- When trace is true and the range is empty, each slow-path byte access appends one trace record into a fixed ring (the same shape as `BusFaultRing`: a pre-sized array, a `System.Threading.Lock`, no allocation on the append). The record holds cycle, address, intent, the target's existing `Name` reference, `RegionTag`, `PhysicalBase`, `DeviceId`, and page index. Copying the name reference MUST NOT allocate a string. Fast-path indexes are not records. This is the default when trace is turned on.
- When trace is true, each rebuild in section 7.3 appends a mapping-change record for the pages it replaced: page range, new target name, new `PhysicalBase`, new `PagePerms`, and the natural `FastOps` from section 7.2 before any range is applied. It also stores one soft-switch snapshot taken from `ISoftSwitchProvider.GetSoftSwitchStates()` on the providers the machine already has (`LanguageCardDevice`, `Extended80ColumnDevice`). `GetSoftSwitchStates` allocates a list. That allocation is allowed on the remap, which is a `$C0xx` write, and forbidden on the fast path. When trace is off, that call MUST NOT be made.
- The ring is readable from the debugger the way `IBusFaultRing` is. The emudbg command `maptrace` prints it. This epic does not add a window.

### 13.1 Forcing a range onto the slow path

`IDebugSession` gains `SetMappingTraceRange(uint start, uint endInclusive)` and `ClearMappingTraceRange()`. `maptrace range $start $end` calls the setter. `maptrace range clear` and `maptrace off` call the clearer. `maptrace on` enables remap-and-slow-path logging with an empty range. `maptrace` with no argument prints whether trace is on and what the range is. `--json` uses the `emudbg/v1` envelope.

Setting a range turns trace on. The range is in CPU addresses. Every 4 KB page that intersects `[start, endInclusive]` is covered, including a range that occupies only part of a page. The whole page goes slow, because `FastOps` is per page.

Eligibility is recomputed, not patched by a check on the CPU fast path:

1. Compute the natural `FastBacking`, `FastBase`, and `FastOps` from section 7.2.
2. If that page intersects the active range, publish `FastOps` as `None`. Leave `FastBacking` and `FastBase` in place so the slow path can still name the array in the trace record.
3. Otherwise publish the natural `FastOps`.

`SetMappingTraceRange` runs that function on every page of the new range, and on every page of the previous range that the new range does not cover. `ClearMappingTraceRange` runs it on every page of the range being cleared, with no range active, which restores natural `FastOps`. Section 7.3's rebuild runs the same function, so a language-card or 80STORE remap while a range is active keeps a covered page slow and leaves a page outside the range fast. The CPU still only reads the published `FastOps`. It does not learn that a range exists.

Accesses to a forced page are ordinary slow-path accesses, so they append the per-access record while trace is on. Accesses outside the range stay on the fast path and add no record. Clearing the range does not by itself turn trace off. `maptrace off` clears the range, recomputes those pages, and then disables trace.

## 14. API migration

`TryRead8`, `TryWrite8`, and the wide `Try*` methods stay on `IMemoryBus`. The debugger, the future MCP host, and the tests are their callers. The CPU hot path stops being one of them for eligible pages.

Call sites on the baseline:

| Caller | What it does |
|---|---|
| `CpuBase` read/write/peek/poke and the wide helpers | `Try*` for every byte. Moves to section 8. |
| `Cpu65C02.InstructionFetch8` | `TryRead8` with `InstructionFetch`. Moves to section 8. |
| `MachineDebugSession.ReadMemory`, `WriteMemory`, screen text | Debug `Try*`. Stays on `Try*`. |
| `MemCommand`, `PeekCommand`, `PokeCommand`, `ReadCommand`, `WriteCommand`, `LoadCommand`, `SaveCommand`, `PwTimeCommand` | Debug `Try*`, then a `FaultKind` switch. Stays on `Try*`. The switch becomes bit tests when section 10 lands. |
| `WatchpointManager` | Builds a `BusAccess` while inspecting an effective address. It does not see fast-path bytes, and it does not today either (it sees the operand address after the step). No change required for the fast path. |
| Tests under `BadMango.Emulator.Bus.Tests` and the debug command tests | Construct `BusFault` and call `Try*` directly. Stay valid. |
| MCP | Not in the tree. #337 MUST call `IDebugSession`, not `CpuBase.Read8` and not `TryRead8` of its own. |

`MachineDebugSession.ReadMemory` already keeps going across faults and stores `(byte)FaultKind` in `MemoryBlock.Faults`. That is the merged #489 shape. This epic does not add a third one. A5 changes the bytes that shape carries to section 10. `ReadMemory` MUST keep storing the low byte and MUST keep omitting the mask when every byte is `00`.

Stages, each of which leaves `dotnet test` green:

1. Add `FastBacking`, `FastBase`, and `FastOps`, and rebuild them on every store. `FastOps` may be set. The CPU still calls `Try*`. Tests that compare whole `PageEntry` values MUST ignore the new fields or opt into them.
2. Switch `CpuBase` / `Cpu65C02` byte and wide helpers to section 8. `Try*` success results are unchanged. CPU-visible bytes, `Stp`, and `TCU` stay as they are.
3. Stop calling `BusFault.Success(in BusAccess, ...)` from `MainBus`. Success results carry a zero fault. `Ok` stays true. Update tests that assert the success fault's address, device, or region (`BusFaultTests` covers the factory, which MAY remain; production call sites go away). `Kind == None` tests stay valid.
4. Change `FaultKind` to the section 10 layout. Fix `switch` statements and any test that casts the kind to a raw `3`, `4`, or `5`. Named uses (`FaultKind.Unmapped`) stay source-compatible for the values that did not move, and stay source-compatible for the names that did.
5. Add the registers, the latch rules, and `SignalLine.Abort` with `AbortReaction.NoOp` as the default. Existing fault tests still see `BusResult.Failed`. New tests read the registers.
6. Build the ring's `BusFault` from the latch plus PC, only when status is nonzero. Debug reads still do not record and do not latch.

No stage removes `BusResult<T>` or makes `TryRead8` throw.

## 15. Benchmarks

The project is `benchmarks/BadMango.Emulator.Benchmarks`. The in-tree workload benchmark is `WorkloadMhzDispatchBenchmark`: `Mix`, `Ram`, and `FlagsTransfer`, on `BareCpuFixture`, with `MemoryDiagnoser`. PR #496 (not merged) is the benchmarking spec: same machine before and after, 0 bytes per instruction on the hot path, and a compare threshold of 5% when the intervals do not overlap. This section uses that rule. It does not wait on #496 to add a `Bus/` folder that `main` does not have yet. The new benchmark class MAY live beside `WorkloadMhzDispatchBenchmark` until that folder exists, and SHOULD move under `Bus/` if #496 merges first.

### 15.1 Workloads

Run these, before and after, on one machine, with the job PR #498 used when it is available (warmup 6, 30 iterations, 200 ms, `MemoryDiagnoser`) and otherwise with the project's default profile:

| Workload | Fixture | Why |
|---|---|---|
| `MixWorkload` | `BareCpuFixture` | The number #499 is aimed at. Thirteen of its 25 instructions are load/store, all of which become array indexes. |
| `RamWorkload` | `BareCpuFixture` | A RAM sweep. Entirely the fast path. |
| `FlagsTransferWorkload` | `BareCpuFixture` | Almost no data traffic. It MUST NOT get slower by more than the 5% rule. It is the control. |
| Device-page / soft-switch | A machine, not `BareCpuFixture` | The slow path. `BareCpuFixture` has no `$C000` page and no language card. |

The device-page workload is a new loop. It MUST execute, and a unit test MUST show that it executes, all of:

- a read and a write of a plain RAM byte (fast path once this spec is implemented; slow path on the baseline),
- a read of a ROM byte,
- a read of a soft switch in the `$C000` page (composite, slow),
- a language-card switch write that remaps `$D000`–`$FFFF`, then a read of `$D000` in the bank that switch selected,
- an 80STORE / PAGE2 transition that remaps text page 1, then a read of `$0400`.

The test that proves the opcodes ran is the same idea as #496's `Exercises` check: step one iteration and account for each access. The benchmark's measured region is the loop, not the setup.

`GlobalSetup` builds that machine the way the existing machine tests build a Pocket2e (language card and 80-column device installed). It MUST NOT read the disk and MUST NOT need a ROM image. RAM and ROM fills are test bytes. If PR #496's `MachineFixture` exists when this is implemented, the benchmark SHOULD use it.

### 15.2 Targets

Allocation, all four workloads, mapping trace off, fault recording off: **0 bytes per instruction**. A run that reports any allocated bytes per instruction fails, including the device-page loop. The trace ring is allocated in `GlobalSetup` only when a separate, unmeasured configuration turns trace on. That configuration is not a target.

Mix, effective MHz. PR #498's published figure, which is not on `main`, is 96.42 MHz (46.88 ns/instruction) on one KVM guest. Absolute MHz is not portable. The implementation PR MUST re-record the baseline on its own machine at the commit it branches from.

- The mix workload MUST be at least **50% higher** effective MHz than that same-machine baseline, and the 99.9% confidence intervals MUST NOT overlap.
- The mix workload SHOULD reach **2×** that baseline. #498's conclusion was that dispatch changes of about 1% did not move mix because the bus dominates. Removing `BusAccess`, the interface `Try*` call, and the success `BusFault` is the change that is supposed to move it. 2× is the goal. 50% is the bar that still proves the fast path is what moved, on a noisy guest.

On the #498 guest those ratios are about 145 MHz and about 193 MHz. Those are illustrations, not the bar. The bar is the ratio to the re-recorded baseline.

Ram SHOULD move by at least as much as mix. Flags-transfer MUST stay inside the 5% rule and at 0 bytes. The device-page workload has no MHz goal. It MUST stay inside the 5% rule against its own baseline (the slow path must not get slower) and at 0 bytes.

Reporting follows #496 section 9 once that spec is merged: workloads, evidence they hit the path, before/after table, machine, runtime, profile, allocations. Until then, the implementation PR includes that table inline.

## 16. Folding this into #501

#501 replaces `specs/Architecture Spec v1.0 Part 1.md`, `Part 2.md`, `Part 3.md`, and `Appendix.md` with a root `ARCHITECTURE.md` and one child page per layer. This file is the normative text for the bus/MMU layer until that split happens. #501 SHOULD do the following, and SHOULD NOT restate a second, weaker fast path:

- `ARCHITECTURE.md`'s "where to look" table maps "bus faults" to this spec, `FaultKind`, `MainBus.TryRead8`, and `CpuBase.Read8`, and maps "soft switches" to `LanguageCardDevice`, `Extended80ColumnDevice`, and section 7.3 of this spec.
- The bus/MMU child page links this spec for the fast path, the fault registers, `FaultKind`, and `ABORT`. It keeps the addressing and device-routing narrative that this spec does not repeat. Per-CPU sections: 65C02 uses the low byte and `AbortReaction`; 65816/65832 use the high byte and the vector sequence in section 11.2.
- `reference/Main bus architecture.md` stays as the historical note. The child page says the `Func<>` and function-pointer paragraphs are superseded by section 6 and section 7.
- The v1.0 parts, once reduced to an index, link here instead of describing `Try*` as the hot path.
- `AGENTS.md` and `.github/copilot-instructions.md` point at `ARCHITECTURE.md`, which points here. They do not grow a second copy of the fast-path rules.

This epic does not edit those architecture files. #501 does, after this spec is the contract.

## 17. Where the code and the issues disagree

These are decisions, not leftovers. Appendix A does not contain a sub-issue that "fixes" them unless the row says so.

1. **NX is dead on the CPU path.** `TryRead8` raises `Nx` only for `BusAccessMode.Atomic`. Every CPU factory passes `Decomposed`. `PagePerms` says NX is ignored in compat mode, and `GetEmulationFlag` on the 65C02 returns true. #499's list of per-byte checks includes NX. The 65C02 fast path does not add it. A native fetch checks `FastOps.Fetch` and otherwise uses the slow path, which still will not raise `Nx` until some CPU issues `Atomic`. That change is out of scope.
2. **Privilege fields are stored and not checked.** `PageEntry` carries three minimums. `TryRead8` ignores them. `BadMango.Emulator.Bus.PrivilegeLevel` and `BadMango.Emulator.Core.PrivilegeLevel` are separate enums. Unifying them is #507. This spec does not rename either enum and does not choose which definition survives. The fast path treats the default minimums as no extra restriction, because the slow path does not enforce them, and it does not compare the two enums. The high-byte `Privilege` bit stays clear.
3. **`BusResult.Cycles` is not the instruction cycle count.** #500 asks for cycle accounting to stay correct. The CPU never reads `BusResult.Cycles`. Correct means `Registers.TCU` and the 256-byte page-cross increments stay as they are. Adding the bus's `cycles: 1` into `TCU` would double-count.
4. **#489 has merged, and its kind numbers are not a flags layout.** `main` documents the mask as 1, 2, 3, 4, 5 in `MemoryBlock`, `DebugOutputRenderer.FaultText`, and `emudbg.md`. Section 10 keeps 0, 1, and 2, and moves 3, 4, and 5 onto bits 2, 3, and 4. `FaultKind` can no longer be a `byte` once the high byte exists. The mask stores the low byte. A5 is the change that moves the merged mask, the `--json` text, and the tests together.
5. **Sticky status versus "success leaves status at zero".** #499 can be read as "every success writes zero". Section 9 chooses first-fault-wins and a sticky register, cleared by the host or by reset. A success writes nothing.
6. **Composite holes are successes.** A null `ICompositeTarget.ResolveTarget` returns `0xFF` with `Ok == true`. A null `PageEntry.Target` returns `Failed` and `Unmapped`. Only the second latches status and can assert `ABORT`. Treating every `0xFF` as a fault would break the I/O page.
7. **Traps and watchpoints are not why a page is slow.** #500 lists them next to devices and composite pages. They run outside the bus, and the fast path does not consult them. Watchpoints still miss fetch and stack traffic, which is the limitation `WatchpointManager` already documents.
8. **"Atomic" remap is a struct store.** `RemapPage` and `SelectSwapVariant` replace a `PageEntry` in an array. The CPU does not read that slot during the store, because the store happens inside its own slow-path write. This spec does not add a lock to make the struct store atomic against a second thread.
9. **`Read16` does not wrap at 64 KB.** Addressing modes wrap zero-page pointers themselves. `CpuBase.Read16` goes through `TryRead16`, and the high byte of an access at `$FFFF` is `$10000`. The fast path keeps that. A 6502-accurate wrap inside `Read16` would change current behavior and is not a sub-issue of #499.
10. **The published mix number is not on `main`.** 96.42 MHz is PR #498, open. Section 15 uses it as a reference point and requires a same-machine re-record.
11. **MCP does not exist.** #499's caller list includes it. Section 14 names `IDebugSession` as the surface #337 will use. There is no MCP project to migrate in this epic.
12. **The architecture note's fast path is a direct `IBusTarget` call, then a `Func<>`, then a function pointer.** The measured cost includes that interface call and the success `BusFault`. Section 7 indexes a `byte[]` instead. The note remains historical; section 16 points #501 at this spec.
13. **ABORT "native only".** The vector table in `reference/WDC 65816 Emulator Reference.md` lists emulation `$FFF8` and native `$FFE8`, and the notes column says "native only". The ABORT Handling paragraph does not say emulation lacks the pin. Section 11.2 follows the paragraph and both columns. #501, or a 65816 CPU issue, should strike or confirm the "native only" words.
14. **Default mapping trace does not log fast RAM reads.** #499 wants wrong-bank reads diagnosed, and also wants zero cost on the fast path when trace is on. A wrong-bank RAM read is a fast index. With no range, section 13 logs the remap and the soft-switch snapshot, not the `LDA`. Per-access detail is the range in section 13.1: those pages are recomputed onto the slow path, and pages outside the range stay fast. The fast path still does not load a trace flag.

## Appendix A. Proposed implementation sub-issues

For epic #499. This appendix does not file or edit GitHub issues. Sizes: S is one type plus its tests, M crosses a component boundary, L is the CPU byte path and the tests that pin bytes, cycles, and `Stp`. None of these is XL.

### A1. Fast-path fields on `PageEntry`, rebuilt on every remap

- **Scope.** Add `FastBacking`, `FastBase`, and `FastOps` to `PageEntry`. Add the slice accessor on `RamTarget` and `RomTarget`. Recompute the fields in every `MainBus` method section 7.3 names. Map-time window check. No CPU change.
- **Acceptance.** A `RamTarget` page mapped from `PhysicalMemory` has `FastOps` read and write when `PagePerms.All` is set. A `RomTarget` page has `Read` and not `Write`. A `LanguageCardSplitTarget` page and the `$C000` composite page have `FastBacking == null`. `SelectSwapVariant` between two RAM banks changes `FastBase` or `FastBacking` before it returns. A slice that is not array-backed, and a window that runs off the end of the array, stay slow. Tests cover language-card `$C080` / `$C082` / `$C083` and an 80STORE remap.
- **Dependencies.** None.
- **Size.** M

### A2. CPU byte fast path and slow-path fallback

- **Scope.** `CpuBase.Read8`, `Write8`, `Peek8`, `Poke8`, and `Cpu65C02.InstructionFetch8` follow section 8.1 and section 8.2. Other `IMemoryBus` implementations stay on `Try*`.
- **Acceptance.** On `BareCpuFixture`, a RAM `Read8` / `Write8` / fetch does not call `TryRead8` / `TryWrite8` (a test double page, or a counter on `MainBus`, is enough). A write to a ROM page and a read of an unmapped page still go through `Try*` and still return the current `Stp` / `0xFF` results. `Peek8` of RAM matches `Read8`. `Poke8` of a writable `RomTarget` still changes the byte. An existing language-card or aux test reads the newly selected bank on the byte after the soft-switch write. `GetEmulationFlag` and `IEventContext.Now` are not called on the fast path (the test can observe a subclass).
- **Dependencies.** A1.
- **Size.** L

### A3. Multi-byte accesses, 4 KB crossings, and cycle accounting

- **Scope.** `Read16`, `Write16`, `Read32`, `Write32`, and `MainBus`'s non-`Try` wide methods follow section 8.3 and section 8.4.
- **Acceptance.** A same-page `Read16` of RAM matches two `Read8`s and does not call `TryRead8`. A `Read16` that crosses a 4 KB boundary uses the fast path on an eligible side and the slow path on the other. A `Write16` whose second page is unmapped leaves the first byte stored and latches nothing until A4; until A4 it still returns the current `Try*` fault. `Read16($FFFF)` on a 16-bit bus still faults the high byte as unmapped, not as `$0000`. `Cpu65C02BusCycleAndPcWrapTests` and the addressing-mode page-cross tests pass unchanged. A fast `Read8` does not change `TCU` by itself; `FetchByte` still adds 1.
- **Dependencies.** A2.
- **Size.** M

### A4. Fault-status and fault-address registers

- **Scope.** The two registers and the private latch on `MainBus` / `IMemoryBus`, section 9. CPU slow path and `Try*` share one latch helper. Debug accesses and composite holes do not latch. First-fault-wins. Host clear and machine reset.
- **Acceptance.** A successful RAM read leaves status at 0 and does not change a status that was already `Unmapped`. Two unmapped reads in a row keep the first address. A `Read16` whose second byte is unmapped latches that second address and does not issue a third access. `Peek8` of an unmapped address does not change status. `Clear` of status zeroes the address. Reset zeroes both. No test calls a 6502 opcode to read them.
- **Dependencies.** A2 for the CPU slow-path hook. The register types themselves do not need the fast path.
- **Size.** M

### A5. `FaultKind` flags, and migrate the merged #489 mask

- **Scope.** Section 10. Underlying type `ushort`. #489 is merged. This item owns moving every published copy of the numbered kinds 1–5 onto the flag bytes, in the same change, so the mask and the docs cannot drift:
  - `FaultKind` itself, and `tests/BadMango.Emulator.Bus.Tests/FaultKindTests.cs` (today it asserts six discriminant values and `(byte)None == 0`).
  - `MachineDebugSession.ReadMemory`, which stores `(byte)read.Fault.Kind` in `MemoryBlock.Faults`. The cast stays. The values change.
  - `MemoryBlock` remarks, which list "1 Unmapped, 2 Permission, 3 Nx, 4 Misaligned, 5 DeviceFault".
  - `DebugOutputRenderer.FaultText`, whose switch arms are the raw bytes 1, 2, 3, 4, and 5. Combined bits MUST print the set names, not "Fault kind N" for `0x04`.
  - `emudbg.md`, the `mem` / `peek` `--json` paragraph that lists those same numbers.
  - Golden and command tests that lock the mask, including `DebugAbstractionsJsonGoldenTests` and `DebugCommandsTests.Json`. A test MUST fail if `Nx` is encoded as `03`, `Misaligned` as `04`, or `DeviceFault` as `05`.
  - The `switch (fault.Kind)` formatters in `MemCommand`, `PeekCommand`, `PokeCommand`, `ReadCommand`, and `WriteCommand`, which become bit tests.
- **Acceptance.** `None` is 0. `Unmapped` is `0x0001`. `Permission` is `0x0002`. `Nx` is `0x0004`. `Misaligned` is `0x0008`. `DeviceFault` is `0x0010`. High-byte names match section 10.2. A 65C02 `TryRead8` fault has a zero high byte. `(byte)FaultKind.Unmapped` is `0x01`. `ReadMemory` of an unmapped byte still returns a mask whose low byte is `01`, and a permission fault is `02`. An Nx fault's mask byte is `04`. `emudbg.md` and the `MemoryBlock` remarks name those flag values, not 3, 4, and 5. `emudbg --json` output for `mem` and `peek` uses that mask. The command formatters print a combined value rather than "unknown". Tests that use the enum names pass.
- **Dependencies.** None. A4 SHOULD land after A5 so the register is born as a `ushort`.
- **Size.** M

### A6. `SignalLine.Abort`, the 65C02 reactions, and the debugger switch

- **Scope.** Section 11, including section 11.1's session property and the `abort` command. The 65816 vector sequence is specified here and implemented with that CPU. `MainBus` takes an optional `ISignalBus`. `AttentionPending` includes `Abort`. `AbortReaction` on `Cpu65C02` defaults to `NoOp`. `IDebugSession.AbortReaction` and `abort noop` / `abort hostbreak` are the only way the debugger selects `HostBreak`.
- **Acceptance.** A latched non-debug unmapped access asserts `Abort` once. A second fault does not assert again. Clearing status deasserts. With the default `NoOp`, an unmapped fetch still sets `HaltState.Stp` and does not call `RequestStop`. After `abort hostbreak` (and after setting the session property directly), the next instruction boundary calls `RequestStop` and does not set `Stp`. `abort noop` restores the default. A machine reset leaves the setting alone. `--json` on `abort` is one `emudbg/v1` envelope. Setting `HostBreak` on a session whose CPU is not a `Cpu65C02` throws `InvalidOperationException`. A bus constructed without a signal bus latches status and does not throw. A unit test documents the 65816 vector addresses from section 11.2 without implementing a 65816.
- **Dependencies.** A4. A5 for the status type.
- **Size.** M

### A7. Construct `BusFault` only when status is nonzero

- **Scope.** Section 12. Remove production calls to `BusFault.Success(in BusAccess, ...)`. Add `Pc` to `BusFault`. The ring receives the same record the failing `Try*` returns.
- **Acceptance.** A successful `TryRead8` returns `Kind == None` and default address, device, and region. A test can count `BusFault` constructor calls, or the success factory can be internal to tests: the production `MainBus` success path does not call the 9-field factory. A recorded fault's `Pc` is the CPU's PC for a CPU access and 0 for a direct `Try*` test. Debug accesses still do not enter the ring. `BusFaultTests` for the named factories still pass.
- **Dependencies.** A4, A5.
- **Size.** S

### A8. Mapping trace, soft-switch snapshots, and a slow-path range

- **Scope.** Section 13 and section 13.1. `MappingTraceEnabled`, the optional range, the fixed ring, remap snapshots via `ISoftSwitchProvider.GetSoftSwitchStates`, and the `maptrace` command on `IDebugSession`. Setting or clearing the range recomputes `FastOps` for the covered pages as section 13.1 describes, including a rebuild that happens while the range is active. Default with no range stays remap-and-slow-path only.
- **Acceptance.** With trace clear, a mix benchmark stays at 0 bytes and the fast path does not read the flag or the range. With trace on and no range, a language-card switch records the `$D000` target name and physical base after the switch, plus a snapshot that includes the language-card state, and a `$C000` read records one slow-path line. A fast RAM `LDA` outside any range does not add a line. `maptrace range $0800 $08FF` clears `FastOps` on the one 4 KB page that contains `$0800`, and an `LDA` there appends a per-access record that names the backing target. An `LDA` in the next page does not. `maptrace range clear` restores natural `FastOps` on that page and the next `LDA` there is not recorded. A language-card remap of a page inside the range leaves that page slow. `maptrace off` clears the range and restores those pages. The ring does not allocate per record. `--json` on `maptrace` is one `emudbg/v1` envelope.
- **Dependencies.** A1 for the rebuild hook. The REPL surface has no other CPU-fast-path dependency. Forcing `FastOps` to `None` is meaningful once A2 reads `FastOps`.
- **Size.** M

### A9. Migrate `Try*` callers without breaking tests

- **Scope.** Section 14 stages 3 through 6 as they apply to `MachineDebugSession`, the debug commands, and the bus tests. MCP is a note in the session remarks pointing #337 at `IDebugSession`, not a new server. The #489 mask already exists. A5 changes its bytes. This item does not invent a third memory-read shape.
- **Acceptance.** `dotnet test` is green at each stage. `MemCommand` still prints `??` for an unmapped byte. `ReadMemory` still returns a mask, and after A5 that mask is the section 10 low byte. No caller of `TryRead8` is deleted except the CPU hot path from A2. A grep note in the PR lists remaining `TryRead8` / `BusFault.Success(in BusAccess` call sites.
- **Dependencies.** A5 and A7 for the flag and success-payload changes. A2 is already merged by the time the CPU drops its `Try*` calls; this row is the non-CPU callers.
- **Size.** M

### A10. Device-page benchmark and the mix acceptance bar

- **Scope.** Section 15. The new workload and its coverage test. A before/after table for mix, RAM, flags-transfer, and the device-page loop.
- **Acceptance.** The coverage test fails if the loop does not touch RAM, ROM, `$C000`, a language-card bank, and an 80STORE page. The measured device-page loop allocates 0 bytes and is within 5% of its baseline. Mix on the implementation machine is at least 50% above its re-recorded baseline with non-overlapping 99.9% intervals, at 0 bytes. The PR reports the SHOULD 2× goal as hit or missed, with the numbers. Flags-transfer stays inside 5% and at 0 bytes.
- **Dependencies.** The workload and the baseline capture MAY land before A2. The acceptance run depends on A2 and A3.
- **Size.** M
