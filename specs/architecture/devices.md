# Devices and Soft Switches

## Summary

This page describes the $C000-$CFFF I/O page, the soft-switches and
peripherals registered in it, the seven expansion slots, and the composite
target that routes accesses across the page. The $C000-$C0FF region contains
side-effecting device registers and status reads; $C100-$C7FF contains slot
ROM; and $C800-$CFFF contains one selected expansion ROM or internal-ROM
overlay.

**Status legend:** **Implemented** means the audited behavior has working
code in the current tree. **Partly implemented** means the core or most
addresses work, but one or more behavior ranges are missing or inaccurate.
**Incomplete** means some addresses in the named range are implemented while
others are still missing. **Aspirational** means no implementation of the
described behavior exists. **Superseded** means older specification text is retained only as
historical context and must not override the current implementation or
linked normative specification.

## Covers

- The historical memory-mapped I/O and soft-switch model (§1.3–§1.3.4).
- The I/O page layout and read/write dispatch (§7.1–§7.1.3).
- Soft-switch sub-regions, the language card, slot I/O, and the peripheral
  interface (§7.2–§7.4).
- The I/O-device portion of the Pocket2e memory map (§7.5).
- `CompositeIOTarget`, `IOPageDispatcher`, slot handling, and internal-ROM
  soft switches (§7.6 and §7.7.4).
- The audited soft-switch summary ranges in Appendix A.3.

## Does not cover

- CPU and MMU internals: separate architecture work is tracked for
  [CPU](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/539)
  and
  [bus/MMU](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/542).
