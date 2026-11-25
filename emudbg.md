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
| `--export-root <dir>` | Directory `disk read --to` may write into |
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

`regs`, `mem`, `peek`, `dasm`, `step`, `run`, `run-until`, `bp`, `watch`, `reset`, `abort`, and `maptrace` call the process-wide `IDebugSession`. `disk drives`, `disk fsinfo`, `disk cat`, and `disk read` use the same envelope when `--json` is set or the process was started with `--json`. `run-until` accepts a PC (`$0801`), `bp`, `watch`, `mem <addr> <byte>`, and `--until-cycles=<n>`. A bad `run-until` argument is exit code 2.

## Disk files

`disk fsinfo`, `disk cat`, and `disk read` recognize a DOS 3.3, ProDOS, or Pascal volume. The source is a mounted `slot:drive` (`disk fsinfo 6:1`) or an offline image (`disk fsinfo --image game.dsk`) with no machine. `disk catalog` is `disk cat`. A missing path lists the volume root. A ProDOS path such as `SUB` lists that directory. DOS 3.3 and Pascal are flat.

`disk read 6:1 HELLO` prints the logical file: a DOS A/I/B header is removed and the payload is trimmed to the header length. `--raw` keeps the on-disk bytes. `--hex` prints a hex dump. `--` ends options, so `disk read --image game.dsk -- -HELLO` reads a name that starts with `-`. This command does not replace the memory `read` command.

Inverse catalog characters display as `\i{A}`, flashing characters as `\f{B}`, and high-bit controls (`$80`–`$9F`) as `\c{G}`. The lookup name stays the visible glyph, so lowercase DOS text such as `Hello` is read with `disk read Hello`.

`--to name` writes the selected view under `--export-root`. The path must stay inside that directory. A rooted path, a `..` segment, or a symlink that leaves the root is refused. Without `--export-root`, `--to` fails.

A mounted drive with unflushed track data is flushed before the catalog or file is read. If that flush fails, the command stops and names the drive. A nibble image has no file system view; the command reports that and leaves the mounted file open.

`abort` shows or sets how a 65C02 treats `SignalLine.Abort`. With no argument it prints the current reaction. `abort noop` selects `NoOp` (the default: the line is ignored and an unmapped fetch still stops with STP). `abort hostbreak` selects `HostBreak` (the next instruction boundary requests a host stop and does not set STP). The command does not pulse the line. A machine reset does not clear the setting. `HostBreak` is rejected when the attached CPU is not a 65C02. With `--json`, the result is one `emudbg/v1` envelope whose payload is `{ "reaction": "NoOp" }` or `{ "reaction": "HostBreak" }`.

`maptrace` shows or sets mapping trace. With no argument it prints whether trace is on, the optional address range, and the fixed ring. `maptrace on` records remaps and slow-path accesses and does not change a range. `maptrace range $start $end` turns trace on and forces every 4 KB page the range intersects onto the slow path. `maptrace range clear` restores those pages and leaves trace on. `maptrace off` clears the range, restores those pages, and then disables trace. With `--json`, the result is one `emudbg/v1` envelope.

`mem` and `peek` keep going across bus faults. Text shows `??` for each unreadable byte. JSON adds a `faults` hex mask beside `data`: `00` means that data byte is valid, and any non-zero byte is a bitmask of fault flags (`0x01` Unmapped, `0x02` Permission, `0x04` Nx, `0x08` Misaligned, `0x10` DeviceFault). A faulted data byte is 0 and is not memory contents. The mask is omitted when every byte is `00`.

JSON for those commands looks like:

```json
{
  "schema": "emudbg/v1",
  "payload": {},
  "timestamp": "2020-01-01T00:00:00+00:00"
}
```

## Guest OS boot

`os` prints the detected guest operating system and the boot stage. `os boot` prints the milestone log. `os on` installs the boot monitor when the profile left it off. `boot --log` and a cold `reset --log` (`reset --hard --log` or `reset --cold --log`) install that monitor and record from reset. The first milestone is `RomInitEntered` (stage `Init`): the reset-vector target read from `$FFFC`/`$FFFD`, and `cold` or `warm`. A cold start is power-on, `reset --hard`, or Open Apple held when RESET releases (`reset --cold`). The detail also says `open-apple` in that last case. A warm reset is logged and does not move the stage unless execution then enters slot firmware.

The next milestone on the first `$Cn00` transfer is `BootDeviceSelected` (stage `Boot device` in `statmon`), then `BootFirmwareEntered` (stage `Boot ROM`). The device detail names the slot, the card, and `method autostart` (the transferring instruction was fetched from ROM), `method user` (it was fetched from RAM, or the host set PC onto the firmware), or `method open-apple`. Later stages are `Boot1`, `Loader`, and `Ready`. `CustomBoot`, `Unknown`, and `NoBootDevice` are terminal.

`NoBootDevice` (`No boot device` in `statmon`) means init reached the ROM keyboard wait without entering boot firmware. v1 sees that wait as two `LDA $C000` or `BIT $C000` instructions fetched from ROM. It does not hardcode an Apple IIe entry point. A ROM that prompts through another register does not reach this stage.

`get_guest_os` and `get_boot_log` return the same stage and milestone names. Milestone `detail` carries the cold/warm and method tokens above.

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
