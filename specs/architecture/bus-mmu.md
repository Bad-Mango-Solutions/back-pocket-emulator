# Bus and MMU

## Summary

The built emulator routes 65C02 memory operations through a host page table of 4 KB pages represented by `PageEntry` values. The table selects RAM, ROM, device, or composite targets and carries permissions and routing metadata. PocketGS and PocketME address models remain aspirational.

**Covers:** address-space models, host page-table entries, bus targets and composite dispatch, dynamic remapping, and bus access policy.

**Does not cover:** the normative CPU hot path and fault implementation, signal wiring, scheduler timing, or machine/profile assembly. Those contracts live in the documents linked below.

## Code paths

- [MainBus](../../src/BadMango.Emulator.Core/MainBus.cs) owns routing and the live page table.
- [PageEntry](../../src/BadMango.Emulator.Core/PageEntry.cs) describes a page's target, permissions, region, and host fast-path eligibility.
- [IBusTarget](../../src/BadMango.Emulator.Core/Interfaces/IBusTarget.cs) is the target contract; [ICompositeTarget](../../src/BadMango.Emulator.Core/Interfaces/ICompositeTarget.cs) supports pages that dispatch within the 4 KB region.
- [IMemoryBus](../../src/BadMango.Emulator.Core/Interfaces/IMemoryBus.cs) exposes checked access and page remapping.
- [RamTarget](../../src/BadMango.Emulator.Core/RamTarget.cs), [RomTarget](../../src/BadMango.Emulator.Core/RomTarget.cs), and [CompositeIOTarget](../../src/BadMango.Emulator.Core/CompositeIOTarget.cs) are the principal target implementations.
- [BusAccess](../../src/BadMango.Emulator.Core/BusAccess.cs), [BusResult](../../src/BadMango.Emulator.Core/BusResult.cs), [BusResult\<T\>](../../src/BadMango.Emulator.Core/BusResult%7BT%7D.cs), [PagePerms](../../src/BadMango.Emulator.Core/PagePerms.cs), and [TargetCaps](../../src/BadMango.Emulator.Core/TargetCaps.cs) describe access context, results, permissions, and capabilities.

The target assembly for this layer is `BadMango.Emulator.Core`, retaining the existing `BadMango.Emulator.Bus` and Core namespaces. `Devices` remains separate and references Core and Storage.