- Expansion-ROM selection lifecycle and the trap registry: separate work is
  tracked in
  [#546](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/546).
  This page only identifies the overlap with the composite I/O target.
- Full machine/profile assembly: separate work is tracked in
  [#549](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/549).
- IIc-specific firmware personalities: separate work is tracked in
  [#547](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/547).
- Video rendering, character generation, disk media formats, and storage
  implementations belong to their separate Rendering or Storage pages and
  specifications.

## Target layout

The target layout is defined by
[`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md)
and issue
[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478).

Peripheral, card, and device implementations—including the keyboard, video,
game I/O, language card, Disk II controller, clocks, character device, and
speaker/audio—target `BadMango.Emulator.Devices`, retaining the existing
`BadMango.Emulator.Devices` namespace.

I/O-page bus plumbing targets `BadMango.Emulator.Core`, retaining the existing
`BadMango.Emulator.Bus` namespace. This includes `IOPageDispatcher`,
`CompositeIOTarget`, `SlotManager`, and the slot/peripheral interfaces.
Machine/profile composition remains in `BadMango.Emulator.Systems`.

> **Current location (until #565):**
> `tests/BadMango.Emulator.Tests`; target: subject-specific test projects
> (namespace unchanged).

The storage assembly remains separate for disk and media implementations.
The Rendering assembly remains separate for display-pipeline output. Speaker
audio remains in Devices.

## 1.3 Historical context — Implemented

### 1.3.1 Memory-mapped I/O — Implemented

The Apple II has no separate I/O instruction space: the CPU communicates with
hardware through addresses. The $C000-$CFFF page is therefore routed to
memory-mapped device and ROM targets rather than ordinary RAM.

The current plumbing is `IOPageDispatcher` for $C000-$C0FF and
`CompositeIOTarget` for the complete $C000-$CFFF page. Devices register read
and write handlers instead of exposing array-backed I/O bytes.

### 1.3.2 Soft-switch concept — Implemented

A soft switch is state controlled by access to a memory-mapped address. Reads,
writes, or both may have side effects. `ISoftSwitchProvider` supplies switch
names and registration data, while `IOPageDispatcher` invokes registered
handlers on read and write.

For example, `VideoDevice` registers $C050-$C051 as any-access text/graphics
toggles. A read from a switch can therefore have a side effect even when its
returned value is not meaningful.

### 1.3.3 Floating bus — Partly implemented

Unregistered soft-switch reads currently return `0xFF`. Unregistered writes are
ignored.

This does not yet model the Apple II's last-bus or video-fetch open-bus
behavior. Such a model is not a valid substitute for the implemented $FF
fallback.

### 1.3.4 Seven slots — Implemented

Slots 1-7 provide 16 bytes of I/O at $C090-$C0FF and 256 bytes of slot ROM at
$Cn00-$CnFF. Slot 0 is reserved for the language-card/I/O region at
$C080-$C08F and is not one of the seven expansion-slot entries.
`SlotManager.SlotCount` is 7, as specified for the built Pocket2e layout.

## 7.1 The Apple II I/O page — Implemented

### 7.1.1 Layout — Implemented

The $Cxxx page is split by address:

| Range | Purpose | Implementation status |
|---|---|---|
| $C000-$C0FF | Soft switches and slot 0-7 I/O | Implemented; two ranges have gaps listed below |
| $C100-$C7FF | Per-slot 256-byte ROM regions | Implemented dispatch; normal device ROM contents vary |
| $C800-$CFFF | Shared expansion ROM | Implemented selection and default-ROM layering |

`CompositeIOTarget` dispatches the three regions and masks offsets to $0FFF.
The lower half of the page is side-effecting, and ROM accesses participate in
slot selection.

### 7.1.2 Selection protocol — Implemented

Access to a $Cn00-$CnFF slot-ROM address selects slot n for the shared
$C800-$CFFF expansion-ROM region. An access to $CFFF deselects the active slot
and restores the default expansion-ROM layer. The protocol runs for reads and
writes at the bus-target boundary.

### 7.1.3 Read versus write behavior — Implemented

`IOPageDispatcher` uses separate read and write handler delegates. A missing
read returns $FF; a missing write is a no-op. ROM regions ignore writes, but
address snooping still participates in slot selection where that behavior is
required.

## 7.2 Soft-switch sub-regions — Implemented

### 7.2.1 System switches ($C000-$C07F) — Partly implemented

The built-in dispatcher registers:

- Keyboard data and strobe: $C000 and $C010.
- Speaker toggle: $C030.
- Graphics and annunciator switches: $C050-$C05F.
- Pushbuttons: $C060-$C063.
- Paddle reads: $C064-$C067.
- Paddle trigger mirrors: $C070-$C07F.
- Apple IIe auxiliary-memory, status, and character switches in their
  respective devices.

Missing behavior is explicit:

- Cassette output at $C020: **Aspirational**, no cassette device.
- $C040 game I/O strobes: **Aspirational**, no $C040 handler.
- $C068-$C06F: **No handler**.
- Speaker mirrors $C031-$C03F: **Incomplete**; only $C030 is registered.

The same status is retained in Appendix A.3 below.

### 7.2.2 Language card switches — Implemented

Slot 0's $C080-$C08F range is owned by `LanguageCardDevice`. It changes ROM/RAM
read source, write enable, and bank selection. Odd-address write enable uses
the two-consecutive-read R×2 protocol. Aliases at $C084-$C087 and
$C08C-$C08F implement the corresponding bank encodings.

Writes to the language-card switch range do not themselves enable the bank
through this switch; the read-based protocol and implemented remap methods
control it.

#### 7.2.2.1 Language card and CPU vectors — Implemented

When language-card RAM is selected for reading, the $D000-$FFFF range is bus-
routed to language-card RAM, including vector addresses. There is no CPU-
specific vector exception. The 65C02 reads $FFFA/$FFFC/$FFFE through the
normal bus path.

### 7.2.3 Slot-device I/O — Implemented

Slots 1-7 have 16-byte MMIO ranges:

| Slot | Range |
|---|---|
| 1 | $C090-$C09F |
| 2 | $C0A0-$C0AF |
| 3 | $C0B0-$C0BF |
| 4 | $C0C0-$C0CF |
| 5 | $C0D0-$C0DF |
| 6 | $C0E0-$C0EF |
| 7 | $C0F0-$C0FF |

`SlotManager.InstallSlotHandlers` registers a card's `SlotIOHandlers` with
`IOPageDispatcher`. Empty or unwired offsets retain the $FF read/no-op write
behavior.

## 7.3 Peripheral interface — Implemented

The slot-card seam uses `ISlotCard`, and the shared device contract uses
`IPeripheral` and `IMotherboardDevice`. Peripherals can expose MMIO handlers,
slot ROM, and expansion ROM; reset and initialization participate in machine
device lifecycle.

The concrete target layout does not imply that every historical peripheral
card is implemented. Card implementations in the tree include Disk II and
clock/Thunderclock families, and other slot cards remain profile-dependent.

## 7.4 Slot manager — Implemented

`SlotManager` is the routing owner for:

- installation and removal of cards in slots 1-7;
- registration of slot I/O handlers;
- slot-ROM access and expansion-ROM selection;
- the default $C800-$CFFF ROM under selected slot ROM; and
- reset/deselection state.

Slot 0 is not in `SlotManager`'s seven-card array.

## 7.5 I/O portion of the Pocket2e memory map — Partly implemented

The built Pocket2e profile maps the I/O page to the composite target. Main,
auxiliary, language-card, slot-ROM, and expansion-ROM routing is bus-wired by
`Pocket2eMachineBuilderExtensions`.

The complete address map, including RAM, ROM, auxiliary memory, and profile
assembly, is outside this page; it belongs to the machine architecture work
tracked in
[#549](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/549).
The implemented Pocket2e map is the built 128KB machine. PocketGS and PocketME
maps are not built.

## 7.6 Composite I/O page handler — Implemented

`CompositeIOTarget` is the composite bus target for $C000-$CFFF. It routes:

- $C000-$C0FF to `IOPageDispatcher`;
- $C100-$C7FF to slot-ROM targets and `SlotManager` selection; and
- $C800-$CFFF to the selected expansion-ROM target or default/internal ROM.

It also carries the IIe INTCXROM, INTC3ROM, and INTC8ROM overlays. Internal
ROM and slot-ROM routing are resolved through `IInternalRomHandler`.

## 7.7.4 Internal ROM soft switches — Implemented

The Apple IIe internal-ROM controls are implemented on
`Extended80ColumnDevice`:

| Address | Function |
|---|---|
| $C006 | SETSLOTCX: peripheral slot ROM visible |
| $C007 | SETINTCX: internal ROM overlay visible |
| $C00A | SETINTC3: internal C3 ROM enabled |
| $C00B | SETSLOTC3: slot 3 ROM enabled |

`CompositeIOTarget` carries INTCXROM and INTC3ROM state and the INTC8ROM
access latch. This is IIe 80-column card behavior, not a claim of a built IIc
personality.

## Appendix A.3 Soft-switch summary — Partly implemented

The following twelve ranges are the normative summary from the architecture
appendix. Status names below match the audit.

### A.3 / Keyboard ($C000-$C01F) — Partly implemented

$C000 owns keyboard data and $C010 clears the keyboard strobe in
`AppleIIeKeyboard`. Apple IIe status reads $C013-$C01F are split across
auxiliary-memory, video, and character devices.

### A.3 / Cassette ($C020-$C02F) — Aspirational

No cassette-output or cassette-input device is implemented. Unregistered I/O
reads return $FF and writes are no-op.

### A.3 / Speaker ($C030-$C03F) — Partly implemented

`SpeakerController` implements the any-access toggle at $C030. **Speaker
mirrors $C031-$C03F are incomplete**; they are not currently registered.

### A.3 / Utility strobes ($C040-$C04F) — Aspirational

No $C040 game-I/O strobe handler is implemented. **$C040 is unimplemented**.
Unregistered reads return $FF and writes are ignored.

### A.3 / Graphics mode switches ($C050-$C057) — Implemented

`VideoDevice` implements $C050-$C057: text/graphics, mixed/full, page 1/page
2, and lo-res/hi-res. Both reads and writes trigger the same switch behavior.

### A.3 / Annunciator outputs ($C058-$C05F) — Implemented

`VideoDevice` implements $C058-$C05F for AN0-AN3. $C05E and $C05F also provide
the implemented double-hi-res side of the annunciator 3 pair.

### A.3 / Pushbutton and joystick ($C060-$C06F) — Partly implemented

`GameIOController` implements pushbuttons $C060-$C063 and paddle reads
$C064-$C067. $C068-$C06F has no handler.

### A.3 / Paddle trigger ($C070-$C07F) — Implemented

Any access in $C070-$C07F invokes `TriggerPaddles`. Paddle timing uses
`CyclesPerPaddleUnit = 11`.

### A.3 / Language card and bank switching ($C080-$C08F) — Implemented

The 16 switch addresses control ROM/RAM read source, write enable, and language-
card bank 1 or bank 2. Odd-address write enable requires the R×2 double-read
protocol. Bus remapping swaps $D000-$FFFF accordingly.

### A.3 / Slot I/O ($C090-$C0FF) — Implemented

`SlotManager` installs each card's 16-byte I/O range. Empty slots continue to
read $FF and ignore writes.

### A.3 / Apple IIe auxiliary-memory switches ($C000-$C00F) — Implemented

`Extended80ColumnDevice` owns $C000-$C00D for 80STORE, RAMRD, RAMWRT, INTCXROM,
ALTZP, SLOTC3ROM, and 80COL. `CharacterDevice` owns $C00E/$C00F for alternate
character-set switching. `AuxiliaryMemoryController` owns the main/auxiliary
bank routing.

### A.3 / Apple IIe status reads ($C011-$C01F) — Partly implemented

$C013-$C018 are implemented on `Extended80ColumnDevice`; $C019-$C01D and
$C01F are implemented on `VideoDevice`; $C01E is implemented on
`CharacterDevice`.

**$C011 and $C012 remain incomplete**: `StatusFlag.RDLCBNK2` and
`RDLCRAM` are declared, but no dispatcher registration implements them.

## Code paths

The primary audited implementation paths are:

- [IOPageDispatcher](../../src/BadMango.Emulator.Core/IOPageDispatcher.cs)
- [CompositeIOTarget](../../src/BadMango.Emulator.Core/CompositeIOTarget.cs)
- [SlotManager](../../src/BadMango.Emulator.Core/SlotManager.cs)
- [ISlotCard](../../src/BadMango.Emulator.Core/Interfaces/ISlotCard.cs)
- [IPeripheral](../../src/BadMango.Emulator.Core/Interfaces/IPeripheral.cs)
- [AppleIIeKeyboard](../../src/BadMango.Emulator.Devices/AppleIIeKeyboard.cs)
- [Extended80ColumnDevice](../../src/BadMango.Emulator.Devices/Extended80ColumnDevice.cs)
- [LanguageCardDevice](../../src/BadMango.Emulator.Devices/LanguageCardDevice.cs)
- [VideoDevice](../../src/BadMango.Emulator.Devices/VideoDevice.cs)
- [SpeakerController](../../src/BadMango.Emulator.Devices/Speaker/SpeakerController.cs)
- [GameIOController](../../src/BadMango.Emulator.Devices/GameIOController.cs)
- [DiskIIController](../../src/BadMango.Emulator.Devices/DiskIIController.cs)

## Source and test pointers

Target homes are listed above.

- Bus I/O-page types and slot types live in
  `src/BadMango.Emulator.Core` (namespace `BadMango.Emulator.Bus`).
- Peripherals and soft-switch devices remain under
  `src/BadMango.Emulator.Devices`; that is a surviving target assembly
  and keeps the existing namespaces.
- Bus tests live in `tests/BadMango.Emulator.Core.Tests` (namespace
  `BadMango.Emulator.Bus.Tests`).
- Cross-suite device/machine tests remain under
  `tests/BadMango.Emulator.Tests`; use the #565 blockquote for their target
  split.

Current test areas include `IOPageDispatcherTests`,
`CompositeIOTargetTests`, `SlotManagerTests`, `LanguageCardDeviceTests`,
`Extended80ColumnDeviceTests`, `SpeakerControllerTests`, and
`GameIOControllerTests`. The bus tests are in `BadMango.Emulator.Core.Tests`.
Device tests stay in `BadMango.Emulator.Devices.Tests`. Cross-suite tests
remain subject to #565.
