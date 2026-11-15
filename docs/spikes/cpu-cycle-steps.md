# Spike: composed per-cycle 65C02 core

## Verdict

**Do not remodel.** Keep the existing compositional core and proceed with the optimizations already planned.

A decode table of shared per-cycle steps is less error-prone about timing, and it emits the real chip's dummy reads. It is not meaningfully less code once the whole 65C02 is accounted for, and it is slower. Josh's bar was a real gain in both less coding and faster execution. This spike hits neither bar hard enough to justify replacing the core. A clear no.

No phased remodel is proposed. The cycle-count bugs below are a small correction in the existing addressing-mode wiring, not a reason to replace the execution engine.

## What was compared

The shipping core (`Cpu65C02`) fetches an opcode, then calls one delegate per opcode. That delegate is an instruction function closed over an addressing-mode function. Both functions perform their bus reads and writes immediately and add a hand-counted amount to `Registers.TCU`. `Step` then calls `Scheduler.Advance` once with that total. There is no per-cycle loop. Dummy reads are not performed. A 16-bit operand fetch is one `Read16`, which the bus may decompose internally, while the CPU still adds 2 to `TCU` by hand.

The handler layer is about 2,191 lines of non-blank, non-comment code:

| Area | Code lines | Raw lines |
| --- | ---: | ---: |
| `AddressingModes.cs` | 502 | 794 |
| `Cpu65C02OpcodeTableBuilder.cs` | 275 | 398 |
| `Instructions.*.cs` | 1,414 | 2,325 |
| Total handler layer | 2,191 | 3,517 |

`Cpu65C02.cs` (326 code lines) and `CpuBase.cs` (303) are the step loop, bus helpers, reset, and interrupts. A remodel would keep that machinery. It is not part of the handler comparison.

Cycle accounting in that layer is separate data the programmer maintains: **101** `TCU +=` sites and **31** `instructionCycles:` overrides. Those numbers are how STZ and INC/DEC abs,X drifted off hardware.

The spike lives entirely under `src/BadMango.Emulator.Emulation/Cpu/Spike/`. It does not replace `Cpu65C02` and does not change existing tests. Each covered opcode is a precomputed list of at most 8 step bytes plus an effect byte, built by concatenating a shared addressing sequence with an operation (`LoadA`, `StoreA`, `StoreZero`, `Inc`, `Asl`, `BranchIfZ`). `Execute` fetches the opcode, then runs that list in one switch loop. Skipped steps (the index penalty when the page does not change, a branch not taken) perform no bus cycle. When a trace buffer is passed, every bus cycle is recorded. When it is not, the instruction still runs to completion and `Step` advances the scheduler once. Both cores use the same `MainBus` over 64KB of `RamTarget`.

Covered opcodes (17): `LDA #`, `LDA zp`, `LDA zp,X`, `LDA abs`, `LDA abs,X`, `STA abs,X`, `STZ abs,X`, `INC abs,X`, `ASL abs,X`, `LDA (zp),Y`, `LDA (zp)`, `JMP (abs)`, `BEQ`, `JSR`, `RTS`, `PHA`, `PLA`.

## Speed

Release build, .NET 10.0.14, `Stopwatch` timestamps. 20,000 warmup loops, then 7 trials of 100,000 loops (2,500,000 instructions). Trial order alternates which core runs first. Each trial reloads the same RAM image. Median is the reported number. `GC.GetAllocatedBytesForCurrentThread` is measured on a separate trial after a full collection.

The mix is one loop of the 17 opcodes, including a page-crossing and a non-crossing form of the indexed instructions, a taken branch, a not-taken branch, a page-crossing branch, `JSR`/`RTS`, and `PHA`/`PLA`. It is not an Apple II software trace. It is the slice the question asked about, each form once. One loop is 25 instructions, 113 real bus cycles on the spike, and 111 cycles as the shipping core counts them.

| | Shipping `Cpu65C02` | Cycle-step spike |
| --- | ---: | ---: |
| Median time | 0.138 s | 0.320 s |
| Trial range | 0.120–0.147 s | 0.291–0.363 s |
| Instructions/sec | 18.1 million | 7.8 million |
| Counted MHz | 80.3 | 35.3 |
| Hardware-equivalent MHz | 81.7 | 35.3 |
| Allocated bytes | 0 | 0 |

