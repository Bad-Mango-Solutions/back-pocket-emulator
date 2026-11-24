# Architecture Index

This index is the entry point for architecture docs in this repository.

GitHub project [Bad-Mango-Solutions/back-pocket-emulator#3](https://github.com/orgs/Bad-Mango-Solutions/projects/3) is the living roadmap.

Target assembly/module layout comes from [#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478) and [`specs/Solution Consolidation Design.md`](specs/Solution%20Consolidation%20Design.md). Architecture page coverage/status comes from [#536](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/536) and [`specs/Architecture Docs Restructure Proposal.md`](specs/Architecture%20Docs%20Restructure%20Proposal.md).

## Current-location convention (for pending #477 moves)

Use this note beside any target-location pointer that has not moved yet:

> **Current location (until #NNN):** `<existing path or project>`; target: `<surviving project/path>` (namespace unchanged).

## System picture

The emulator is split into CPU/bus core logic, devices/storage/rendering/systems, two hosts (`bpui`, `emudbg`), and one MCP host (`emudbg-mcp`).

`bpui` remains a host shell and does not reference the machine. `emudbg-mcp` keeps only `Debug.Infrastructure` as a direct project reference.

## Target source assembly map (#478)

```mermaid
graph TD
  Core[BadMango.Emulator.Core]
  Devices[BadMango.Emulator.Devices]
  Storage[BadMango.Emulator.Storage]
  Rendering[BadMango.Emulator.Rendering]
  Systems[BadMango.Emulator.Systems]
  UI[BadMango.Emulator.UI (bpui)]
  DbgInfra[BadMango.Emulator.Debug.Infrastructure]
  DbgUI[BadMango.Emulator.Debug.UI]
  Debug[BadMango.Emulator.Debug (emudbg)]
  Mcp[BadMango.Emulator.Debug.Mcp (emudbg-mcp)]

  Devices --> Core
  Devices --> Storage
  Systems --> Core
  Systems --> Devices
  DbgInfra --> Core
  DbgUI --> DbgInfra
  Debug --> DbgInfra
  Debug --> DbgUI
  Mcp --> DbgInfra
```

| Target assembly | Role | Target direct project references |
| --- | --- | --- |
| `BadMango.Emulator.Core` | CPU + bus + emulation core | none |
| `BadMango.Emulator.Devices` | machine devices / slot devices | `BadMango.Emulator.Core`, `BadMango.Emulator.Storage` |
| `BadMango.Emulator.Storage` | disk/file-system/media support | none |
| `BadMango.Emulator.Rendering` | display-frame rendering pipeline | (separate from `Devices`) |
| `BadMango.Emulator.Systems` | machine/profile composition helpers | `BadMango.Emulator.Core`, `BadMango.Emulator.Devices` |
| `BadMango.Emulator.UI` (`bpui`) | UI host shell (absorbs Configuration/Infrastructure/UI.Abstractions) | host-only surface (no machine refs) |
| `BadMango.Emulator.Debug.Infrastructure` | debugger contracts + command/session infrastructure (absorbs Debug.Abstractions) | core/debugger service surface |
| `BadMango.Emulator.Debug.UI` | debugger windows/editor surface (absorbs TextEditor) | debugger UI surface |
| `BadMango.Emulator.Debug` (`emudbg`) | debug console executable host | `Debug.Infrastructure`, `Debug.UI` |
| `BadMango.Emulator.Debug.Mcp` (`emudbg-mcp`) | MCP stdio host | `Debug.Infrastructure` only |

> **Current location (until #559):** `src/BadMango.Emulator.Configuration`, `src/BadMango.Emulator.Infrastructure`, and `src/BadMango.Emulator.UI.Abstractions`; target: `src/BadMango.Emulator.UI` (namespaces unchanged).
>
> **Current location (until #560):** `src/BadMango.Emulator.Debug.Abstractions`; target: `src/BadMango.Emulator.Debug.Infrastructure` (namespace `BadMango.Emulator.Debug.Abstractions` unchanged).
>
> **Current location (until #561):** `src/BadMango.Emulator.TextEditor`; target: `src/BadMango.Emulator.Debug.UI` (namespace `BadMango.Emulator.TextEditor` unchanged).
>
> **Current location (until #562):** `src/BadMango.Emulator.Bus` and `src/BadMango.Emulator.Emulation`; target: `src/BadMango.Emulator.Core` (namespaces such as `BadMango.Emulator.Bus` and `BadMango.Emulator.Emulation.Cpu` unchanged).

### Extension seams (#473)

- CPU seam: CPU implementation boundary (`65C02` now; `65816`/`65832` companions documented separately).
- Bus-device / slot-card seam: device registration and page-target routing boundary.
- Firmware/trap seam: expansion-ROM and trap registration boundary.
- Display/audio-sink seam: rendering output and speaker/audio output boundaries.
- Debugger-extension seam: shared debug session/command extension boundary.

Versioned module host support is **not** claimed as implemented yet.

## Where to look

| Task | Target location | Current location |
| --- | --- | --- |
| Add a new opcode | `src/BadMango.Emulator.Core/Cpu/` (target after Bus+Emulation fold) | `src/BadMango.Emulator.Emulation/Cpu/` and `src/BadMango.Emulator.Core/Cpu/` |
| Add a new device | `src/BadMango.Emulator.Devices/` | `src/BadMango.Emulator.Devices/` |
| Bus faults (`FaultKind`, fault registers, `ABORT`) | `src/BadMango.Emulator.Core/` (target after Bus fold) + `specs/Bus Fast Path and Fault Registers Specification.md` | `src/BadMango.Emulator.Bus/` + `specs/Bus Fast Path and Fault Registers Specification.md` |
| Soft switches / I/O page behavior | `src/BadMango.Emulator.Devices/` with machine wiring in `src/BadMango.Emulator.Systems/` | `src/BadMango.Emulator.Devices/` with machine wiring in `src/BadMango.Emulator.Systems/` |

> **Current location (until #562):** `tests/BadMango.Emulator.Bus.Tests`; target: `tests/BadMango.Emulator.Core.Tests` (namespace unchanged).
>
> **Current location (until #565):** `tests/BadMango.Emulator.Tests` cross-suite tests cover CPU/machine paths; target: subject-specific test projects (namespace unchanged).

## Architecture pages and issue tracking

The child architecture pages are tracked by issue; landed pages link to their canonical file paths, while planned pages remain status-pending.

| Issue | Planned page path | Status |
| --- | --- | --- |
| [#539](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/539) | [`specs/architecture/cpu.md`](specs/architecture/cpu.md) | landed |
| [#540](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/540) | `specs/architecture/cpu-65816.md` | pending |
| [#541](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/541) | `specs/architecture/cpu-65832.md` | pending |
| [#542](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/542) | `specs/architecture/bus-mmu.md` | pending |
| [#543](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/543) | `specs/architecture/signals.md` | pending |
| [#544](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/544) | `specs/architecture/scheduler.md` | pending |
| [#545](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/545) | `specs/architecture/devices.md` | pending |
| [#546](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/546) | `specs/architecture/rom-traps.md` | pending |
| [#547](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/547) | `specs/architecture/compat.md` | pending |
| [#548](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/548) | `specs/architecture/observability.md` | pending |
| [#549](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/549) | `specs/architecture/machine.md` | pending |
| [#550](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/550) | `specs/architecture/host-ui.md` | pending |
| [#551](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/551) | [`CONTRIBUTING.md`](CONTRIBUTING.md) | landed |

## Architecture vs. other docs

- Architecture design/index: this file (`ARCHITECTURE.md`) and the tracked pages above.
- Project list and contributor standards: [`CONTRIBUTING.md`](CONTRIBUTING.md).
- Legacy Applesoft BASIC architecture: [back-pocket-basic-legacy](https://github.com/Bad-Mango-Solutions/back-pocket-basic-legacy).
