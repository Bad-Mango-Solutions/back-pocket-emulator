# emudbg

`emudbg` is the debug console for the Back Pocket emulator. It is a REPL for people and a batch host for scripts and agents. Inspection commands call the shared `IDebugSession`. With `--json`, their output is one `emudbg/v1` envelope from `DebugSessionJsonContext`.

## Launch

```bash
dotnet run --project src/BadMango.Emulator.Debug/BadMango.Emulator.Debug.csproj
```

Option names are case-insensitive (`--JSON` and `--json` are the same flag).

| Option | Meaning |
| --- | --- |
| `-p, --profile <name>` | Load this machine profile at startup |
| `-e, --exec <commands>` | Run semicolon- or newline-separated commands, then exit |
| `-f, --file <path>` | Run commands from a file (`#` starts a comment), then exit |
| `--json, -j` | Write session command results as `emudbg/v1` JSON |
| `--no-banner` | Suppress the banner and prompt |
| `--headless` | Do not load the Avalonia debug UI |
| `--agent` | Headless, JSON, and non-interactive |
| `--max-cycles <n>` | Default cycle budget for `run`, `run-until`, and `step` |
| `--seed <n>` | Rejected. The machine has no seeded random-number source |
| `-h, --help` | Show usage and exit 0 |

A missing value, an unknown flag, or a bad number is a usage error: the message goes to stderr and the process exits 2. A missing script file also goes to stderr and exits 1. The exit code of a batch is the last command: 0 success, 1 failure, 2 usage error.

## Examples

```bash
emudbg --profile pocket2e-lite --exec "regs;step 4;exit" --no-banner
emudbg --file scripts/boot-check.emudbg --json --agent
emudbg --headless --max-cycles 100000 --exec "run-until --until-cycles=1000;exit"
```

## Commands that use the session

`regs`, `mem`, `peek`, `dasm`, `step`, `run`, `run-until`, `bp`, `watch`, `reset`, and `abort` call the process-wide `IDebugSession`. `disk-list --json` uses the same envelope. `run-until` accepts a PC (`$0801`), `bp`, `watch`, `mem <addr> <byte>`, and `--until-cycles=<n>`. A bad `run-until` argument is exit code 2.

`abort` shows or sets how a 65C02 treats `SignalLine.Abort`. With no argument it prints the current reaction. `abort noop` selects `NoOp` (the default: the line is ignored and an unmapped fetch still stops with STP). `abort hostbreak` selects `HostBreak` (the next instruction boundary requests a host stop and does not set STP). The command does not pulse the line. A machine reset does not clear the setting. `HostBreak` is rejected when the attached CPU is not a 65C02. With `--json`, the result is one `emudbg/v1` envelope whose payload is `{ "reaction": "NoOp" }` or `{ "reaction": "HostBreak" }`.

`mem` and `peek` keep going across bus faults. Text shows `??` for each unreadable byte. JSON adds a `faults` hex mask beside `data`: `00` means that data byte is valid, and any non-zero byte is a bitmask of fault flags (`0x01` Unmapped, `0x02` Permission, `0x04` Nx, `0x08` Misaligned, `0x10` DeviceFault). A faulted data byte is 0 and is not memory contents. The mask is omitted when every byte was readable.

JSON for those commands looks like:

```json
{
  "schema": "emudbg/v1",
  "payload": {},
  "timestamp": "2020-01-01T00:00:00+00:00"
}
```

## Interactive use

```text
> regs
> step 5
> dasm
> bp add $c030
> run-until bp
> exit
```

`help` and `help <command>` list the rest, including disk, trace, and profile commands.