> **Current location (until #565):** relevant cross-suite coverage in `tests/BadMango.Emulator.Tests`; target: subject-specific test projects (namespace unchanged).

## Addressing and address spaces

### 3.1 Address Space Models — **Status: Partly implemented**

The built Pocket2e machine uses a flat 16-bit virtual address space backed by a 4 KB host page table: `address >> 12` selects a `PageEntry`, and `address & 0xFFF` selects an offset within that page. The bank-switching hardware changes entries rather than replacing the address width or adding a guest CPU-level TLB.

PocketGS's 24-bit and PocketME's 32-bit, 4 KB two-level page-table models are not built. The PocketME `PageTableEntry` design belongs to the 65832 companion rather than this host routing contract.

### 3.5 Fault Status Codes — **Status: Superseded**

The v1.0 `FaultStatusCode` enum was never implemented. Bus faults use the live `FaultKind` contract and host fault registers implemented in `MainBus`.

The fault values, fault-register behavior, `FaultKind`, and `ABORT` line are normative in [Bus Fast Path and Fault Registers Specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md). This page does not duplicate or restate them.

## Host page table and main bus

### 4.1 Design Philosophy — **Status: Partly implemented**

The surviving model has three planes:

- **Data plane:** `PageEntry[]` lookup routes each access to a target. Plain RAM and ROM can participate in the fast path.
- **Control plane:** mapping, remapping, layer activation, and swap-group state change live page-table entries.
- **Observability plane:** fault and mapping diagnostics remain separate from ordinary data access.

The historical [Main bus architecture](../../reference/Main%20bus%20architecture.md) proposed this three-plane framing and the data/control/observability split. It is a historical design note: its `Func<>` and function-pointer proposals are superseded by [the normative fast-path specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).

### 4.2 Access Context — **Status: Implemented**

`BusAccess` carries address, write value, width, access mode, emulation state, intent, source, cycle, flags, and privilege data. `BusAccessMode`, `AccessIntent`, and `AccessFlags` are implemented.

Debug reads and writes use non-side-effect access flags. Wide access context is used to request atomic or decomposed behavior.

### 4.3 Fault Model — **Status: Superseded**

The v1.0 byte-wide fault model and always-present success fault are superseded. Implemented faults use `FaultKind` and `BusFault` only as a consequence of the checked access path. Faults and access success/failure behavior are defined normatively in [Bus Fast Path and Fault Registers Specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).

### 4.4 Bus Result Types — **Status: Partly implemented**

`BusResult` and `BusResult<T>` wrap the checked `Try*` access APIs. Plain CPU byte accesses use the live page table and do not construct a result for a fast-path hit. The complete hot-path contract is in the fast-path specification rather than this page.

### 4.5 Page Table Structure — **Status: Partly implemented**

`PageEntry` contains the structural device ID, region tag, permissions, target capabilities, target, physical base, and host fast-path metadata. Existing privilege minimum fields are stored but are not enforced. `RegionTag` is the implemented routing classification and is not identical to the historical `MainRAM`/`AuxRAM`/`ROM`/`IOPage` sketch.

### 4.6 Bus Target Interface — **Status: Implemented**

`IBusTarget` supplies read and write operations for a mapped region. Wide operations may provide target-specific implementations; otherwise the bus may decompose them. `ICompositeTarget.ResolveTarget` selects a sub-target for an offset in a composite page, while `GetSubRegionTag` supplies its observability classification.

### 4.7 Memory Bus Interface — **Status: Partly implemented**

`IMemoryBus` provides checked `Try*` operations, direct and debug access operations, page inspection, mapping, and remapping. A fast CPU path may bypass `Try*`; slow and device-backed accesses retain the checked interface and access policy.

### 4.8 Main Bus Implementation — **Status: Partly implemented**

`MainBus` owns the 4 KB `PageEntry[]`, checked routing, wide-access handling, fast-page metadata, remapping, and fault registers. RAM and ROM targets expose memory-backed pages, while composite and device targets retain target dispatch. The exact hot-path eligibility and rebuild rules are normative in [Bus Fast Path and Fault Registers Specification §7–§8](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).

### 4.9 Access Policy Rules — **Status: Partly implemented**

The ordered policy concerns unmapped pages, read/write permissions, fetch policy, wide-access decomposition, target capabilities, and debug no-side-effect access. Cross-page and explicitly decomposed wide accesses follow byte-wise behavior; atomic wide behavior requires an eligible target and native-style access.

The 65C02 compatibility path does not raise `FaultKind.Nx` from an instruction fetch. The implementation details and all fault outcomes are authoritative in [Bus Fast Path and Fault Registers Specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).

## Composite dispatch and remapping gaps

### Gap 1: Composite Page Dispatch — **Status: Implemented**

The Apple II $C000–$CFFF region is one composite page. `ICompositeTarget.ResolveTarget` dispatches an offset to soft switches, slot ROM, expansion ROM, or another sub-target. A null sub-target follows the implemented floating-bus behavior rather than becoming a second mapping concept.

`CompositeIOTarget` and `IOPageDispatcher` implement the $C000 page composition.

### Gap 2: Dynamic Page Remapping — **Status: Implemented**

`IMemoryBus` exposes single-page and range remapping. Language-card, auxiliary-memory, and other bank-switching devices replace live `PageEntry` values to select a target or physical base. Remapping is visible to the next memory operation through the live page table.

## Source of truth

- [Bus Fast Path and Fault Registers Specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md) — normative hot path, fault registers, `FaultKind`, and `ABORT`.
- [Architecture Spec v1.0 Part 1](../Architecture%20Spec%20v1.0%20Part%201.md) — historical/audited source; its copied implementation sketches are not authoritative where they conflict with the audit or the normative fast-path specification.
- [Main bus architecture](../../reference/Main%20bus%20architecture.md) — historical design note.
