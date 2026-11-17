# AGENTS.md

Guidance for coding agents working in this repository. Coding standards, Serilog, and the Release warn-as-error bar live in `.github/copilot-instructions.md`. This file is how to drive `emudbg`.

## Build

```bash
dotnet build BackPocketEmulator.slnx -c Release -warnaserror
dotnet test BackPocketEmulator.slnx -c Release
```

The debug host project is `src/BadMango.Emulator.Debug`. Its assembly name is `emudbg`.

## Driving emudbg

Prefer a batch over a piped interactive session. Option names are case-insensitive.

```bash
dotnet run --project src/BadMango.Emulator.Debug/BadMango.Emulator.Debug.csproj -- \
  --headless --no-banner --json \
  --exec "regs;mem \$0200 16;dasm \$1000 --instructions=4;exit"
```

- `--exec` / `-e` and `--file` / `-f` run commands and exit. End a sequence with `exit` when you need a clean success.
- `--json` makes `regs`, `mem`, `peek`, `dasm`, `step`, `run`, `run-until`, `bp`, `watch`, `reset`, and `abort` print an `emudbg/v1` envelope. Parse `payload`. Do not scrape the text boxes.
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
