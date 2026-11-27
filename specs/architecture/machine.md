# Machine assembly and profiles

## Summary

Pocket2e is the machine this repository builds. `MachineBuilder` assembles a CPU,
a memory bus, devices, a scheduler, and a signal bus into an `IMachine`.
`Pocket2eMachineBuilderExtensions` wires the 65C02 Pocket2e layout.
Profiles under `profiles/` name a machine in JSON, and `MachineBuilder.FromProfile`
applies the regions, motherboard devices, and slot cards that the loader
understands. A built machine starts `Off`. Power-on applies a RAM pattern and a
power-on reset; later resets pulse `SignalLine.Reset`.

Coverage and status come from
[#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536)
and
[`Architecture Docs Restructure Proposal.md`](../Architecture%20Docs%20Restructure%20Proposal.md).
Target assembly layout comes from
[#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478)
and
[`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md).
Namespaces below are the namespaces in the tree. This page does not rename them.

## Covers

- Machine taxonomy for Pocket2e, and the unbuilt PocketGS and PocketME rows (§1.1).
- The Pocket2e memory map the builder and the loadable profiles actually install (§7.5).
- Pocket2e reset and boot (§10.1).
- The Pocket2e rows of the memory-map quick reference (A.2).
- Builder overview, the live build path, the machine interface, reset, usage, and
  the gap summary (B.1–B.5).
- The machine object and device-initialization order (Gap 5, Gap 6).
- Profiles under `profiles/`, including named slot sets.

## Does not cover

- 65C02 execution, registers, and opcode dispatch:
  [`cpu.md`](cpu.md). `Cpu65816` and `Cpu65832` remain companions in
  [#540](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/540)
  and
  [#541](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541).
- Page-table routing, composite dispatch, and remapping internals:
  [`bus-mmu.md`](bus-mmu.md). Fault registers and `ABORT` stay in the
  [fast-path specification](../Bus%20Fast%20Path%20and%20Fault%20Registers%20Specification.md).
- Signal-line contracts and the 65C02 IRQ, NMI, RESET, and `ABORT` reactions:
  [`signals.md`](signals.md). This page owns the machine pulse and power state.
- Scheduler queue internals and Gap 4. That page is
  [#544](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/544).
  This page records only that machine reset does not call `Scheduler.Reset`.
- Soft switches, slots, and peripheral behavior:
  [`devices.md`](devices.md).
- Expansion ROM and traps:
  [#546](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/546).
- Apple IIc personalities:
  [#547](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/547).
- The desktop shell. `bpui` is a host, documented in
  [`host-ui.md`](host-ui.md). It is not the machine.

## Code paths

Programmatic Pocket2e wiring lives in `BadMango.Emulator.Systems`:

- [`Pocket2eMachineBuilderExtensions`](../../src/BadMango.Emulator.Systems/Pocket2eMachineBuilderExtensions.cs)
  — `AsPocket2e`, `WithPocket2eMemoryLayout`, `WithLanguageCard`,
  `WithAuxiliaryMemory`, `WithSlotManager`, `WithPocket2eIOPage`.

`MachineBuilder`, `Machine`, and `IMachine` live in namespace
`BadMango.Emulator.Bus`. Their target assembly is `BadMango.Emulator.Core`.

- [`MachineBuilder`](../../src/BadMango.Emulator.Core/MachineBuilder.cs) and
  [`MachineBuilder.FromProfile`](../../src/BadMango.Emulator.Core/MachineBuilder.FromProfile.cs)
- [`Machine`](../../src/BadMango.Emulator.Core/Machine.cs),
  [`MachineState`](../../src/BadMango.Emulator.Core/MachineState.cs), and
  [`IMachine`](../../src/BadMango.Emulator.Core/Interfaces/IMachine.cs)
- [`PowerOnMemoryPattern`](../../src/BadMango.Emulator.Core/PowerOnMemoryPattern.cs)
- [`SlotSetRuntimeState`](../../src/BadMango.Emulator.Core/SlotSetRuntimeState.cs) and
  [`SlotSetRuntimeDefinition`](../../src/BadMango.Emulator.Core/SlotSetRuntimeDefinition.cs)
- [`MachineBuilderExtensions`](../../src/BadMango.Emulator.Core/MachineBuilderExtensions.cs)
  registers the `composite-io` handler.

The 65C02 implementation lives in namespace `BadMango.Emulator.Emulation.Cpu`.
Pocket2e passes an `IEventContext` to `Cpu65C02`.

- [`Cpu65C02`](../../src/BadMango.Emulator.Core/Cpu/Cpu65C02.cs) reads the
  reset vector at `$FFFC` (`Cpu65C02Constants.ResetVector`).
- [`CpuFamily`](../../src/BadMango.Emulator.Core/Cpu/CpuFamily.cs) names
  `Cpu65C02`, `Cpu65C816`, and `Cpu65832`.

Profile types already live in `BadMango.Emulator.Core.Configuration`:

- [`MachineProfile`](../../src/BadMango.Emulator.Core/Configuration/MachineProfile.cs)
- [`MachineProfileLoader`](../../src/BadMango.Emulator.Core/Configuration/MachineProfileLoader.cs)
  — default profile name `simple-65c02`.
- [`SlotSystemProfile`](../../src/BadMango.Emulator.Core/Configuration/SlotSystemProfile.cs),
  [`SlotCardSetProfile`](../../src/BadMango.Emulator.Core/Configuration/SlotCardSetProfile.cs),
  and [`SlotSetCardProfile`](../../src/BadMango.Emulator.Core/Configuration/SlotSetCardProfile.cs)
- [`BootProfile`](../../src/BadMango.Emulator.Core/Configuration/BootProfile.cs)

The debugger host turns a profile into a machine with
[`MachineFactory`](../../src/BadMango.Emulator.Debug.Infrastructure/MachineFactory.cs)
in the surviving `BadMango.Emulator.Debug.Infrastructure` assembly. That factory
is the profile entry point. It is not `bpui`.

Language-card and auxiliary-memory devices used by the layout:

- [`LanguageCardDevice`](../../src/BadMango.Emulator.Devices/LanguageCardDevice.cs)
  in `BadMango.Emulator.Devices`.
- [`AuxiliaryMemoryController`](../../src/BadMango.Emulator.Core/AuxiliaryMemoryController.cs)
  in namespace `BadMango.Emulator.Bus`, in Core with `MachineBuilder`.
- [`DeviceFactoryExtensions`](../../src/BadMango.Emulator.Devices/DeviceFactoryExtensions.cs)
  registers motherboard and slot-card factories, including the language-card
  and extended 80-column factories that configure their own RAM.

## Target layout

`BadMango.Emulator.Systems` keeps Pocket2e composition and references Core and
Devices. It stays a separate assembly. `BadMango.Emulator.Core` holds
`MachineBuilder`, the bus, and the CPU implementation under the existing
`BadMango.Emulator.Bus` and `BadMango.Emulator.Emulation.Cpu` namespaces.
Profiles stay under `profiles/`. The `bpui` shell does not reference the machine.

Systems references Core and Devices. The Systems source path does not move.
Systems does not have a test project. Pocket2e and machine tests
(`MachineBuilderIntegrationTests`, `ResetSignalMachineTests`,
`MachineProfileLoaderTests`, `MachineProfileSerializerTests`) live in
`tests/BadMango.Emulator.Core.Tests` (namespace `BadMango.Emulator.Tests` unchanged).

> **Conditional removal (#566):** `IBringUpHandler`, `BringUpHandlerBase`,
> `IBringUpResult`, `BringUpResult`, `BringUpValidationResult`,
> `IProvisioningBundle`, `ProvisioningBundle`, `IMachineConstants`, and
> `BringUpHandlerTests`. Current files are under `src/BadMango.Emulator.Core/`
> and `tests/BadMango.Emulator.Core.Tests/BringUpHandlerTests.cs`. `MachineBuilder`,
> Systems, and Devices do not use them. They do not power machine construction.
> Removal stays conditional on #566's verification. This note does not assign
> them a destination.

## Profiles

JSON profiles live in [`profiles/`](../../profiles/). The local schema is
[`schemas/machine-profile.schema.json`](../../schemas/machine-profile.schema.json).
Every file in that directory uses a 65C02 and a 16-bit `addressSpace`. The schema
enum also names `6502`, `65816`, and `65832`. `FromProfile` accepts `65C02`,
`65816`, and `65832`, and rejects any other string. `MachineFactory` then supplies
a CPU factory that constructs `Cpu65C02` for `65C02` and throws
`NotSupportedException` for `6502`, `65816`, and `65832`. No profile selects those
three.

`cpu.clockSpeed` becomes `IMachine.ClockSpeedHz` (zero leaves the run loop
unthrottled). When the profile omits a positive clock, keyboard timing uses
`MachineBuilder.ProfileCpuClockHz`, which defaults to 1,000,000 Hz.

`FromProfile` requires the regions array. It creates physical blocks, maps `ram`,
`rom`, and `composite` regions, skips disabled motherboard devices, and installs
slot cards when `devices.slots.enabled` is true. An unknown motherboard type is
skipped. An unknown slot-card type is skipped, and the builder writes a warning
when it has a logger. Region type `io` throws `NotSupportedException`. Region
start, size, and source offset must be 4 KB aligned.

`memory.swapGroups` and `memory.controllers` deserialize onto
`MemoryProfileSection` and have no reader in `FromProfile`. Language-card
banking and auxiliary RAM are the `languagecard` and `extended80column`
motherboard devices, which allocate their own memory. `devices.slots.internalC3Rom`
and `internalCxRom` are applied to the composite I/O handler, and to the extended
80-column card's power-on state when that device is present. `devices.slots.io-region`
deserializes and is not read; the composite region's handler name selects the I/O
target. `boot.startupSlot` deserializes and is not read. Guest ROM autostart owns
the boot slot.

| Profile | Clock (Hz) | Regions `FromProfile` maps | Devices and slots | Boot flags |
| --- | --- | --- | --- | --- |
| [`pocket2e.json`](../../profiles/pocket2e.json) | 1020484 | 48 KB RAM at `$0000`, `composite-io`, 12 KB ROM at `$D000` | Keyboard, speaker, video, game I/O; slot 4 `pocketwatch`; slot 6 `disk-ii-compatible` | `autoStart` true |
| [`pocket2e-a2-enh.json`](../../profiles/pocket2e-a2-enh.json) | 1020484 | 48 KB RAM, `composite-io`, 12 KB ROM at `$D000` | Keyboard, video, character, speaker, language card, game I/O, extended 80-column; slots 4 and 6 | `autoStart` false; `autoVideoWindowOpen` true |
| [`pocket2e-a2.json`](../../profiles/pocket2e-a2.json) | 1020484 | Same shape as the enhanced profile | Same motherboard set; slots 4 and 6 | `autoStart` false; `autoVideoWindowOpen` true |
| [`pocket2e-devices.json`](../../profiles/pocket2e-devices.json) | 1020484 | 48 KB RAM, `composite-io`, 12 KB ROM at `$D000` | Keyboard, speaker, video, game I/O; slot 4 `pocketwatch` | `autoStart` false |
| [`pocket2e-lite.json`](../../profiles/pocket2e-lite.json) | 1020484 | 48 KB RAM, `composite-io`, 12 KB ROM at `$D000` | Keyboard, video, character, speaker, language card; slot 4 `pocketwatch` | `autoStart` false; `autoVideoWindowOpen` true |
| [`pocket2e-min.json`](../../profiles/pocket2e-min.json) | 1020484 | 48 KB RAM, `composite-io`, 12 KB ROM at `$D000` | No motherboard devices; `cards` is empty | `autoStart` false |
| [`simple-65c02.json`](../../profiles/simple-65c02.json) | 1000000 | 64 KB RAM at `$0000` | None. `MachineProfileLoader.DefaultProfileName` is `simple-65c02` | No boot section |
| [`simple-65c02-with-rom.json`](../../profiles/simple-65c02-with-rom.json) | 1000000 | 48 KB RAM and 16 KB ROM at `$C000` | None | No boot section |

`pocket2e.json` maps the same 48 KB RAM, `composite-io` page, and `$D000` ROM
window as `pocket2e-devices.json`. It does not declare swap groups, an auxiliary
controller, or language-card physical blocks. `MachineFactory` registers the
handler name `composite-io`. Where a profile lists `languagecard` or `extended80column`,
`RegisterStandardDeviceFactories` builds that device's own RAM during
`ConfigureMemory`. `pocket2e.json` does not declare those devices.

### Named slot sets

`devices.slots.sets` is optional. When it is present, `defaultSet` is required
and must match a set name. Matching ignores case. The active set keeps the
casing stored in the profile. Two set names that differ only by case throw.
Each set overlays `devices.slots.cards`: a set card adds or replaces a slot, and
a null `type` removes that slot from the base list. A set may name each slot
once. Slot numbers are 1 through 7 (`PeripheralSlots.Minimum` and `Maximum`).

`FromProfile` installs the resolved default set and stores a
`SlotSetRuntimeState` component. No file under `profiles/` defines `sets` or
`defaultSet`. The behavior is covered by `MachineBuilderTests`.

Switching sets at runtime (`slot use`) and inserting or removing a card require
`MachineState.Off`. `InsertSlotCard` installs and initializes the card, then
flushes the scheduler so an off machine keeps an empty queue. Power-on reset
re-arms periodic device work. The loaded profile file is left unchanged.
`SlotSetRuntimeState.IsModified` records a manual edit.

## 1.1 Machine Taxonomy

**Status: Partly implemented**

| Machine | CPU | Address space | What the tree builds |
| --- | --- | --- | --- |
| Pocket2e | `CpuFamily.Cpu65C02` | 16-bit visible space | The built machine. `AsPocket2e` calls `WithAddressSpace(16)` and `new Cpu65C02(context)`. |
| PocketGS | 65C816 | 24-bit / 16 MB in the v1.0 table | No PocketGS type. `Cpu65816.Step` and `Cpu65816.Reset` throw `NotImplementedException`. |
| PocketME | 65832 | 32-bit / 4 GB in the v1.0 table | No PocketME type. `Cpu65832.Step` and `Cpu65832.Reset` throw `NotImplementedException`. |

`AsPocket2e` documents a 128 KB machine: `MainRamSize` and `AuxRamSize` are each
64 KB, and `FullRamSize` is 128 KB. `WithPocket2eMemoryLayout` allocates those
two blocks. `LanguageCardDevice` allocates a further 16 KB (two 4 KB banks at
`$D000` and 8 KB at `$E000`) and banks it over `$D000-$FFFF`. The visible bus
stays 16 bits. PocketGS and PocketME address widths are not built.
`MachineBuilder.WithCpu` records a `CpuFamily` and does not construct a CPU.
`CreateDefaultCpu` throws `InvalidOperationException` for `Cpu65C02` unless
`WithCpuFactory` or `AsPocket2e` supplied a factory, and throws
`NotSupportedException` for `Cpu65C816` and `Cpu65832`.

There is no nested PocketME / PocketGS / Pocket2e runtime. Compatibility modes
are the shared register helpers in [`cpu.md`](cpu.md).

## 7.5 Apple II Memory Map

**Status: Partly implemented**

The audit note names `ConfigurePocket2eMemory`. The layout method in the tree is
`WithPocket2eMemoryLayout`. `AsPocket2e` then adds the language card, auxiliary
controller, slot manager, I/O page, and an empty `VectorTable` layer at priority
1000. `AsPocket2e` does not call `WithVectorTable` or `WithStandardVectorTable`.

Programmatic Pocket2e map:

| Range | What `AsPocket2e` installs |
| --- | --- |
| `$0000-$FFFF` | 64 KB main RAM (`RegionTag.Ram`, `PagePerms.All`), mapped first. Later overlays replace pages. |
| `$0000-$FFFF` auxiliary | 64 KB auxiliary RAM held as `AuxiliaryRamComponent`. It is not mapped until `AuxiliaryMemoryController` bank-switches it. |
| `$C000-$CFFF` | `CompositeIOTarget` from `WithPocket2eIOPage`, installed in `BeforeDeviceInit`. |
| `$D000-$FFFF` | Language-card layers: two 4 KB banks for `$D000-$DFFF`, 8 KB for `$E000-$FFFF`. Reads fall through to the base mapping while language-card RAM is off. |
| `$FFFA-$FFFF` | The `VectorTable` layer exists. It has no bytes until the caller adds `WithVectorTable`. |

`WithStandardVectorTable` writes placeholder handler addresses `$FA40`, `$FA62`,
and `$FA40`. Its remarks say those values are placeholders. A built profile does
not install that table. The CPU reads whatever the bus returns at `$FFFC`.

Profile maps that `FromProfile` can load are the 48 KB RAM window at `$0000`,
the `$C000` composite page, and, when the file has a ROM region, a 12 KB or
16 KB ROM window. `simple-65c02-with-rom.json` places 16 KB of ROM at `$C000`,
which covers the I/O page addresses. Language-card and extended 80-column
devices, when the profile names them, add their own banked RAM on top of that
region list. Page-3 vectors in the v1.0 map (`$03F0`, `$03FE`, and the DOS work
area) are ordinary RAM. The emulator does not own those guest conventions.
Soft-switch detail for `$C000-$CFFF` is in [`devices.md`](devices.md).

## 10.1 Pocket2e Boot Sequence

**Status: Partly implemented**

`Machine.BootAsync` is the run entry.

- When `State` is `Off`, `BootAsync` calls the power-on path, then `RunAsync`.
- When `State` is already on, `BootAsync` calls `Reset()` and then `RunAsync`.

Power-on applies `PowerOnMemoryPattern` to every `IHardResetMemory` component
(or `Bus.Clear` when none is registered), calls each peripheral `Reset`, sets
the power-on-reset flag, and pulses RESET. Releasing that first pulse calls
`Cpu65C02.Reset`, which constructs registers with `new Registers(true, vector)`
and loads PC from `$FFFC` through the post-reset mapping. `BootAsync` then
starts execution. Disk autostart and the monitor or BASIC prompt are code inside
the ROM image. This repository has no boot loader that scans slots or jumps to
`$0801`.

`boot.autoStart` is read by `BootCommand.AutoStartProfileAsync`, which calls
`BootAsync` only while the machine is `Off`. `boot.startupSlot` is stored on
`BootProfile` and has no reader in the builder, `Machine`, or `BootCommand`.
Shipped profiles omit it.
`boot.autoVideoWindowOpen` opens the debugger video window from `BootCommand`.
`boot.powerOnMemory` is applied by `MachineFactory.CreateDebugSystem` and is not
applied by `CreateMachine`. No file under `profiles/` sets `powerOnMemory`.

The v1.0 seven-step list (firmware soft-switch init, autostart search, load
`$0801`, enter the monitor) is the guest ROM's job once PC is at the reset vector.

## A.2 Memory Map Quick Reference (Pocket2e)

**Status: Partly implemented**

The Pocket2e quick reference that matches the builder is a 16-bit map, with
auxiliary RAM bank-switched into that map:

```
$0000-$BFFF : main RAM on the loadable profiles (48 KB)
$0000-$FFFF : main RAM on AsPocket2e, before I/O and ROM overlays (64 KB)
$C000-$CFFF : composite I/O (soft switches, slot ROM, expansion ROM)
$D000-$FFFF : ROM, or language-card RAM when that device has enabled it
Banked aux  : 64 KB, reached through auxiliary-memory soft switches
```

`AsPocket2e` uses one 16-bit space. It does not install a second CPU bank.
The PocketGS 16 MB rows and the PocketME 4 GB rows in the v1.0 appendix are
aspirational and belong to the 65816 and 65832 companions
([#540](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/540),
[#541](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541)).
This page does not copy those maps.

## B.1 Overview

**Status: Implemented**

A machine is a `MachineBuilder` product. `Build` creates the scheduler, device
registry, signal bus, and `MainBus`, runs memory configuration, maps ROM
descriptors, creates layers, constructs the CPU from `WithCpuFactory` (or
`CreateDefaultCpu`), assembles `Machine`, runs `BeforeDeviceInit`, calls
`InitializeDevices`, registers motherboard soft-switch handlers, installs
pending slot cards, runs component factories, and flushes the scheduler.
The returned machine's `State` is `Off`.

The v1.0 phase list (a `DeviceRegistry` created by the caller, then RAM, ROM,
and CPU objects passed into a `Pocket2eMachine`) is the same idea. The method
that runs it is `MachineBuilder.Build`.

## B.2 Complete Build Example

**Status: Partly implemented**

The working programmatic path is the extension methods on `MachineBuilder`:

```csharp
IMachine machine = new MachineBuilder()
    .AsPocket2e()
    .WithRom(romImage, 0xC000, "System ROM")
    .Build();
```

`AsPocket2e` is `WithAddressSpace(16)`, `WithCpu(CpuFamily.Cpu65C02)`,
`WithCpuFactory` (`new Cpu65C02(context)`), `WithPocket2eMemoryLayout`,
`WithLanguageCard`, `WithAuxiliaryMemory`, `WithSlotManager`,
`WithPocket2eIOPage`, and `CreateLayer("VectorTable", 1000)`.
`WithCard` queues a slot card as `PendingSlotCard`. `WithStubRom` installs a
16 KB NOP ROM whose reset vector is `$FF00`, for tests.

The working profile path is `MachineFactory`, which registers `composite-io`
when a region asks for it, calls `RegisterStandardDeviceFactories`, then
`FromProfile`, `WithCpuFactory`, and `Build`.

The appendix sample (`Pocket2eMachineBuilder`, `IPocket2eMachine`, `RamDevice`,
`AppleIISoftSwitchPage`, `new Cpu65C02(bus, signals)`) is not the API
`MachineBuilder` exposes. `WithRom(byte[])` on that sample type is not a
`MachineBuilder` method. `MachineBuilder.WithRom` takes the image, a load
address, and an optional name.

## B.3 Machine Interface and Reset Sequence

**Status: Implemented**

`IMachine` exposes `Cpu`, `Bus` (through `IEventContext`), `Signals`,
`Scheduler`, `Devices`, and `State`. Other devices are components retrieved
with `GetComponent<T>`. `Reset(bool hardReset = false)` pulses
`SignalLine.Reset` from the machine's own source.

Asserting RESET calls `Scheduler.FlushPending`, then `IResetReceiver.OnResetAsserted`
(bus, components, scheduled devices; slot cards receive the line through the
slot manager). Releasing RESET calls `OnResetReleased`. The line is held for
two CPU cycles (`ResetHoldCycles`). A running machine stays `Running` and
releases the line from a scheduled event. A stopped or paused machine steps the
hold and the release sequence and stays in that state, with PC at the vector.

Power-on reset and hard reset set `powerOnResetPending`. On release,
`Cpu65C02.Reset` loads `$FFFC` and replaces the warm sequence. A later warm
reset does not call `Cpu.Reset`. `Step` runs the release sequence: `I` is set,
`D` is cleared, `E` and `CP` are set, the `M` and `X` status flags are set,
`SP` drops by 3, and PC is loaded from `$FFFC`. `A`, `X`, and `Y` stay as they
were. `Scheduler.Reset` exists and is not called. `Scheduler.Now` is not zeroed.
`AbortReaction` is left unchanged.

`Reset` throws `InvalidOperationException` while `State` is `Off`
(`ThrowIfOff`). The built machine starts `Off`, so the first entry is
`BootAsync` or `PowerOnAsync`, not `Reset`.

### Power state

`MachineState` is `Off`, `Stopped`, `Running`, or `Paused`. The field starts at
`Off`. `Build` does not move it.

| Operation | RAM | Devices | CPU | State afterward |
| --- | --- | --- | --- | --- |
| `PowerOnAsync` / `BootAsync` from `Off` | `PowerOnMemoryPattern` on each `IHardResetMemory`, or `Bus.Clear` when none is registered | `IPeripheral.Reset`, then the RESET pulse notifies `IResetReceiver` | Power-on `Cpu.Reset` during the RESET release | `Stopped`, then `BootAsync` enters `Running` |
| `Reset(false)` while on, after the first power-on | Preserved | RESET receivers see assert and release | Warm release sequence in `Step` | Unchanged (`Running` stays `Running`) |
| `Reset(true)` while on | `ClearForHardReset` (zeros; `Bus.Clear` if no `IHardResetMemory`) | `PowerOnResetDevices` while the line is held | Power-on `Cpu.Reset` | Unchanged run state |
| `PowerOffAsync` | `PowerOnMemoryPattern` again, after disks are flushed | `PowerOnResetDevices`, then the scheduler queue is flushed | No RESET pulse and no `Cpu.Reset` | `Off` |

The default pattern kind is `PowerOnMemoryPatternKind.AppleIIe`. The other kinds
are `Zero`, `Alternating00FF`, and `Random`. A null `PowerOnMemorySeed` uses
`Environment.TickCount64` for that power-on and logs the seed. A non-null seed
is reused. `CreateDebugSystem` copies `boot.powerOnMemory` onto the machine
before that path runs.

Slot insert, slot remove, and named-set switches throw while the machine is not
`Off`. `Run`, `Step`, and `Reset` throw while it is `Off`. `Pause` while `Off`
returns without a state change.

## B.4 Usage Example

**Status: Partly implemented**

`BootAsync` is the run entry. From a machine `Build` just returned:

```csharp
await machine.BootAsync(cancellationToken);
```

That powers on, loads PC from `$FFFC`, and runs. `PowerOnAsync` powers on and
leaves the machine `Stopped`. A later `Reset()` is a warm reset. `Reset(true)`
is a hard reset. `PowerOffAsync` returns the machine to `Off`.

The appendix sample (`new Pocket2eMachineBuilder().WithRom(...).WithSlotCard(6, ...).Build()`,
then `Reset()` and `Run()`, and a comment that PC is typically `$FA62`) does not
match `Machine` or `MachineBuilder`. `$FA62` is only the placeholder stored by
`WithStandardVectorTable`.

## B.5 Identified Specification Gaps

**Status: Partly implemented**

Gaps 1–3 and 5–8 match the code. Gap 4 added `Scheduler.Reset`, which clears the
queue and zeroes `Now`. Machine reset does not call it. RESET assert calls
`Scheduler.FlushPending`.

| Gap | Status in the audit | Where it lives |
| --- | --- | --- |
| Gap 1 Composite page dispatch | Implemented | [`bus-mmu.md`](bus-mmu.md) |
| Gap 2 Dynamic page remapping | Implemented | [`bus-mmu.md`](bus-mmu.md) |
| Gap 3 CPU interface for PC access | Implemented | [`cpu.md`](cpu.md). `Cpu65C02.Reset` reads `$FFFC`. |
| Gap 4 Scheduler reset | Partly implemented | [#544](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/544). Machine reset does not call `Scheduler.Reset`. |
| Gap 5 Machine-level abstraction | Implemented | This page |
| Gap 6 Device initialization order | Implemented | This page |
| Gap 7 CPU construction parameters | Implemented | [`cpu.md`](cpu.md). Pocket2e uses `WithCpuFactory` and `new Cpu65C02(context)`. |
| Gap 8 Stop/halt coordination | Implemented | [`cpu.md`](cpu.md) |

## Gap 5: Machine-Level Abstraction

**Status: Implemented**

`IMachine` is the assembled machine. `Machine` owns the bus, CPU, scheduler,
signal bus, and device registry, and it is the component bucket for everything
else. `MachineState` includes `Off`, which the v1.0 `IMachine` sketch does not.
There is no `IPocket2eMachine` or `IPocket2Machine` with typed `Slots`, `Video`,
`Keyboard`, and `Speaker` properties. Those devices are components.

Lifecycle methods on the live interface include `Reset(bool hardReset)`,
`PulseReset`, `Run`, `RunAsync`, `Stop`, `Step`, `Pause`, `ResumeAsync`,
`BootAsync`, `PowerOnAsync`, `PowerOffAsync`, `InsertSlotCard`, and
`RemoveSlotCard`. `Step` does not advance the scheduler a second time:
`Cpu65C02.Step` already does.

## Gap 6: Device Initialization Order

**Status: Implemented**

`Build` maps ROM descriptors after memory-configuration callbacks and before
`InitializeDevices`. `LanguageCardDevice.Initialize` depends on that order. It
calls `SaveBaseMappingRange` for pages `$D`–`$F` so the language card can
restore the ROM (or other base) mapping that was installed after
`ConfigureMemory`. Swap groups are created earlier, inside the language-card
`ConfigureMemory` callback. `Initialize` throws if those groups are missing.

The same `Build` pass then registers motherboard handlers and installs pending
slot cards. Devices that need the scheduler receive the machine as
`IEventContext` from `InitializeDevices`. `Build` finishes by flushing the
scheduler, because the machine is `Off` and must not keep runnable work queued.
`PowerOnResetDevices` is the later power-on path. It calls `Reset` and does not
call `Initialize` again.

The v1.0 eight-step list and its `EventContext` type are the same dependency
described on `MachineBuilder.Build`. The type that runs it is `Machine`, which
implements `IEventContext`.

## Related pages

- [`ARCHITECTURE.md`](../../ARCHITECTURE.md) — index and the current-location convention.
- [`cpu.md`](cpu.md) — `Cpu65C02`, including reset-vector and halt behavior.
- [`bus-mmu.md`](bus-mmu.md) — page table, composite dispatch, remapping.
- [`signals.md`](signals.md) — RESET hold, release edge, and the 65C02 reaction.
- [`devices.md`](devices.md) — `$C000-$CFFF` soft switches and slot cards.
- [`host-ui.md`](host-ui.md) — `bpui` and the debugger hosts.
- [`Machine Profile Configuration Guide`](../reference/Machine%20Profile%20Configuration%20Guide.md)
  — field-level profile guide. `FromProfile` applies regions, motherboard
  devices, slot cards, named slot sets, and the initial `internalC3Rom` /
  `internalCxRom` selection. It stores `io-region` and does not use it to select
  slot ROM.
