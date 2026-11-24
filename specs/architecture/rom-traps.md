# Expansion ROM and traps

## Summary

The registry is empty in a built machine. `MachineBuilder.Build` installs a
`TrapRegistry` and registers no handler. No type under `src/` registers a
monitor, Applesoft, slot-ROM, or expansion-ROM trap. Profiles that use the
`composite-io` handler, such as
[`profiles/pocket2e-a2-enh.json`](../../profiles/pocket2e-a2-enh.json), declare
ROM images and do not register traps.

Expansion-ROM banking is separate and does run. A read or write in
`$C100`-`$C7FF` selects a slot, and an access to `$CFFF` deselects it so the
default expansion ROM shows through again.

The 65C02 consults the registry on instruction fetch for call traps. Data
reads and writes through `MainBus` invoke read and write traps. `Cpu65816`
and `Cpu65832` do not step.

**Status legend:** **Implemented** means the audited behavior has working
code in the current tree. **Partly implemented** means the mechanism is
present and a named part is missing or differs from the v1.0 text.
**Aspirational** means no implementation of the described behavior exists.
Markers on the moved sections match the
[#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
audit in
[`Architecture Docs Restructure Proposal.md`](../Architecture%20Docs%20Restructure%20Proposal.md).
Where that audit and
[`Architecture Spec v1.0 Part 2`](../Architecture%20Spec%20v1.0%20Part%202.md)
disagree, this page follows the code and the audit.

## Covers

- Expansion-ROM selection, deselection, and the default ROM underneath
  (§7.7–§7.7.3).
- The trap registry, its memory contexts, and the 65C02 fetch check
  (§8.3–§8.3.12), including the empty registry in a built machine.

## Does not cover

- The I/O page, soft switches, slot I/O, and the internal-ROM switch
  addresses. Those are on [Devices and soft switches](devices.md), including
  [§7.7.4](devices.md#774-internal-rom-soft-switches--implemented).
- Page-table routing outside this window. See [Bus and MMU](bus-mmu.md),
  including
  [composite page dispatch](bus-mmu.md#gap-1-composite-page-dispatch--status-implemented).
- The 65C02 instruction set beyond the fetch-time trap check. See
  [CPU core](cpu.md#65c02-implementation).
- Apple IIc firmware personalities and 65832 compatibility contexts, tracked
  in
  [#547](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/547).
- Profile assembly and boot, tracked in
  [#549](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/549).
- 65816 and 65832 execution, tracked in
  [#540](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/540)
  and
  [#541](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541).

## Code paths

Trap registration and expansion-ROM bus plumbing target
`BadMango.Emulator.Core` and keep the `BadMango.Emulator.Bus` and
`BadMango.Emulator.Bus.Interfaces` namespaces. Slot cards and device ROMs
target `BadMango.Emulator.Devices` and keep the `BadMango.Emulator.Devices`
namespace. Pocket2e builder extensions stay in `BadMango.Emulator.Systems`.

> **Current location (until #562):** `src/BadMango.Emulator.Bus`; target:
> `src/BadMango.Emulator.Core` (namespace `BadMango.Emulator.Bus` unchanged).

- [`TrapRegistry`](../../src/BadMango.Emulator.Bus/TrapRegistry.cs) and
  [`ITrapRegistry`](../../src/BadMango.Emulator.Bus/Interfaces/ITrapRegistry.cs)
  store handlers.
  [`NullTrapRegistry`](../../src/BadMango.Emulator.Bus/NullTrapRegistry.cs) is
  what `Cpu65C02` holds until a concrete registry is assigned.
- [`SlotManager`](../../src/BadMango.Emulator.Bus/SlotManager.cs) tracks slots
  1–7 and `ActiveExpansionSlot`.
- [`CompositeIOTarget`](../../src/BadMango.Emulator.Bus/CompositeIOTarget.cs)
  selects and deselects expansion ROM. It implements
  [`IInternalRomHandler`](../../src/BadMango.Emulator.Bus/Interfaces/IInternalRomHandler.cs).
- [`MemoryContexts`](../../src/BadMango.Emulator.Bus/MemoryContexts.cs) names
  ROM, language-card RAM, auxiliary RAM, and custom contexts.
- [`Cpu65C02`](../../src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs) calls
  `ContainsAddress`, then `TryExecute`, on fetch.
- [`MachineBuilder`](../../src/BadMango.Emulator.Bus/MachineBuilder.cs)
  constructs the empty registry in `Build`.
- [`Pocket2eMachineBuilderExtensions`](../../src/BadMango.Emulator.Systems/Pocket2eMachineBuilderExtensions.cs)
  supplies `WithStubRom`.
- [`DiskIIController`](../../src/BadMango.Emulator.Devices/DiskIIController.cs),
  [`DiskIIBootRom`](../../src/BadMango.Emulator.Devices/DiskIIBootRom.cs),
  [`DiskIIBootRomStub`](../../src/BadMango.Emulator.Devices/DiskIIBootRomStub.cs),
  and
  [`DiskIIExpansionRomStub`](../../src/BadMango.Emulator.Devices/DiskIIExpansionRomStub.cs)
  are card ROM targets.
- [`MachineFactory`](../../src/BadMango.Emulator.Debug.Infrastructure/MachineFactory.cs)
  assigns the component onto `Cpu65C02`.
  [`BreakpointManager`](../../src/BadMango.Emulator.Debug.Infrastructure/BreakpointManager.cs)
  can register debugger call traps after the machine exists.

> **Current location (until #562):**
> `src/BadMango.Emulator.Emulation/Cpu/Cpu65C02.cs`; target:
> `src/BadMango.Emulator.Core/Cpu/` (namespace `BadMango.Emulator.Emulation.Cpu`
> unchanged).

`Build` adds an empty `TrapRegistry` and assigns that same instance to a
`Cpu65C02`. `NullTrapRegistry` remains the default for a CPU that was not
built by `MachineBuilder`. `NullTrapRegistry.Register` throws
`NotSupportedException`. `MachineFactory.WireTrapRegistry` assigns the same
component again for the debug hosts.

## Target layout

Target assemblies come from
[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
and
[`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md).
[#477](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/477)
has not folded Bus or Emulation into Core. Namespaces stay as they are. The
target layout does not put handlers into the registry.

| Concern | Target assembly | Namespace |
| --- | --- | --- |
| Trap registry, slot manager, composite I/O target | `BadMango.Emulator.Core` | `BadMango.Emulator.Bus`, `BadMango.Emulator.Bus.Interfaces` |
| 65C02 fetch check | `BadMango.Emulator.Core` | `BadMango.Emulator.Emulation.Cpu` |
| Slot cards and device ROMs | `BadMango.Emulator.Devices` | `BadMango.Emulator.Devices` |
| Pocket2e builder extensions, including `WithStubRom` | `BadMango.Emulator.Systems` | `BadMango.Emulator.Systems` |
| Debugger wiring and breakpoint traps | `BadMango.Emulator.Debug.Infrastructure` | `BadMango.Emulator.Debug.Infrastructure` |

`BadMango.Emulator.Devices`, `BadMango.Emulator.Systems`, and
`BadMango.Emulator.Debug.Infrastructure` are already the surviving
assemblies.

## Sections

- [§7.7 Expansion ROM lifecycle](#77-expansion-rom-lifecycle--implemented)
- [§7.7.1 Selection triggers](#771-selection-triggers--implemented)
- [§7.7.2 Deselection trigger](#772-deselection-trigger--implemented)
- [§7.7.3 Trap handler implications](#773-trap-handler-implications--partly-implemented)
- [§8.3 ROM routine interception](#83-rom-routine-interception--partly-implemented)
- [§8.3.1 Why trap ROM routines?](#831-why-trap-rom-routines--partly-implemented)
- [§8.3.2 The trap mechanism](#832-the-trap-mechanism--implemented)
- [§8.3.3 ROM address space classification](#833-rom-address-space-classification--partly-implemented)
- [§8.3.4 Fixed ROM traps](#834-fixed-rom-traps--partly-implemented)
- [§8.3.5 Slot ROM traps](#835-slot-rom-traps--partly-implemented)
- [§8.3.6 Expansion ROM traps](#836-expansion-rom-traps--partly-implemented)
- [§8.3.7 Trap registry lifecycle](#837-trap-registry-lifecycle--implemented)
- [§8.3.8 Handling the $CFFF access](#838-handling-the-cfff-access--implemented)
- [§8.3.9 Complete trap registry interface](#839-complete-trap-registry-interface--implemented)
- [§8.3.10 Well-known trap points](#8310-well-known-trap-points--aspirational)
- [§8.3.11 CPU integration](#8311-cpu-integration--partly-implemented)
- [§8.3.12 ROM-free operation mode](#8312-rom-free-operation-mode--partly-implemented)

## 7.7 Expansion ROM lifecycle — Implemented

`SlotManager` keeps seven slots, numbered 1–7. Slot 0 is the language card
and is outside that array. `ActiveExpansionSlot` is the slot whose expansion
ROM is layered at `$C800`-`$CFFF`. When it is null, `GetVisibleExpansionRom`
returns the target installed with `SetDefaultExpansionRom`.

`Extended80ColumnDevice` installs that default during initialization, and it
registers the same image as slot 3's internal expansion ROM. A selected
slot's `ExpansionROMRegion` layers on top. `SelectExpansionSlot` notifies the
previous card through `OnExpansionROMDeselected` when the slot number
changes, then notifies the new card through `OnExpansionROMSelected`.
`DiskIIController.OnExpansionROMSelected` is empty. The card's expansion
image is a passive ROM target.

### 7.7.1 Selection triggers — Implemented

`CompositeIOTarget` selects the slot from the address on both reads and
writes of `$C100`-`$C7FF`. The slot number is `(offset >> 8) & 7`. Slot 0 is
ignored. A data read such as `LDA $C600`, an instruction fetch such as
`JSR $C600`, and an indirect read whose address falls in a slot ROM all use
that path.

`ReadSlotRom` and `WriteSlotRom` call `SelectExpansionSlot` directly.
`SlotManager.HandleSlotROMAccess` performs the same selection. The composite
target does not call it.

Internal-ROM overlays change the trigger. The switch addresses are on the
devices page.

- While `INTCXROM` is on and an internal ROM image is installed, `$C100`-`$C7FF`
  reads come from that image and do not select a slot. A `$C3xx` access that
  also has `INTC3ROM` on still arms the `INTC8ROM` latch before that return.
- `INTC3ROM` without `INTCXROM` still selects slot 3 on `$C3xx` and returns
  internal ROM bytes. The write path selects slot 3 and does not forward the
  write to a card.
- Any other slot read selects the slot, then returns the card's `ROMRegion`
  when the card published one. A card with no slot ROM falls through to the
  internal CXROM image when one is installed. That is how a Disk II card
  without a P5A image still executes motherboard slot firmware. The read is
  `$FF` when no internal image is installed.
- A slot-ROM write selects the slot and is forwarded only when the card
  published a `ROMRegion`. `INTCXROM` drops the write.

### 7.7.2 Deselection trigger — Implemented

`ReadExpansionRom` and `WriteExpansionRom` call `DeselectExpansionSlot` when
the page offset is `$xFFF`. The read also clears the `INTC8ROM` latch. After
that, a read with `INTCXROM` on returns the internal ROM byte. Otherwise the
read returns the default expansion ROM from `GetVisibleExpansionRom`. The
byte is `$FF` when neither image is present. A write to `$CFFF` deselects
and does not store the value.

`SelectExpansionSlot` replaces `ActiveExpansionSlot`. `Reset` and
`OnResetAsserted` deselect as well. Removing the card that owns the active
slot clears the selection. The v1.0 text describes `$CFFF` as the only
deselect and describes the window as returning to the floating bus. The live
path also clears the selection on reset and on removal of the active card,
and the byte under the layer is the default ROM.

### 7.7.3 Trap handler implications — Partly implemented

`TrapRegistry.CheckContext` skips a slot-dependent trap when the slot has no
card, when `RequiresExpansionRom` is set and `ActiveExpansionSlot` is a
different slot, or when `IInternalRomHandler` reports `IsIntCxRomEnabled`,
`IsIntC3RomEnabled` on `$C300`-`$C3FF`, or `IsIntC8RomEnabled` on
`$C800`-`$CFFF`. Those checks run for entries created with
`RegisterSlotDependent`. A plain `Register` call does not set a slot number,
so the overlay and expansion-slot checks do not apply to it.

`Build` constructs the registry after copying pre-registered components onto
the machine, and before device initialization. It passes the slot manager,
language-card state, and internal-ROM handler it can resolve, and it passes
a null context resolver. It registers no traps. With an empty registry, a
normal boot has nothing for those checks to suppress.

`WithLanguageCard` and `WithSlotManager` add their components before `Build`,
so an `AsPocket2e` machine resolves both. `IInternalRomHandler` depends on
the build path. A profile whose I/O region uses the `composite-io` handler
adds `CompositeIOTarget` as that component during memory configuration,
which is before the registry is constructed. `WithPocket2eIOPage` creates
the same target in a `BeforeDeviceInit` callback, which runs after
construction, and that callback does not add the target to the component
bag. The programmatic Pocket2e registry therefore keeps a null internal-ROM
handler.

The v1.0 `DiskIIExpansionTrap` sample is not a type in the tree.

## 8.3 ROM routine interception — Partly implemented

The registry and the 65C02 fetch check exist. A built machine registers no
traps. [`profiles/pocket2e-a2-enh.json`](../../profiles/pocket2e-a2-enh.json)
and the other `composite-io` profiles under `profiles/` name `memory.rom-images`.
They do not name trap handlers. [`profiles/pocket2e.json`](../../profiles/pocket2e.json)
still names its I/O handler `pocket2e-io`. `MachineFactory` registers
`composite-io` only, and no `Pocket2eIOPage` type is in the tree, so that
profile's composite region has no factory.

### 8.3.1 Why trap ROM routines? — Partly implemented

The v1.0 section lists copyright avoidance, speed, extra behavior, ROM-free
operation, and diagnostics as reasons to replace ROM calls. The code
provides registration and a fetch check so a handler can replace a call.
Shipping profiles load ROM images and leave the registry empty, so a built
machine runs the ROM image.

### 8.3.2 The trap mechanism — Implemented

`TrapOperation` has `Read`, `Write`, and `Call`. `Register` without an
operation stores a `Call` trap in `MemoryContexts.Rom`.
`TryExecute(address, operation, ...)` looks up
`(address, operation, active context)` and returns `TrapResult.NotHandled`
when the entry is missing, disabled, in a disabled category, or rejected by
`CheckContext`.

`TrapResult` is `(Handled, CyclesConsumed, ReturnMethod, ReturnAddress)`.
`ReturnMethod` is `Rts`, `Rti`, or `None`. `TrapResult.Success(cycles)` uses
`Rts`. `SuccessInterrupt` uses `Rti`. `SuccessWithRedirect` uses `None` and
a return address. `NotHandled` uses `None`.

The v1.0 `TrapResult` sketch has no `ReturnMethod` and treats a null return
address as an implied RTS. The live CPU performs RTS only when
`ReturnMethod` is `Rts`.

`TrapHandler` is `TrapResult (ICpu cpu, IMemoryBus bus, IEventContext context)`.

The only production caller of `TryExecute` is `Cpu65C02.Step`, and it calls
the `Call` overload. No bus read or write path calls `TryExecute` with
`Read` or `Write`. A read or write registration is stored and is not fired
by memory access.

### 8.3.3 ROM address space classification — Partly implemented

`MemoryContexts` defines `Rom`, `LanguageCardRam`, `LanguageCardBank1`,
`LanguageCardBank2`, `MainRam`, `AuxiliaryRam`, `ProDosRamDisk`, and
`Custom(id)`.

`Cpu65C02` decides whether to call `TryExecute` with `ContainsAddress`. That
test is an address-index lookup. It does not read the active memory context.
Context is resolved inside `TryExecute`.

With the null resolver installed by `MachineBuilder`, `ResolveMemoryContext`
returns `LanguageCardRam` when the address is at least `$D000` and
`ILanguageCardState.IsRamReadEnabled` is true. Every other address resolves
to `Rom`. The default resolver never returns auxiliary RAM, main RAM, a
language-card bank id, or a custom id. Those contexts can be stored with
`RegisterWithContext`. A built machine's resolver does not select them.

| Range | Live classification | Built machine |
| --- | --- | --- |
| `$D000`-`$FFFF` | `Rom`, or `LanguageCardRam` while language-card RAM is the read source | No trap is registered |
| `$Cn00`-`$CnFF` | Slot-dependent only when `RegisterSlotDependent` stored it | No `$Cn00` handler is registered |
| `$C800`-`$CFFF` | `RequiresExpansionRom` when `RegisterSlotDependent` stored it | No expansion-ROM trap is registered |
| Other named contexts | Present on `MemoryContexts` | The built resolver does not select them |

The v1.0 table calls `$D000`-`$F7FF` and `$F800`-`$FFFF` unconditionally safe
to trap. The live lookup for `$D000`-`$FFFF` follows the language-card read
source, as the next section describes.

### 8.3.4 Fixed ROM traps — Partly implemented

`Register(address, name, category, handler, description)` stores one call
trap at a fixed address in the ROM context. `RegisterLanguageCardRam` stores
the same shape in `MemoryContexts.LanguageCardRam`.

When language-card RAM is the read source, `TryExecute` looks up the
language-card context and does not run a ROM-context trap at that address.
The v1.0 text chooses to ignore language-card state for Pocket2e. The code
checks it.

No `Register` call site in `src/` names a monitor or Applesoft entry.
`TrapCategory.MonitorRom` and `TrapCategory.BasicInterpreter` exist and are
unused by production registration.

### 8.3.5 Slot ROM traps — Partly implemented

`RegisterSlotDependent` stores one ROM-context trap with a slot number from
1 to 7. `UnregisterSlotTraps` removes every entry whose `SlotNumber`
matches. No caller registers a `$Cn00` handler.

For a slot-dependent entry outside `$C800`-`$CFFF`, `CheckContext` requires
a slot manager and a card in that slot, and it applies the internal-ROM
overlay check. It does not require `ActiveExpansionSlot` to equal the slot,
and it does not compare `DeviceType` with `"DiskII"`. The v1.0 sample's
device-type test is not what the registry does.

A handled call trap returns before `Cpu65C02` fetches the opcode, so the
composite target does not see that fetch and does not run
`SelectExpansionSlot` for it. No production handler selects a slot in its
place. The registry is empty, so a built machine still selects through the
bus when a fetch actually reads a slot ROM byte.

The dictionary key is `(address, operation, MemoryContexts.Rom)`. The slot
number is metadata on that one entry. A second registration for the same
address and operation, including `Register` and `RegisterSlotDependent` for
different slots, throws `InvalidOperationException`.

### 8.3.6 Expansion ROM traps — Partly implemented

`RegisterSlotDependent` sets `RequiresExpansionRom` when the address is in
`$C800`-`$CFFF` inclusive. `CheckContext` then requires
`ActiveExpansionSlot` to equal the entry's slot, after the card-present and
internal-ROM checks. `IsIntC8RomEnabled` suppresses that window, and
`IsIntCxRomEnabled` suppresses `$C100`-`$CFFF`.

No such trap is registered. `DiskIIExpansionRomStub` is the expansion image
`DiskIIController` publishes. It is a 2 KB `RomTarget` (`LDA #$00` / `RTS`
at the base, `$FF` elsewhere), not a trap handler.

The same single-key rule as slot ROM applies. The v1.0 pattern of one
conditional `$C800` handler per slot does not fit that key.

### 8.3.7 Trap registry lifecycle — Implemented

The live lifecycle is the registry API:

- `Register`, `Register` with `TrapOperation`, `RegisterWithContext`,
  `RegisterSlotDependent`, and `RegisterLanguageCardRam`.
- `Unregister`, `UnregisterWithContext`, `UnregisterLanguageCardRam`,
  `UnregisterSlotTraps`, `UnregisterContextTraps`, and `Clear`.
- `SetEnabled` returns whether the entry existed. `SetCategoryEnabled`
  returns the number of traps in the category when the enabled state
  changes, and 0 when the category was already in the requested state.
  Disabling a category leaves the entries in the dictionary.
- `TrapRegistry` implements `ITrapRegistryObserver` and raises
  `TrapRegistered`, `TrapUnregistered`, `TrapInvoked`, and
  `TrapEnabledChanged`. The debugger trap monitor in
  [`TrapMonitorWindow`](../../src/BadMango.Emulator.Debug.UI/Views/TrapMonitorWindow.axaml.cs)
  subscribes to those events.

`SlotManager.Install` and `Remove` do not call the registry. The v1.0
`OnSlotInstall` / `OnSlotRemove` hooks are not in the tree. The comment on
`MachineBuilder.Build` says a peripheral may register during `Initialize` or
slot installation. No type in `src/` does.

### 8.3.8 Handling the $CFFF access — Implemented

`$xFFF` is handled in `CompositeIOTarget`, not by a trap. The read and the
write both deselect the active expansion slot. The read returns the internal
ROM byte when `INTCXROM` is on, and otherwise the revealed default ROM or
`$FF`. The v1.0 text also sketches a trap handler for this access. The code
leaves `$CFFF` to the bus. A handled call trap at that address would skip
the bus access, and no such trap is registered.

### 8.3.9 Complete trap registry interface — Implemented

`ITrapRegistry` is the v1.0 method list plus operation, context, slot, and
language-card overloads. Names and results that differ from the v1.0 sketch:

| v1.0 sketch | Live member |
| --- | --- |
| `Monitor`, `BasicInterp`, `BasicRuntime`, `Dos`, `Firmware`, `PrinterDriver`, `DiskDriver`, `Custom` | `TrapCategory` is `MonitorRom`, `BasicInterpreter`, `OperatingSystem`, `SlotFirmware`, `OnboardDevice`, `UserDefined`. `SlotFirmware` is the shared name |
| `GetAll` | `GetAllTraps` |
| `GetInfo` | `GetTrapInfo`, plus `GetTrapsAtAddress` |
| `HasTrap` | `HasTrap` and `ContainsAddress`. `ContainsAddress` is true when any operation is registered at the address |
| `SetEnabled` returns void | `SetEnabled` returns `bool` |
| `SetCategoryEnabled` returns void | `SetCategoryEnabled` returns `int` |
| `TrapInfo` without operation or context | `TrapInfo` carries `Operation`, `Handler`, `IsEnabled`, `SlotNumber`, and `MemoryContext` |
| Three-field `TrapResult` | `Handled`, `CyclesConsumed`, `ReturnMethod`, `ReturnAddress`, plus `NotHandled`, `Success`, `SuccessWithRedirect`, and `SuccessInterrupt` |

`IsEmpty` is on the concrete `TrapRegistry`, not on `ITrapRegistry`. The
65C02 uses it to skip the fetch check while the registry has no addresses.

### 8.3.10 Well-known trap points — Aspirational

The tables below are the v1.0 catalog. No `Register` call site in `src/`
names these entries. `TrapCategory.MonitorRom` is unused by production
registration. The addresses were not checked against a ROM image in the
tree.

#### 8.3.10 / Monitor ROM ($F800-$FFFF) — Aspirational

| Address | Name | Description in the v1.0 catalog |
| --- | --- | --- |
| `$F800` | PLOT | Plot lo-res point |
| `$F819` | HLINE | Draw horizontal lo-res line |
| `$F828` | VLINE | Draw vertical lo-res line |
| `$F832` | CLRSCR | Clear lo-res screen |
| `$F836` | CLRTOP | Clear top of lo-res screen |
| `$F847` | GBASCALC | Calculate lo-res base address |
| `$F856` | SETCOL | Set lo-res color |
| `$F871` | SCRN | Read lo-res screen color |
| `$FB1E` | PREAD | Read paddle position |
| `$FB2F` | INIT | Initialize text screen |
| `$FB39` | SETTXT | Set text mode |
| `$FB40` | SETGR | Set lo-res graphics mode |
| `$FBC1` | BASCALC | Calculate text base address |
| `$FC10` | PRBLNK | Print 3 spaces |
| `$FC22` | VTAB | Move cursor to line |
| `$FC42` | CLREOLZ | Clear to end of line (with check) |
| `$FC58` | HOME | Clear screen and home cursor |
| `$FC62` | CR | Output carriage return |
| `$FC66` | LF | Output line feed |
| `$FC70` | SCROLL | Scroll screen up one line |
| `$FC9C` | CLREOL | Clear to end of line |
| `$FCA8` | WAIT | Delay loop |
| `$FD0C` | RDKEY | Read key from keyboard |
| `$FD35` | RDCHAR | Read character with escape handling |
| `$FD67` | GETLN | Get line of input |
| `$FD6A` | GETLN1 | Get line (skip prompt) |
| `$FD6F` | GETLNZ | Get line (clear first) |
| `$FD8E` | CROUT | Output CR |
| `$FDDA` | PRBYTE | Print byte as hex |
| `$FDE3` | PRHEX | Print nibble as hex |
| `$FDED` | COUT | Output character |
| `$FDF0` | COUT1 | Output character (no escapes) |
| `$FE80` | SETINV | Set inverse video |
| `$FE84` | SETNORM | Set normal video |
| `$FE89` | SETKBD | Set keyboard as input device |
| `$FE93` | SETVID | Set video as output device |

The v1.0 text marks each of these safe to trap. Nothing in `src/` registers
them.

#### 8.3.10 / Applesoft BASIC ($D000-$F7FF) — Aspirational

| Address | Name | Description in the v1.0 catalog |
| --- | --- | --- |
| `$D365` | RESTART | BASIC warm start |
| `$D683` | CHKCOM | Check for comma |
| `$D6A5` | FRMNUM | Evaluate numeric expression |
| `$D6DA` | FRMEVL | Evaluate expression |
| `$DD67` | VAR | Get variable pointer |
| `$DFE3` | PRINT | PRINT statement |
| `$E10C` | STROUT | Print string |
| `$E2F2` | LINPRT | Print line number |
| `$E752` | FOUT | Float to string |
| `$EAF9` | FIN | String to float |
| `$EB63` | FADD | Floating add |
| `$EB90` | FSUB | Floating subtract |
| `$EBA0` | LOG | Natural log |
| `$EC23` | FMULT | Floating multiply |
| `$ED36` | FDIV | Floating divide |

No BASIC trap table is registered.

#### 8.3.10 / Disk II controller — Aspirational

| Address | Name | v1.0 note |
| --- | --- | --- |
| `$C600` | BOOT | Slot 6 cold boot entry |
| `$C620` | BOOT1 | Boot continuation |
| `$C800` | *(varies)* | Expansion ROM base, conditional on the active slot |
| `$Cxxx` | RWTS-related | Read/write track/sector, conditional on the active slot |

`DiskIIController` and `DiskIIBootRom` emulate the card. `DiskIIBootRom` is
a 256-byte target filled from caller-supplied P5A bytes. When those bytes
are absent, the controller's slot ROM is null and the composite target falls
through to internal slot firmware. The controller always publishes
`DiskIIExpansionRomStub` as `ExpansionROMRegion`. Neither image is a trap
handler, and neither path registers `$C600` or an RWTS trap.

### 8.3.11 CPU integration — Partly implemented

`Cpu65C02.Step` reads the program counter and calls `MayHaveTrapAt` before
the opcode fetch. For a concrete `TrapRegistry`, that is
`MayHaveCallTrap`: false when no call traps are registered, without an
address lookup, and otherwise an operation-specific lookup. A read-only or
write-only trap at the program counter does not enter `TryExecute`. For any
other registry that is not `NullTrapRegistry`, the pre-check is
`HasTrap(address, Call)`. `NullTrapRegistry` skips the lookup.

`MainBus.Read8`, `TryRead8`, `Write8`, and `TryWrite8` dispatch
`TrapOperation.Read` and `TrapOperation.Write` for data and DMA intents
after the access. Instruction fetch and debug intents do not. Slot-dependent
keys include the slot, so two cards can both register at `$C800`; execution
uses the active expansion slot.

When the call trap returns `Handled`:

- `Rts` pops the return address, adds one, and charges 6 cycles on top of
  `CyclesConsumed`.
- `Rti` pops the status byte and the return address, with no plus-one, and
  charges 6 cycles.
- `None` stores `ReturnAddress` when it has a value and otherwise leaves the
  program counter. It charges no extra cycles.

`Cpu65816.Step`, `Cpu65816.Reset`, `Cpu65832.Step`, and `Cpu65832.Reset`
throw `NotImplementedException`. Those types have no trap-registry property.

`MachineBuilder` assigns the registry onto a CPU that implements
`ITrapAwareCpu`. `Cpu65C02` is that CPU. `MachineFactory.WireTrapRegistry`
assigns the same component again for the debug hosts. `emudbg` and
`emudbg-mcp` both go through `MachineFactory`. On the `WithPocket2eIOPage`
path the I/O page is also the registry's `IInternalRomHandler`.

`BreakpointManager` registers `TrapOperation.Call` entries named
`BP_<address>` with `TrapCategory.UserDefined` when a debugger breakpoint is
added. A handled hit requests a CPU stop and returns
`TrapResult.Success(Cycle.Zero, TrapReturnMethod.None)`, so the program
counter stays on the breakpoint. That registration happens after the machine
exists. It is not a ROM-routine replacement. A built machine that has not
had a breakpoint added still has an empty registry.

### 8.3.12 ROM-free operation mode — Partly implemented

`Pocket2eMachineBuilderExtensions.WithStubRom` calls `WithRom` with 16 KB of
`$EA` (NOP), the name `"Stub ROM"`, and a load address of `$C000`. Offset
`$3F00` is `JMP $FF00`. The NMI, reset, and IRQ/BRK vectors at the end of
the image all point at `$FF00`. Tests use this helper. Profiles do not.

The tree contains no type named `StubRom`, no per-entry native handler, and
no landing-pad image of RTS opcodes at trapped addresses. The v1.0 sketch
fills a stub with `STP` (`$DB`) and points the vectors at `$FA62`, `$FA40`,
and `$FA00`.

`DiskIIBootRomStub` is a 256-byte slot ROM: identification bytes, `RTS` at
offset 0, and `$FF` elsewhere. `DiskIIControllerStub` mounts it. The
production `DiskIIController` mounts a caller-supplied `DiskIIBootRom` or no
slot ROM, plus `DiskIIExpansionRomStub`. Those are ROM images. They are not
the v1.0 ROM-free handler table.

## Tests

Bus-level coverage of the registry, the null registry, slot selection, and
the composite `$CFFF` path is in `tests/BadMango.Emulator.Bus.Tests`:

- [`TrapRegistryTests`](../../tests/BadMango.Emulator.Bus.Tests/TrapRegistryTests.cs)
- [`NullTrapRegistryTests`](../../tests/BadMango.Emulator.Bus.Tests/NullTrapRegistryTests.cs)
- [`SlotManagerTests`](../../tests/BadMango.Emulator.Bus.Tests/SlotManagerTests.cs)
- [`CompositeIOTargetTests`](../../tests/BadMango.Emulator.Bus.Tests/CompositeIOTargetTests.cs)

> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`;
> target: `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).

Cross-suite coverage of the component `Build` installs, fetch-time
execution, and `WithStubRom` is in `tests/BadMango.Emulator.Tests`:

- [`MachineBuilderIntegrationTests`](../../tests/BadMango.Emulator.Tests/MachineBuilderIntegrationTests.cs)
- [`TrapRegistryIntegrationTests`](../../tests/BadMango.Emulator.Tests/TrapRegistryIntegrationTests.cs)
- [`TrapRegistryComprehensiveIntegrationTests`](../../tests/BadMango.Emulator.Tests/TrapRegistryComprehensiveIntegrationTests.cs)

> **Current location (until #565):** `tests/BadMango.Emulator.Tests`
> cross-suite tests cover trap and machine paths; target: subject-specific
> test projects (namespace unchanged).

[`MachineFactoryTests`](../../tests/BadMango.Emulator.Debug.Infrastructure.Tests/MachineFactoryTests.cs)
covers assigning the component onto `Cpu65C02`. That test project is already
`BadMango.Emulator.Debug.Infrastructure.Tests`.

## Source of truth

- [#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
  and
  [`Architecture Docs Restructure Proposal.md`](../Architecture%20Docs%20Restructure%20Proposal.md)
  — which sections move here, and the status of each one.
- [#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
  and
  [`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md)
  — target assemblies. Namespaces are unchanged.
- [`Architecture Spec v1.0 Part 2`](../Architecture%20Spec%20v1.0%20Part%202.md#77-expansion-rom-lifecycle)
  — historical source for
  [§7.7](../Architecture%20Spec%20v1.0%20Part%202.md#77-expansion-rom-lifecycle)
  and
  [§8.3](../Architecture%20Spec%20v1.0%20Part%202.md#83-rom-routine-interception-trap-handlers).
  Its samples are not the implementation where this page says otherwise.
- [`ARCHITECTURE.md`](../../ARCHITECTURE.md) — the current-location note used
  above.