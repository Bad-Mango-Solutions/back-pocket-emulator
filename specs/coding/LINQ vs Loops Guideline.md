# LINQ vs Loops Guideline

## Decision

Use hand-written loops in hot paths. Use LINQ in cold paths and tests when it makes the
operation easier to read.

A hot path is code that runs per CPU cycle, per bus access, per rendered frame, per audio
block, per scheduled event, or otherwise enough times that small costs accumulate. CPU step,
dispatch, memory access, rendering, device timing, and inner emulation loops are hot unless a
measurement establishes otherwise.

A cold path is normally run once or in response to a user action, including machine
construction, configuration, reset, diagnostics, UI setup, and tests. Readability is the
primary concern there.

The rule applies to allocation frequency, not merely to the class containing the code. A
helper in `Machine` can be cold, and a LINQ-looking helper called by the CPU loop is still
hot. When a loop crosses that boundary, use the hottest caller to classify it.

## Review guidance

Prefer a loop in a hot path when it:

- avoids a deferred iterator, delegate invocation, closure, or intermediate collection;
- fuses filtering, transformation, and aggregation in one pass;
- exposes indexed access, bounds-check elimination, early exit, or state accumulation to the
  JIT; or
- avoids an allocation that would occur once per cycle, event, pixel, or frame.

Prefer LINQ in cold paths and tests when a named operation such as `Where`, `Select`,
`ToArray`, `Any`, or `FirstOrDefault` communicates the intent more directly than the loop.
Keep `foreach` when the body owns side effects, has several conditions, maintains ordering
state, or must stop and resume in ways that obscure a LINQ pipeline. Do not rewrite clear code
solely to satisfy a style suggestion.

Neither form is automatically fastest. Iterator specialization, array access, inlining,
allocation escape analysis, and tiered compilation can change the result. Measure representative
Release workloads with BenchmarkDotNet before changing a disputed hot path.

## .NET 10 observations

BenchmarkDotNet 0.15.8 ran on .NET 10.0.15 with RyuJIT, Release configuration, server GC, and
`[MemoryDiagnoser]`. These sandbox measurements are useful for relative comparison, not as
portable absolute targets.

The benchmarks are in
`benchmarks/BadMango.Emulator.Benchmarks/Coding/`:

| Shape | Hand-written loop | LINQ | LINQ / loop |
| --- | ---: | ---: | ---: |
| Hot: filter and sum 32 integers | 19.00 ns, 0 B | 81.01 ns, 48 B | 4.26x time |
| Cold: invoke eight factories, retain four results | 40.23 ns, 120 B | 122.91 ns, 248 B | 3.06x time, 2.07x allocation |

The hot case demonstrates why repeated LINQ cost matters: the indexed loop was both faster and
allocation-free. The cold case also had a measurable difference, but about 83 ns and 128 B per
machine-build-shaped operation is normally insignificant compared with its useful work. It is
therefore reasonable to choose whichever cold-path form is clearer.

The JIT can remove abstraction and optimize loops, especially for arrays and indexing, but it
still has to preserve iterator and delegate semantics. A LINQ pipeline can allocate enumerators
or closures and indirect calls before inlining decisions are available. Materializing results
also allocates the output collection. Conversely, a hand-written loop can allocate when it
builds or grows a collection; LINQ does not inherently allocate more in every case.

Reproduce with:

```bash
dotnet run -c Release \
  --project benchmarks/BadMango.Emulator.Benchmarks/BadMango.Emulator.Benchmarks.csproj \
  -- --filter '*LoopVsLinqBenchmark*'
```

## Enforcement

Use this note and normal code review as the initial enforcement. Do not disable a LINQ
suggestion repo-wide: the rule is context-sensitive, and doing so would hide useful cold-path
and test suggestions.

If GitHub Code Quality suggestions identify a stable Roslyn rule ID, add a narrowly scoped
suppression for the affected hot-path file or directory at `silent`/`none`, with the reason in
this guideline. Do not suppress the rule for `tests/`, benchmark code, configuration code, or
the entire repository. If the suggestion is a GitHub Code Quality configuration rather than a
compiler analyzer, dismiss or configure only the hot-path path pattern. No
`.editorconfig` change is needed for this spike because it must preserve the warning-free build
and the available findings did not include a rule that can be scoped safely.

## Scope and follow-up

This spike does not change production hot paths.

- Keep the `MachineBuilder.Build` factory loop as-is for now. It is a cold, side-effecting
  construction sequence, and the loop clearly checks and registers each created component.
- Keep the filtering loop in `Machine` as-is if the inspected code remains reset-time work;
  apply the hot-path rule to it only if a benchmarked caller makes reset frequent.
- Keep test loops when stateful scheduling or dispatch makes them clearer; otherwise tests may
  use LINQ for assertions and setup.
- Add a focused benchmark before changing component lookup or other loop-like LINQ in `Machine`
  if callers can reach it from a repeated emulation path.
- In a follow-up, audit LINQ calls in CPU step, bus access, rendering, timing, and per-event
  device code and replace only measured or clearly repeated allocation/iteration costs.