Hardware-equivalent MHz scores both cores against the spike's 113 bus cycles per loop, so the shipping core is not rewarded for under-counting STZ and INC. The ranges do not overlap. The shipping core runs this mix at about **2.3×** the spike's instruction rate.

The comparison favors the spike on everything except the model under test. The spike keeps registers in plain bytes. The shipping core goes through the 65816-wide register structs, a trap-registry check, and debug-listener checks. The spike still loses. The loss is the per-cycle model: every step, including dummy re-reads of the operand byte and the CMOS read-modify-write idle read, is a `TryRead8` or `TryWrite8` plus a switch. The shipping core does not perform those dummy accesses, and it fetches a 16-bit operand with one `Read16`.

## Simplicity

Spike engine, excluding the benchmark harness (`SpikeMemoryRig`, `SpikeInstructionMix`):

| File | Code lines | Raw lines |
| --- | ---: | ---: |
| `CycleStepCpu.cs` | 347 | 469 |
| `CycleProgramTable.cs` | 87 | 209 |
| `ObservedCycle.cs`, `CycleStepRegisters.cs` | 4 | 29 |
| Engine total | 438 | 707 |

That 438 lines implements 17 opcodes. The fixed cost is the interpreter. Adding an opcode whose bus pattern already exists is one `Compose` call. Adding a new bus pattern is a new step kind and a new switch arm.

Extrapolating to a full 65C02, not by multiplying the slice:

- The shipping handler layer is already the full chip: 2,191 code lines, 256 opcode slots, 101 hand-written cycle additions.
- A full step table is about 210 `Compose` lines (one per defined opcode) plus the effect and step enums.
- The interpreter does not stay at 347 code lines. Still to write: Y-index as a parameter on the existing index steps, `(zp,X)`, accumulator and implied ops, the other seven branches (a flag mask on the branch step already sketched), `ADC`/`SBC` including decimal, compares, `BIT`, shifts other than `ASL`, `RMB`/`SMB`, `BBR`/`BBS`, `BRK`/`RTI`, and the odd NOP timings (`$5C` is 8 cycles). Decimal `ADC` alone is a large part of `Instructions.Arithmetic.cs` and does not get smaller by moving it into a step.
- A fair estimate of the finished step-based handler layer is roughly 1,600–2,000 code lines against 2,191 today. The saving is the duplicated addressing-mode bodies and the deleted `TCU +=` sites. It is a few hundred lines, not a different order of magnitude.

The error-proneness win is real and separate from the line count. The spike stores no cycle numbers. `STZ abs,X` and `INC abs,X` differ by one step (`ForcedPenalty` versus `OptionalPenalty`), which is why one is 5 cycles always and the other is 7 always, while `ASL abs,X` stays 6 or 7. That is the bug class in the shipping core, and the step list makes it structural. It is not, by itself, enough code reduction to pay for a slower interpreter.

### 65816 and 65832

The shipping addressing modes already branch on the direct-page register and on `GetAccumulatorSize` / `GetIndexSize`. A 65816 still means new modes (absolute long, stack relative, indirect long, block move) and an 8/16-bit cycle rule on almost every existing mode, each one another hand-counted conditional. The compositional "one `ADC`" shape still helps. The cycle bugs multiply.

A step table scales more cleanly there: a 16-bit operand is another read or write step, selected by M or X, and "DL ≠ 0" is another optional penalty. `MVN`/`MVP` are a loop of steps. That is the best argument for the model, and it shows up as correctness, not speed. Wider accesses mean more interpreted bus cycles, so the slowdown measured here gets worse, not better. The 65832 case is the same shape again (another width, privilege already enforced by the bus). None of that is a reason to slow down the 65C02 core now.

## Correctness

