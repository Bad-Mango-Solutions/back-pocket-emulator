# Modular Solution Layout

`BackPocketEmulator.slnx` splits the 65C02 emulator into one project per responsibility. On disk, projects are direct children of `src/` and `tests/`. The solution file groups those same projects into virtual folders for the IDE: `/src/emulator/`, `/src/emulator/infrastructure/`, `/src/emulator/ui/`, and `/tests/`.

The contributor-facing map, including every project reference, is [CONTRIBUTING.md](CONTRIBUTING.md#project-structure). The Applesoft BASIC interpreter moved to [back-pocket-basic-legacy](https://github.com/Bad-Mango-Solutions/back-pocket-basic-legacy). Namespaces in this repository are `BadMango.Emulator.*`.

## Libraries

| Project | Responsibility |
| --- | --- |
| `BadMango.Emulator.Core` | CPU, bus, and emulation: contracts and state, signal bus, machine profiles, `MainBus`, `MachineBuilder`, and `Cpu65C02` (`Cpu65816` and `Cpu65832` are placeholders). Namespaces `BadMango.Emulator.Bus` and `BadMango.Emulator.Emulation` stay |
| `BadMango.Emulator.Devices` | Keyboard, video, speaker, language card, Disk II, clock cards, game I/O |
| `BadMango.Emulator.Storage` | Disk-image formats, storage backends, GCR nibblizing |
| `BadMango.Emulator.Rendering` | Pixel buffers and character rendering |
| `BadMango.Emulator.Systems` | Pocket2e machine composition |
| `BadMango.Emulator.TextEditor` | Avalonia text editor |
| `BadMango.Emulator.Debug.Infrastructure` | REPL, commands, breakpoints, trace, and debug session contracts. Namespace `BadMango.Emulator.Debug.Abstractions` stays |
| `BadMango.Emulator.Debug.UI` | Avalonia windows hosted by the console debugger |

## Hosts

| Project | Assembly | Responsibility |
| --- | --- | --- |
| `BadMango.Emulator.Debug` | `emudbg` | Console debugger. References Core, Debug.Infrastructure, and Debug.UI |
| `BadMango.Emulator.UI` | `bpui` | Avalonia GUI. References Core. Holds the event aggregator, application settings, and UI contracts |

## Tests

`tests/BadMango.Unit.Components` is the shared test library. Every `tests/BadMango.Emulator.*.Tests` project references it and the library it covers. `BadMango.Emulator.Core.Tests` covers the bus, memory map, and device registry. `BadMango.Emulator.Debug.Infrastructure.Tests` covers the REPL and the debug session contracts (namespace `BadMango.Emulator.Debug.Abstractions.Tests` stays). `BadMango.Emulator.Tests` is the cross-project suite for the CPU, machine builder, Systems, and Core.

## Naming

- CPU types are `Cpu65C02`, `Cpu65816`, and `Cpu65832`.
- A library and its tests share the library name plus a `.Tests` suffix, except for the cross-project suite `BadMango.Emulator.Tests` and the helper library `BadMango.Unit.Components`.
