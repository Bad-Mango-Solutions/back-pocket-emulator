# Benchmarking Specification

**Epic:** #493 · **Design issue:** #494 · **First slice:** #495
**Status:** Draft

## 1. Purpose

Benchmarks are noisy and expensive, but they're required whenever a change touches how instructions execute. This spec defines a permanent, extensible benchmarking setup so that changes to CPU dispatch, addressing modes, the bus, video or disk I/O come with evidence that performance didn't get worse.

The motivating gap: the MHz loops used for PR #436 never executed the opcodes that PR converted. A benchmark must exercise the code that changed, or it measures nothing.

## 2. Principles

1. **Measure the changed code.** Every performance claim names the workload and shows that workload runs the changed path.
2. **Same machine, before vs after.** Numbers are only compared against a baseline taken on the same machine, runtime and configuration. Absolute numbers across machines are not meaningful.
3. **Opt-in, not per-push.** Benchmarks never run in the normal test run and never gate merges automatically.
4. **Extend by adding, not editing.** A new benchmark is a new class (and optionally a new workload file). No central registry or switch to update.
5. **Allocation-free hot paths.** Instruction execution must allocate 0 bytes per instruction; benchmarks always report allocations.

## 3. Project layout

```
benchmarks/
  BadMango.Emulator.Benchmarks/
    BadMango.Emulator.Benchmarks.csproj   (console exe, BenchmarkDotNet)
    Program.cs                             (BenchmarkSwitcher.FromAssembly(...).Run(args))
    Cpu/          Dispatch, addressing modes, per-opcode
    Bus/          Reads/writes, page mapping, soft switches
    Video/        Frame composition, text/lo-res/hi-res renderers
    Disk/         Nibble/block paths, SmartPort traps
    Workloads/    WorkloadLibrary + embedded workload sources/binaries
    Harness/      Bare-CPU and full-machine fixtures shared by benchmarks
  baselines/      Saved JSON results (see §7)
  tools/          Compare tool (see §7)
```

- The project is in `BackPocketEmulator.slnx` so it builds with everything else, but it has no test adapter and is not discovered as tests.
- The BenchmarkDotNet version is pinned centrally in `Directory.Packages.props`.
- Repo rules apply: .NET 10, no unsafe code, no static mutable state, no empty catches.

Each area folder holds plain BenchmarkDotNet classes. Categories (`[BenchmarkCategory("Cpu", "Dispatch")]`) let a run select an area with `--anyCategories` or `--filter`.

## 4. Fixtures

Two fixtures cover nearly every case:

| Fixture | Contents | Use |
|---|---|---|
| `BareCpuFixture` | `Cpu65C02` on a flat 64 KB RAM bus, no devices, no scheduler | Per-opcode and dispatch micro-benchmarks, isolating the CPU |
| `MachineFixture` | A headless machine built from a profile (via the #435 headless host) | Bus, video, disk and whole-system workloads |

Fixtures build their state in `[GlobalSetup]`, reset registers and memory in `[IterationSetup]` only where needed, and never touch the filesystem inside the measured region.

## 5. Workloads

A **workload** is a small 6502/65C02 program plus metadata:

```
Name, Description, CpuFamily (6502 | 65C02), LoadAddress, EntryPoint,
CyclesPerLoop (measured once and asserted), Exercises (list of opcodes / subsystems)
```

- Stored as assembly source with a pre-assembled binary embedded as a resource, so benchmarks don't depend on the assembler at run time.
- `Exercises` is checked by a unit test in the normal test project that steps the workload once and verifies that each listed opcode actually executes. This is what enforces principle 1.
- Initial workloads:
  - `mix`: the existing general instruction mix.
  - `ram`: the existing RAM sweep.
  - `flags-transfer`: a loop dominated by CLC/SEC/CLI/SEI/CLV/CLD/SED and TAX/TXA/TAY/TYA/TSX/TXS (for #436).
  - Later, one workload per migrated instruction group in #282–#288.

## 6. Benchmark tiers

| Tier | What it measures | Output |
|---|---|---|
| **Per-opcode** | One opcode through the real dispatch path, repeated N times per invocation (`OperationsPerInvoke`) | ns/instruction, B allocated/instruction |
| **Workload MHz** | A workload run for a fixed cycle count on `BareCpuFixture` | Effective MHz, B allocated |
| **System** | A workload or boot sequence on `MachineFixture` | Wall time, frames/sec or MHz |

Per-opcode benchmarks are parameterized (`[ParamsSource]`) over the opcodes in a group, so a migration PR adds its opcodes to a list rather than writing new methods.

## 7. Run configuration, baselines and comparison

### Run profiles

| Profile | Use | Settings |
|---|---|---|
| `quick` | Local iteration | ShortRun job, 1 launch |
| `default` | PR evidence | 3 launches, default warmup/iterations, `MemoryDiagnoser` |
| `ci` | CI workflow | Same as `default`, JSON exporter on |

Selected with `--profile <name>`; implemented as BenchmarkDotNet `ManualConfig`s in the project.

### Baselines

- Results export as BenchmarkDotNet full JSON to `benchmarks/baselines/<machine-id>/<short-sha>.json`.
- Baselines are written by hand or by CI; they're committed only when they document a milestone (for example, the before/after of a migration PR). Day-to-day results stay local or in CI artifacts.

### Compare tool

`benchmarks/tools/Compare` (a small console app) takes two result files and prints a Markdown table per benchmark: before, after, delta %, and whether the confidence intervals overlap.

- **Regression threshold:** a benchmark is flagged when it is more than **5% slower** and the 99.9% confidence intervals don't overlap, or when allocations go from 0 to non-zero.
- Exit code 0 when nothing is flagged, 1 when something is, so CI can mark the run.

## 8. CI

A `benchmarks.yml` workflow:

- **Triggers:** `workflow_dispatch` (inputs: base ref, head ref, filter) and a `run-benchmarks` label on a PR. Never on every push.
- **Steps:** build Release, run the `ci` profile on base then head (interleaved by launching both runs back to back in the same job), run the compare tool, upload the JSON results and the Markdown table as artifacts.
- Shared runners are noisy, so CI results are a smoke check. Merge-relevant evidence for CPU changes comes from same-machine local runs.

## 9. Reporting in PRs

A PR that touches execution paths includes:

- The workloads and per-opcode groups run, and evidence they exercise the changed code
- A before/after table from the compare tool, with base and head commits, machine, runtime and profile
- Allocation results

## 10. Later

- `emudbg bench <workload>` against a live headless machine (#435 host), reusing the workload library.
- Nightly trend runs on a fixed machine, if one becomes available.
- Video and disk benchmark areas as #219 and the video pipeline work land.

## 11. Implementation sub-issues

1. **#495:** project, `BareCpuFixture`, per-opcode dispatch benchmarks, `flags-transfer` workload; validate PR #436.
2. Workload library with metadata and the `Exercises` verification test; port `mix` and `ram`.
3. Run profiles and JSON export; the compare tool with the regression threshold.
4. `benchmarks.yml` CI workflow.
5. `MachineFixture` and the first system benchmark.
6. Per-group opcode lists and workloads for each of #282–#288 as they land (added by each migration PR).
