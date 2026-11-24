# IIc and 65832 Personalities

## Summary

Apple IIc hardware differences and 65832 guest personalities are unbuilt.
Pocket2e is a real 65C02 machine, not a compatibility window inside a 65832.
This page stays short because both subjects are aspirational.

Section status matches the
[#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
audit in
[`Architecture Docs Restructure Proposal.md`](../Architecture%20Docs%20Restructure%20Proposal.md):

- **Aspirational:** §7.8, §7.8.1, §7.8.2, §7.8.4, §8.1, and §8.2.
- **Partly implemented:** §7.8.3. The IIe INTCXROM switches exist. There is
  no IIc firmware-image switch.

Target assembly layout comes from
[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
and
[`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md).
Namespaces stay unchanged during the
[#477](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/477)
folds. Folding Bus and Emulation into Core does not create an IIc profile
or a guest personality.

## Covers

- Apple IIc differences (§7.8–§7.8.4), from
  [Architecture Spec v1.0 Part 2](../Architecture%20Spec%20v1.0%20Part%202.md#78-apple-iic-differences).
- The 65832 compatibility context (§8.1) and device-page object model (§8.2).

## Does not cover

- The IIe I/O page, slot install path, and INTCXROM switch table. Those are
  [devices and soft switches](devices.md#774-internal-rom-soft-switches--implemented).
- Expansion-ROM banking and trap registration
  ([#546](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/546);
  `specs/architecture/rom-traps.md` is not written yet).
- 65832 privilege, paging, and boot
  ([#541](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541)).
  The draft stays
  [Privileged architecture spec v0.1 for 65832](../../reference/Privileged%20architecture%20spec%20v0.1%20for%2065832.md).
  This page does not copy it.
- CP/E architectural modes on the shared register file
  ([CPU core](cpu.md#12-compatibility-layers)). A mode value is not a nested
  guest.
- Pocket2e profile assembly
  ([#549](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/549)).
- Host page-table routing ([bus and MMU](bus-mmu.md)).

## Code paths

Aspirational CPU and bus support is described against the target
`BadMango.Emulator.Core` assembly. CPU placeholders keep the existing
`BadMango.Emulator.Emulation.Cpu` namespace. Bus types keep
`BadMango.Emulator.Bus` and `BadMango.Emulator.Bus.Interfaces`. Machine
composition stays in `BadMango.Emulator.Systems`. Peripherals stay in
`BadMango.Emulator.Devices`. None of those homes is an IIc machine or a
65832 guest runtime.

> **Current location (until #562):** `src/BadMango.Emulator.Bus` and
> `src/BadMango.Emulator.Emulation`; target: `src/BadMango.Emulator.Core`
> (namespaces such as `BadMango.Emulator.Bus` and
> `BadMango.Emulator.Emulation.Cpu` unchanged).

> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`;
> target: `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

Checked against the tree:

- No IIc profile and no `AppleIIc` type. Profiles are `pocket2e*.json` and
  `simple-65c02*.json`, including
  [`profiles/pocket2e.json`](../../profiles/pocket2e.json).
- [`SlotManager`](../../src/BadMango.Emulator.Bus/SlotManager.cs) always
  allocates seven peripheral slots (`PeripheralSlots` 1-7).
- No built-in serial, mouse, or memory-expansion ROM window.
- `RegisterIIcTraps` does not exist.
- No `CompatibilityPersonality` or `CompatibilityController`.
  [`SystemRegisters`](../../src/BadMango.Emulator.Core/Cpu/SystemRegisters.cs)
  has CR0, PTBR, VBAR, FAR, FSC, and TLS, and no COMPATID field.
- No `IDevicePageObject`. Devices implement
  [`IBusTarget`](../../src/BadMango.Emulator.Bus/Interfaces/IBusTarget.cs)
  or register on
  [`IOPageDispatcher`](../../src/BadMango.Emulator.Bus/IOPageDispatcher.cs).
- [`Cpu65832`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65832.cs) throws
  `NotImplementedException` from `Step` and `Reset`.
- The built machine is
  [`Pocket2eMachineBuilderExtensions`](../../src/BadMango.Emulator.Systems/Pocket2eMachineBuilderExtensions.cs).

[`DevicePageId.CreateCompatIO`](../../src/BadMango.Emulator.Bus/DevicePageId.cs)
and
[`DevicePageClass.CompatIO`](../../src/BadMango.Emulator.Bus/DevicePageClass.cs)
encode a 24-bit identifier in `BadMango.Emulator.Bus`.
[`DevicePageIdTests`](../../tests/BadMango.Emulator.Bus.Tests/DevicePageIdTests.cs)
checks that encoding. No production caller maps a guest I/O page from it,
and it is not `IDevicePageObject`. The encoding belongs to
[#548](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/548).

The partly implemented overlap is the IIe card, already in Devices:
[`Extended80ColumnDevice`](../../src/BadMango.Emulator.Devices/Extended80ColumnDevice.cs)
writes $C006/$C007 through
[`IInternalRomHandler`](../../src/BadMango.Emulator.Bus/Interfaces/IInternalRomHandler.cs)
on
[`CompositeIOTarget`](../../src/BadMango.Emulator.Bus/CompositeIOTarget.cs).
[`Extended80ColumnDeviceTests`](../../tests/BadMango.Emulator.Devices.Tests/Extended80ColumnDeviceTests.cs)
and
[`CompositeIOTargetTests`](../../tests/BadMango.Emulator.Bus.Tests/CompositeIOTargetTests.cs)
cover that IIe overlay. No test registers an IIc personality.

## 7.8 Apple IIc Differences — Aspirational

No profile under `profiles/` is an IIc, and no `AppleIIc` type exists. The
v1.0 Pocket2c sketch is not a machine builder. The profiles on disk are
`pocket2e.json`, `pocket2e-a2.json`, `pocket2e-a2-enh.json`,
`pocket2e-devices.json`, `pocket2e-lite.json`, `pocket2e-min.json`,
`simple-65c02.json`, and `simple-65c02-with-rom.json`.

Source:
[§7.8](../Architecture%20Spec%20v1.0%20Part%202.md#78-apple-iic-differences),
[§7.8.1](../Architecture%20Spec%20v1.0%20Part%202.md#781-no-physical-slots),
[§7.8.2](../Architecture%20Spec%20v1.0%20Part%202.md#782-memory-mapped-rom-selection),
[§7.8.3](../Architecture%20Spec%20v1.0%20Part%202.md#783-firmware-switching),
[§7.8.4](../Architecture%20Spec%20v1.0%20Part%202.md#784-emulation-implications).

### 7.8.1 No Physical Slots — Aspirational

The sketch replaces external slots with a built-in Disk II (as slot 6),
serial ports (as slots 1 and 2), a mouse (as slot 4), and memory expansion
(as slot 5). Firmware would still answer a probe at the slot ROM address.
`SlotManager` always allocates seven slots and installs cards into them.
[`DiskIIController`](../../src/BadMango.Emulator.Devices/DiskIIController.cs)
is an IIe slot card. There is no slotless IIc wiring.

### 7.8.2 Memory-Mapped ROM Selection — Aspirational

The sketch places internal firmware at $C100, $C200, $C400, $C500, and
$C600, and reuses IIe expansion-ROM selection for $C800-$CFFF. No built-in
serial, mouse, or memory-expansion ROM window exists. Slot ROM contents are
whatever card the slot manager installed.

### 7.8.3 Firmware Switching — Partly implemented

The INTCXROM pair at $C006 (SETSLOTCX) and $C007 (SETINTCX) is implemented
for the IIe 80-column card. `Extended80ColumnDevice` drives `IInternalRomHandler` on
`CompositeIOTarget`. That overlay is not an IIc firmware-image switch: the
tree does not select among IIc ROM images. The switch table is on the
[devices page](devices.md#774-internal-rom-soft-switches--implemented).

### 7.8.4 Emulation Implications — Aspirational

`RegisterIIcTraps` does not exist. The sketch's always-present disk and
serial trap entries are not registered, and there is no Pocket2c path that
skips slot checks. Trap registration itself is
[#546](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/546).

## 8.1 Compatibility Context — Aspirational

The v1.0 sketch defines `CompatibilityPersonality` (`None`, `AppleIIe`,
`AppleIIc`, `AppleIIgs`) and a `CompatibilityController` whose COMPATID
value would map a 64 KB guest window: RAM, the $C000 I/O page, and ROM.
None of that exists. `SystemRegisters` has no COMPATID field. `Cpu65832`
does not step, so it cannot enter a personality. Pocket2e is not that guest
window. The privileged draft still describes the register at
[COMPATID system register (K-only)](../../reference/Privileged%20architecture%20spec%20v0.1%20for%2065832.md#compatid-system-register-k-only).

Source sketch:
[§8.1 Compatibility Context](../Architecture%20Spec%20v1.0%20Part%202.md#81-compatibility-context).

## 8.2 Device Page Object Model — Aspirational

`IDevicePageObject` is not in the tree. The sketch's per-guest object, with
`Read8`, `Write8`, and `RejectAccess`, is not how devices attach. A device
implements `IBusTarget` or registers read and write handlers on
`IOPageDispatcher`. Pocket2e has one shared I/O page. Guests do not each
receive an instance.

`DevicePageClass.CompatIO` only names an identifier class. It is not this
object model.

Source sketch:
[§8.2 Device Page Object Model](../Architecture%20Spec%20v1.0%20Part%202.md#82-device-page-object-model).

## Related

- [Devices and soft switches](devices.md)
- [CPU core](cpu.md)
- [Bus and MMU](bus-mmu.md)
- [Architecture index](../../ARCHITECTURE.md)