# AGENTS.md

Guidance for coding agents working in this repository. Start architecture navigation at [`ARCHITECTURE.md`](ARCHITECTURE.md). Coding standards, Serilog, and the Release warn-as-error bar live in `.github/copilot-instructions.md`. This file is how to drive `emudbg`.

## Build

```bash
dotnet build BackPocketEmulator.slnx -c Release -warnaserror
dotnet test BackPocketEmulator.slnx -c Release
```

The 65C02 SingleStepTests full category (`SingleStepFull`) is not part of that test command. CI runs it with `tests/BadMango.Emulator.Core.Tests/singlestep-full.runsettings`.

The debug host project is `src/BadMango.Emulator.Debug`. Its assembly name is `emudbg`.

The stdio MCP host is `src/BadMango.Emulator.Debug.Mcp`. Its assembly name is `emudbg-mcp`. stdout is the protocol. Serilog writes to stderr and `logs/debug-mcp-.log` under the application directory. Sandbox roots are the `DebugMcp:SandboxRoots` list of absolute directories. An empty list denies sandboxed file access.

Inspection tools are `get_registers`, `read_memory`, `write_memory`, and `disassemble`. Breakpoint tools are `set_breakpoint` and `set_watchpoint`. `read_memory` length is 1 through 4096 and defaults to 16. `write_memory` accepts at most 256 bytes. `disassemble` count is 1 through 256 and defaults to 16. Addresses accept `$` hex, `0x` hex, decimal, or a soft-switch name. Bus faults use the shared `FaultKind` formatter.

I/O tools are `get_screen_text`, `type_keys`, `mount_disk`, `disk_list_files`, and `disk_read_file`. `mount_disk` takes `path`, `slot` (default 6), `drive` (default 1), and `writeProtect` (default true). A caller sets `writeProtect` false to allow writes. `disk_list_files` and `disk_read_file` take either an `image` path or both `slot` and `drive`, plus the volume `path` or `filename`. A relative image path is resolved against the first `DebugMcp:SandboxRoots` entry, then checked like an absolute path. `disk_read_file` defaults to the logical view (`raw` selects on-disk bytes). A DOS text, ProDOS TXT, or Pascal text file in that view returns `encoding` `text` (DOS text stops at the first `$00`). Every other file returns `encoding` `base64`. A file larger than `DebugMcp:MaxDiskFileBytes` (default 1 MiB) returns an error envelope. The tool does not write a new host file. Reading a dirty read-write drive flushes that drive to the host image. A write-protected drive is not flushed. `..` that leaves a root, an absolute path outside the roots, and a symlink whose target leaves the roots are rejected. The link target is resolved with `FileSystemInfo.ResolveLinkTarget` before that check. Catalog display names use the same `\c{X}`, `\i{X}`, and `\f{X}` escaping as `disk cat`. File lookup matches `disk read`: exact name, then a case-insensitive match only when it is unique. The returned path is the catalog name.

```json
{
  "servers": {
    "emudbg-mcp": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "exec",
        "src/BadMango.Emulator.Debug.Mcp/bin/Release/net11.0/emudbg-mcp.dll"
      ]
    }
  }
}
```

Put that document at `.vscode/mcp.json`. The same `servers` entry is the `mcp.json` shape. Build the host before the client launches it. Do not add a console logger on stdout.

## Driving emudbg

Prefer a batch over a piped interactive session. Option names are case-insensitive.

```bash
dotnet run --project src/BadMango.Emulator.Debug/BadMango.Emulator.Debug.csproj -- \
  --headless --no-banner --json \
  --exec "regs;mem \$0200 16;dasm \$1000 --instructions=4;exit"
```

- `--exec` / `-e` and `--file` / `-f` run commands and exit. End a sequence with `exit` when you need a clean success.
- `--json` makes `regs`, `mem`, `peek`, `dasm`, `step`, `run`, `run-until`, `bp`, `watch`, `reset`, `abort`, and `maptrace` print an `emudbg/v1` envelope. Parse `payload`. Do not scrape the text boxes.
- `--agent` is headless + JSON + non-interactive.
- `--max-cycles <n>` is the default `RunBudget` for `run`, `run-until`, and `step`.
- `--seed` exits 2. There is no seeded RNG to attach it to.
- Exit codes: 0 success, 1 the last command failed or the script file is missing, 2 usage error. Read stderr for the message.
- `--headless` must not load Avalonia. Do not add a reference that pulls the debug UI into that path.

`run-until` conditions: `$addr`, `bp`, `watch`, `mem <addr> <byte>`, `--until-cycles=<n>`.

## Where the commands live

- `src/BadMango.Emulator.Debug/Program.cs` parses options, selects headless, and returns the exit code.
- `EmudbgOptionTable` is the only option list. `EmudbgOptions` and `DebugHostArguments` both use it.
- `DebugRepl` derives interactivity from the options and `Console.IsInputRedirected` when the caller does not set `IsInteractive`.
- Session commands take `IDebugSession` from DI. Do not call `new MachineDebugSession` inside a command.
- `DebugOutputRenderer` chooses text or the source-generated envelope. Do not serialize anonymous objects.

More command syntax is in `emudbg.md`.