Ground truth is the WDC 65C02 set from [SingleStepTests/65x02](https://github.com/SingleStepTests/65x02) `wdc65c02/v1`, 10,000 cases per opcode. The spike was checked cycle by cycle (address, data, read/write). The shipping core was checked on final registers, final RAM, and the cycle count only. The shipping core was not modified.

Committed tests use a stratified subset (615 cases) so CI does not download or store the ~70MB of JSON. The full 170,000-case run was executed locally against `/tmp` copies of the upstream files and is not committed. `FullSet_WhenLocalVectorsPresent` repeats it when `CYCLE_SPIKE_FULL_DIR` points at those files. It is `[Explicit]`, so a normal `dotnet test` skips it.

| | Spike, full set | Shipping core, full set |
| --- | ---: | ---: |
| Cases | 170,000 | 170,000 |
| Final-state mismatches | 0 | 0 |
| Cycle or bus-trace mismatches | 0 | 9,913 |

Shipping-core cycle mismatches, and they match the no-page-cross population of the same files exactly:

| Opcode | Mismatches | Same file, no page cross | What the core counted |
| --- | ---: | ---: | --- |
| `STZ abs,X` `$9E` | 4,920 / 10,000 | 4,920 | 4, vector 5 |
| `INC abs,X` `$FE` | 4,993 / 10,000 | 4,993 | 6, vector 7 |
| `ASL abs,X` `$1E` | 0 / 10,000 | 5,034 | 6, or 7 when the page changes |
| `STA abs,X` `$9D` | 0 / 10,000 | 5,022 | 5 always |
| Every other opcode in the slice | 0 |  |  |

Page-crossing `STZ abs,X` and `INC abs,X` already match, because `AbsoluteX` adds the extra cycle only when the high byte changes. The failures are exactly the cases where it does not.

Cause, left unfixed on purpose:

- `$9E` is `STZ(AddressingModes.AbsoluteX)`. `$9D` `STA` uses `AbsoluteXWrite`, which always charges the extra cycle. Hardware treats `STZ abs,X` like the store: 5 cycles always. The WDC datasheet's 4 is wrong, as Bruce Clark's 6502.org 65C02 opcodes tutorial, section 7, says.
- `$FE` `INC` and `$DE` `DEC` use `AbsoluteX` as well. Hardware takes 7 cycles for `INC`/`DEC abs,X` whether or not the page changes (read, dummy read, write, plus the index cycle that stores always pay). The core charges 6 when the page does not change. `$DE` was not in the vector run. It is the same wiring as `$FE`, so the same 6-versus-7 split is expected and was not separately counted.
- `$1E` `ASL abs,X` also uses `AbsoluteX`, and that is the correct rule for the shifts: 6, or 7 on a page cross. The full set confirms it (0 mismatches). Shifts should not be switched over to the always-penalty path.

On the 65C02 the "extra" indexed cycle is a re-read of the last operand byte (the address of the high byte just fetched), not the NMOS read of the effective address with the old high byte. `JMP (abs)` still does the page-wrapped high-byte read when the pointer is `$xxFF`, then a sixth cycle reads the real next-page byte. `(zp),Y` re-reads the operand byte on a page cross. The spike follows those traces. The shipping core's final PC for `JMP (abs)` is already correct, including the `$xxFF` case, because `Read16` reads the following byte rather than wrapping. It simply does not perform the dummy wrapped read.

## Risks if this spike were taken further

- Seventeen opcodes. No decimal mode, no interrupts, no `BRK`/`RTI`/`WAI`/`STP`, no Rockwell bit ops, no debug trace, no traps.
- The step meanings are easy to get wrong from a 6502 timing sheet. Several 65C02 penalties re-read the instruction stream, not the effective address. The vectors are what made the slice correct.
- A full port would be slower on the hot path by the margin measured here, and slower still once 16-bit 65816 accesses add more interpreted cycles.
- The program table is a `static readonly` array filled in the type initializer and not written after that. It is not mutable runtime state. It is still a process-wide table.

## What to do instead

Leave the execution engine as it is.

When #422 is picked up, the change in the existing core is the wiring, not a new sequencer:

- `STZ abs,X` should use the same always-penalty indexed mode as `STA abs,X`.
- `INC abs,X` and `DEC abs,X` need that same always-penalty mode, and then the read-modify-write still adds its own read, idle, and write. Target is 7 cycles always.
- `ASL`/`LSR`/`ROL`/`ROR abs,X` should keep the optional page-cross cycle. The full set says the shipping core already matches hardware there.

Dummy reads for `$C0xx` soft switches, mid-instruction IRQ sampling, and beam racing are a different requirement from "make every instruction an interpreter." They can be a side path later: run the direct handler unless the instruction's address range is one a device needs to observe per cycle, and only then expand the steps. That keeps the fast path on the core that just measured 18 million instructions/sec. It is not this spike, and it should not be started off the back of these numbers.
