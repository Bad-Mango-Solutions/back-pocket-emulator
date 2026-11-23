# Host and UI

## Summary

This page is a pointer to the host-facing specification and the projects that implement the desktop UI and debugger hosts. It does not move or duplicate sections from the v1.0 architecture specification.

## Covers

- The desktop host (`bpui`) and debugger hosts (`emudbg` and `emudbg-mcp`).
- The target host assembly/module layout and its existing namespace boundaries.
- Links to the host implementation projects and their current locations during the consolidation moves.

## Does not cover

- CPU, bus/MMU, devices, scheduler, machine composition, or emulator internals.
- A moved or rewritten chapter from the v1.0 architecture specification. The [Emulator UI Specification](../Emulator%20UI%20Specification.md) remains the UI specification source.
- Host redesign or changes to current host behavior.

## UI specification

See [`specs/Emulator UI Specification.md`](../Emulator%20UI%20Specification.md) for the emulator UI design and behavior.

## Target host surfaces

The target layout is defined by [#478](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/478) and [`Solution Consolidation Design.md`](../Solution%20Consolidation%20Design.md).

| Target surface | Assembly and executable | Role |
| --- | --- | --- |
| Desktop UI host | [`BadMango.Emulator.UI`](../../src/BadMango.Emulator.UI/) (`bpui`) | Host shell. It absorbs Configuration, Infrastructure, and UI.Abstractions; `bpui` still does not reference the machine. Existing namespaces such as `BadMango.Emulator.Configuration`, `BadMango.Emulator.Infrastructure`, and `BadMango.Emulator.UI.Abstractions` remain unchanged. |
| Debugger UI | [`BadMango.Emulator.Debug.UI`](../../src/BadMango.Emulator.Debug.UI/) | Debugger windows and the text editor. It absorbs TextEditor; the existing `BadMango.Emulator.TextEditor` namespace remains unchanged. |
| Debug console | [`BadMango.Emulator.Debug`](../../src/BadMango.Emulator.Debug/) (`emudbg`) | Separate executable host for the shared debugger surface. |
| Debug MCP host | [`BadMango.Emulator.Debug.Mcp`](../../src/BadMango.Emulator.Debug.Mcp/) (`emudbg-mcp`) | Separate stdio MCP executable. Its only direct project reference remains `BadMango.Emulator.Debug.Infrastructure`. |
| Debugger infrastructure | [`BadMango.Emulator.Debug.Infrastructure`](../../src/BadMango.Emulator.Debug.Infrastructure/) | Shared debugger contracts and command/session infrastructure. It absorbs Debug.Abstractions; the existing `BadMango.Emulator.Debug.Abstractions` namespace remains unchanged. |

`emudbg` and `emudbg-mcp` stay separate processes. `emudbg-mcp` does not directly reference `BadMango.Emulator.Debug.UI`.

## Pending source moves

> **Current location (until #559):** `src/BadMango.Emulator.Configuration`, `src/BadMango.Emulator.Infrastructure`, and `src/BadMango.Emulator.UI.Abstractions`; target: `src/BadMango.Emulator.UI` (namespaces unchanged).

> **Current location (until #559):** `tests/BadMango.Emulator.Infrastructure.Tests` and `tests/BadMango.Emulator.Configuration.Tests`; target: `tests/BadMango.Emulator.UI.Tests` (namespace unchanged).

> **Current location (until #560):** `src/BadMango.Emulator.Debug.Abstractions`; target: `src/BadMango.Emulator.Debug.Infrastructure` (namespace `BadMango.Emulator.Debug.Abstractions` unchanged).

> **Current location (until #560):** `tests/BadMango.Emulator.Debug.Abstractions.Tests`; target: `tests/BadMango.Emulator.Debug.Infrastructure.Tests` (namespace unchanged).

> **Current location (until #561):** `src/BadMango.Emulator.TextEditor`; target: `src/BadMango.Emulator.Debug.UI` (namespace `BadMango.Emulator.TextEditor` unchanged).

> **Current location (until #561):** `tests/BadMango.Emulator.TextEditor.Tests`; target: `tests/BadMango.Emulator.Debug.UI.Tests` (namespace unchanged).

