# 65816 Opcode Table Design

Design for #568, under epic #276. Split from #288. This file is the design. It does not add types, change a builder, or edit a handler.

Grounded at `5437591` (`main` after #567 removed the legacy closure path). `Cpu65816` and `Cpu65832` are still placeholders: `Step` and `Reset` throw `NotImplementedException`. The 65C02 table is the mechanism this note extends.

#288 specializes today's handlers on the concrete CPU type. This note assumes that shape and describes the 65816 tables and the 65832 `$42` prefix on top of it. Implementing those tables stays out of #288.

## Recommendation

Build five immutable `OpcodeTable`s for the 65816: one emulation table, and one native table for each M/X pair. Build them from one opcode list. Width-sensitive handlers are the #288 methods with two extra type arguments, `TAcc` and `TIndex`. Addressing modes that differ between emulation and native mode are different `IAddressingMode` structs, the same way `ImmediateMode` and `AbsoluteXMode` are different structs today. Each closed generic gets one `static readonly OpcodeDispatch`, cached the way `LDAImmediateDispatch` is cached now.

Each CPU instance holds the active table in an instance field. `Step` loads that field and calls `OpcodeTable.Execute`. The field is replaced at the end of `REP`, `SEP`, `XCE`, `PLP`, and (recommended) `RTI` and `Reset`. The hot loop does not test M, X, or E.

`$42` stays a named `DispatchWdm` slot on every CPU. The 65C02 slot is the existing 2-byte NOP. The 65816 slot is WDM. The 65832 slot, in 32-bit mode, is the extended-opcode prefix: it fetches `xx` and calls a second `OpcodeTable`. The `$42` slot of that second table is the deeper prefix for `$42 42 xx`.

The alternative — one table, with `GetAccumulatorSize` / `GetIndexSize` left inside the handlers — is compared below. It is the smaller change and the easier one to keep consistent. It leaves a width test, a variable-length immediate, and a variable base cycle count on the common path. The five tables exist so those three are constants, which is the same reason #288 closes handlers over a concrete CPU.

## 1. What the 65C02 table does today

`OpcodeDispatch` is `void (ICpu cpu, in OpcodeEntry entry)`. `OpcodeEntry` stores that delegate and an `OpcodeInfo`. `OpcodeTable.Execute` is:

```csharp
ref readonly OpcodeEntry entry = ref entries[opcode];
entry.Fn(cpu, in entry);
```

`Cpu65C02.Step` fetches one opcode byte, then calls `opcodeTable.Execute(opcode, this)`. `opcodeTable` is a `readonly` field set once in the constructor from `Cpu65C02OpcodeTableBuilder.Build`. Unassigned slots start as `IllegalOpcodeDispatch`. The finished 65C02 table overwrites every slot.

Handlers are static methods generic over the addressing mode. The LDA immediate slot is the pattern every other slot follows:

```csharp
internal static readonly OpcodeDispatch LDAImmediateDispatch = DispatchLDAImmediate;

private static void DispatchLDAImmediate(ICpu cpu, in OpcodeEntry entry)
    => OpLDA<ImmediateMode>(cpu);

private static void OpLDA<TMode>(ICpu cpu)
    where TMode : struct, IAddressingMode
{
    Addr address = TMode.Resolve(cpu);
    byte size = cpu.Registers.GetAccumulatorSize();
    // read, flags, write A
}
```

`IAddressingMode` exposes `static abstract Addr Resolve(ICpu cpu)`. `ImmediateMode.Resolve` forwards to `AddressingModes.Immediate`, which advances PC by one byte. There is one cached delegate per opcode. Several unused NOP slots share a method (`DispatchNopImmediate`) and still each have their own `static readonly` field. Nothing is stored in a mutable static.

`$42` is the exception inside that NOP group. The builder comment and the registration keep it off `DispatchNopImmediate`:

```csharp
SetDirectOpcode(0x42, Instructions.WdmDispatch, CpuInstructions.NOP, CpuAddressingModes.Immediate);
```

`WdmDispatch` points at `DispatchWdm`, which calls `OpWdm`. `OpWdm` resolves `ImmediateMode` and adds one to `TCU`. Together with the opcode fetch in `Step`, that is the 2-byte, 2-cycle NOP. Metadata for the slot says `NOP`, not a distinct mnemonic. `CpuInstructions` has no `WDM` member yet.

`GetAccumulatorSize` and `GetIndexSize` already branch on architectural mode. `CP` set and `E` set is 8-bit. `CP` set and `E` clear is 8 or 16 from the M or X flag (`M` and `X` set means 8-bit). Any other state, which `GetArchitecturalMode` calls 65832 mode (`CP` clear), returns 32. On a 65C02 those helpers always take the 8-bit arm, because `Reset` forces `E`, `CP`, `M`, and `X` set.

Three details of the shared resolvers matter when a second CPU reuses them:

- `AddressingModes.Immediate` always consumes one byte. A 16-bit immediate needs a different mode struct.
- `AddressingModes.AbsoluteX` adds the full X register and charges a page-cross cycle whenever the sum crosses a page. The instruction-set reference charges that penalty in emulation mode. A native-mode table needs its own absolute-indexed struct.
- `AddressingModes.ZeroPage` already adds the direct-page register `D` and charges a cycle when `D` is not page-aligned. That matches the 65816 direct-page rule for a 65C02 whose `D` stays 0. Emulation mode on the 65816 keeps the direct page at 0; native mode does not. Separate mode structs keep that difference in the type argument.

`OpcodeInfo.OperandLength` is fixed when the table is built. The 65C02 builder's length helper returns 1 for every immediate. A 65816 immediate is 1 byte or 2 bytes according to M (accumulator ops) or X (index ops). The length lives on the table entry so the disassembler and the handler agree. The field is a `byte`. The comment on `OpcodeInfo` describes the 65C02 range, 0, 1, or 2. Absolute-long addressing in the instruction-set reference takes a 3-byte operand. The field can hold 3. The 65816 builder will store 3 for those opcodes. Updating the comment is part of that implementation, not part of this note.

## 2. How this sits on #288

#288 changes the handler from `Op<TMode>(ICpu cpu)` to `Op<TCpu, TMode>(TCpu cpu) where TCpu : ICpu`, closed over `Cpu65C02` when that table is built, so register and bus access inline. It keeps one cached `static readonly OpcodeDispatch` per opcode. It keeps `$42` as its own named `DispatchWdm`. It forbids unsafe code, `delegate*`, and static mutable state. Epic #276's original `delegate*` trial is not the mechanism these two issues use.

This note keeps that delegate type and that call site. A 65816 or 65832 slot is still an `OpcodeDispatch`. The generic body gains width arguments only on the instructions that read a width:

```csharp
static void OpLda<TCpu, TMode, TAcc>(TCpu cpu)
    where TCpu : class, ICpu
    where TMode : struct, IAddressingMode
    where TAcc : struct, IOperandWidth
{
    Addr address = TMode.Resolve(cpu);
    var value = cpu.ReadValue(address, TAcc.Bits);
    cpu.Registers.A.SetValue(value, TAcc.Bits);
}
```

`IOperandWidth` is the same kind of contract as `IAddressingMode`: a struct with static abstract members (`Bits`, `OperandBytes`, and the base-cycle adjustment the reference writes as "+1 if M=0"). `Acc8` and `Acc16` are two structs. Width-independent instructions stay `Op<TCpu, TMode>` as #288 specifies them. `CLC` does not grow width parameters.

The delegate signature still takes `ICpu`, because that is what `OpcodeTable.Execute` calls and what #288 keeps cached. The thin static method is the only place that turns `ICpu` into `TCpu`:

```csharp
static void DispatchLdaImmediate(ICpu cpu, in OpcodeEntry entry)
    => OpLda<Cpu65816, Immediate8, Acc8>((Cpu65816)cpu);
```

That method is stored in a `static readonly OpcodeDispatch` on a generic holder, one holder per closed `(TCpu, machine mode, TAcc, TIndex)`. The cast sits inside a monomorphic delegate target. The generic body then sees the concrete CPU type. Whether the JIT deletes the cast is a measurement for the #288 benchmark, not a behavior this note depends on. Register access after the cast is on `TCpu`.

A delegate closed over `Cpu65C02` is the wrong target for a `Cpu65816` slot. The cast would fail. Sharing a cached delegate across CPU classes is limited to methods that do not close over `TCpu`. The practical rule is: each CPU type has its own cache. The 65C02 builder keeps using `Instructions.WdmDispatch` and the other `Instructions.*Dispatch` fields. The 65816 builder references `NativeDispatch<Cpu65816, Acc8, Idx16>.LdaImmediate` and its siblings.

`Cpu65816` and `Cpu65832` are unsealed. The table is built for the concrete class named as `TCpu`. A later subclass needs its own closed caches. Sealing the two classes is an implementation choice for that PR; this note does not require it.

## 3. The five tables

Native mode has four legal widths. Emulation mode is a fifth machine, not a synonym for native M=1, X=1.

| Table | E | M | X | A | X, Y |
| --- | --- | --- | --- | --- | --- |
| Emulation | 1 | 1, forced | 1, forced | 8 | 8 |
| Native 8/8 | 0 | 1 | 1 | 8 | 8 |
| Native 8/16 | 0 | 1 | 0 | 8 | 16 |
| Native 16/8 | 0 | 0 | 1 | 16 | 8 |
| Native 16/16 | 0 | 0 | 0 | 16 | 16 |

M and X set means 8-bit, matching `IsMemorySize8Bit` / `IsIndexSize8Bit` and the instruction-set reference (bit 5 is M, bit 4 is X).

Emulation mode and native 8/8 use the same `Acc8` and `Idx8` and different mode structs. The in-repo references that force the split:

- Stack is page 1 and 8-bit in emulation mode, and a 16-bit pointer in bank 0 in native mode.
- Direct page is fixed at 0 in emulation mode and follows `D` in native mode. A direct-page access whose low byte of `D` is nonzero takes an extra cycle in native mode.
- `REP` and `SEP` cannot change M or X in emulation mode. M and X stay 1.
- `XCE` into emulation mode forces M and X to 1. Indexed page-cross penalties apply in emulation mode.
- `BRK` uses the IRQ vector in emulation mode and the native BRK vector in native mode. `RTI` pulls PBR only in native mode.
- Reset always returns to emulation mode.

Those are different instructions, not a flag test inside one ALU body. `OpLda<Cpu65816, EmulationAbsoluteXMode, Acc8>` and `OpLda<Cpu65816, NativeAbsoluteXMode, Acc8>` are two methods. The arithmetic at the bottom is the same source method. The mode struct carries the page-cross rule.

### Building them from one list

The 65C02 builder is one straight-line list of `SetDirectOpcode` calls. Copying that list five times is how the handlers would drift. The list stays one generic method. The varying parts are type arguments and the `OpcodeInfo` taken from those type arguments.

`IAddressingMode` already uses `static abstract` members. The same shape exposes a set of cached delegates:

```csharp
interface IOpcodeSet
{
    static abstract OpcodeDispatch LdaImmediate { get; }
    static abstract OpcodeDispatch LdaAbsoluteX { get; }
    static abstract OpcodeDispatch DispatchWdm { get; }
    // one member per opcode the list registers
}

static void Fill<TSet, TAcc, TIndex>(OpcodeDispatch[] dispatch, OpcodeInfo[] metadata)
    where TSet : IOpcodeSet
    where TAcc : struct, IOperandWidth
    where TIndex : struct, IOperandWidth
{
    Set(0xA9, TSet.LdaImmediate, CpuInstructions.LDA,
        CpuAddressingModes.Immediate, TAcc.OperandBytes, TAcc.LdaBaseCycles);
    Set(0x42, TSet.DispatchWdm, /* mnemonic and length come from the set */);
}
```

Five calls build the five tables:

```csharp
Fill<EmulationSet<Cpu65816, Acc8, Idx8>, Acc8, Idx8>(emulation, emulationInfo);
Fill<NativeSet<Cpu65816, Acc8, Idx8>, Acc8, Idx8>(native88, native88Info);
Fill<NativeSet<Cpu65816, Acc8, Idx16>, Acc8, Idx16>(native816, native816Info);
Fill<NativeSet<Cpu65816, Acc16, Idx8>, Acc16, Idx8>(native168, native168Info);
Fill<NativeSet<Cpu65816, Acc16, Idx16>, Acc16, Idx16>(native1616, native1616Info);
```

`NativeSet<TCpu, TAcc, TIndex>` and `EmulationSet<TCpu, TAcc, TIndex>` are the holders. Each member is a `static readonly OpcodeDispatch` whose target is one closed `OpLda<...>` (or `Op<TCpu, TMode>` for a width-independent opcode). The opcode numbers are written once, in `Fill`. Adding an instruction means one `Set` line and one member on the interface, not five copies.

Width-independent opcodes (`CLC`, `NOP`, branches that do not read A or X) point at `CpuDispatch<TCpu>.ClcImplied` from both sets. That is one cached delegate per CPU type, installed in every table that CPU owns. Width-dependent opcodes are four native closures plus an emulation closure. The JIT compiles a closed generic when that table is first built, which is during the CPU type's first construction, not during `REP`.

Base cycles that the reference writes as "+1 if M=0" or "+1 if X=0" are constants on `TAcc` or `TIndex`, stored in `OpcodeInfo.BaseCycles` for that table. Penalties that depend on the effective address stay inside the mode struct: the direct-page low-byte penalty, and the emulation-mode indexed page-cross penalty. No table can bake those in. They are address-dependent, and they are already computed inside `AddressingModes.ZeroPage` and `AddressingModes.AbsoluteX` for the 65C02.

The five `OpcodeTable` objects are immutable after `Build`. `OpcodeTable` copies the dispatch and metadata arrays into private storage and has no mutator. Hold them in `static readonly` fields on a `Cpu65816Tables` type so every `Cpu65816` instance shares them. The active reference is an instance field. One CPU's `XCE` does not change another CPU's table. Static initialization of the delegates and of the tables is the same pattern as `LDAImmediateDispatch`. It is not mutable static state.

`Cpu65832` reuses the same `Fill` with `TCpu = Cpu65832` for the five compatible tables, plus the 32-bit table in section 6. `GetAccumulatorSize` returns 32 when `CP` is clear, with no M/X split. This note follows that helper. A wider matrix inside 65832 mode is an open question.

## 4. Swapping the active table

`Cpu65C02.opcodeTable` can be `readonly` because the 65C02 has one width. `Cpu65816` stores the active table in a mutable instance field:

```csharp
private OpcodeTable active;

public override CpuStepResult Step()
{
    byte opcode = FetchByte();
    active.Execute(opcode, this);
    // cycle accounting, as in Cpu65C02.Step
}
```

`Execute` is unchanged. The per-instruction work added on top of the 65C02 path is one load of that instance field. There is no test of M, X, or E, no second delegate call, and no write. The delegate call that follows is the same megamorphic `OpcodeDispatch` call the 65C02 already makes.

`SelectTable` writes the field:

```csharp
void SelectTable()
{
    if (Registers.E)
    {
        active = Cpu65816Tables.Emulation;
        return;
    }

    active = (Registers.P & (ProcessorStatusFlags.M | ProcessorStatusFlags.X)) switch
    {
        ProcessorStatusFlags.M | ProcessorStatusFlags.X => Cpu65816Tables.Native88,
        ProcessorStatusFlags.M => Cpu65816Tables.Native816,
        ProcessorStatusFlags.X => Cpu65816Tables.Native168,
        _ => Cpu65816Tables.Native1616,
    };
}
```

Call `SelectTable` at the end of the handlers that change E, M, or X, after the handler has finished writing P and E. Forcing M and X to 1 on entry to emulation mode is part of that write. Clearing the high bytes of X and Y is open question 3; the swap itself does not do it. The hot loop never calls `SelectTable`.

| Instruction | Opcode | What changes | Table effect |
| --- | --- | --- | --- |
| `REP` | `$C2` | Clears bits in P. In emulation mode M and X stay 1. | Native mode only, and only when the mask clears M or X. Calling `SelectTable` anyway stores the same reference when the width did not change. |
| `SEP` | `$E2` | Sets bits in P. Same emulation-mode rule. | Native mode, when the mask sets M or X. |
| `XCE` | `$FB` | Exchanges C and E. Entering emulation forces M and X to 1. | The emulation table, or the native 8/8 table on the way out, under the reading in section 8. |
| `PLP` | `$28` | Pulls P. In emulation mode M and X are forced back to 1. | Native mode, when the pulled M or X differs. |
| `RTI` | `$40` | Pulls P, and PBR in native mode. | Same as `PLP` for M and X. E is not in the stacked status byte. |
| `Reset` | — | The emulator reference: reset always returns to emulation mode. `Cpu65C02.Reset` already sets `E`, `CP`, `M`, and `X`. | The emulation table. `Cpu65816.Reset` is still the placeholder. |

`REP` that only clears carry still runs `SelectTable` under the simple rule. That is a handful of bit tests and a reference store on an instruction that already rewrote P. It is off the fetch/execute path of every other opcode.

Interrupts, as the emulator reference describes them, push P, clear D, and set I. They do not change M, X, or E. Interrupt entry does not call `SelectTable`. The following instruction runs on the table that was already active.

The debug snapshot in `Cpu65C02.Step` reads metadata before `Execute`, and only when a debug listener is attached. A 65816 step reads that metadata from `active`, so the immediate length matches the table the instruction will run on. The swap happens inside the handler, so the next instruction's fetch sees the new table. The listener-null path, which is the fast path today, does not touch metadata.

### What the swap costs

Paid once per flag-changing instruction:

- Read E and the M and X bits.
- Store one reference. The tables already exist. There is no allocation, no 256-slot copy, and no static write.

Paid once per ordinary instruction:

- Load `active`.
- The existing `entries[opcode]` index and `entry.Fn` call.

A design that rebuilt the 256 slots inside `REP` would copy or allocate on every width change. These tables are built once. The swap is the store.

The 65C02 field is `readonly`, so the JIT may treat it as invariant for the life of the method. The 65816 field is mutable, so `Step` reloads it. That reload is the hot-loop cost of being able to swap. It is one pointer load against a handler that is about to touch the bus.

Emulation-mode changes are `XCE` and reset. Both are already control transfers with their own stack or vector traffic. Selecting the emulation table there is lost in that work. A program that lives in one native width, which is the steady state of a 16-bit IIgs routine, pays the pointer load and never pays the store.

## 5. Alternative: width checks inside the handlers

Leave a single `OpcodeTable` on `Cpu65816`. Keep today's `OpLDA` shape, after #288's `TCpu` change, and keep the call to `GetAccumulatorSize` / `GetIndexSize` that `OpLDA`, `OpLDX`, and the stores already make.

What that preserves:

- One delegate per opcode, the same cardinality as the 65C02 table.
- No `SelectTable`, so a new flag writer cannot forget to swap. `RTI` is correct as soon as it writes P.
- Less closed-generic code for the JIT. One `OpLda<Cpu65816, AbsoluteXMode>` instead of five.
- `$42` is still one slot replacement. The prefix design in section 6 does not depend on having five primary tables.

What that leaves on the common path:

- `GetAccumulatorSize` tests `CP`, then `E`, then M, on every accumulator instruction. Specialized on `Cpu65816` with `CP` known, the live test is still E and M. The five tables delete that test. The handler's width is the type argument.
- `AddressingModes.Immediate` consumes one byte. A 16-bit `LDA #` has to branch and fetch a second byte, or `ImmediateMode` itself has to branch. The mode struct stops being a straight-line `Resolve`. Split immediate structs (`Immediate8`, `Immediate16`) are the table design's way to keep `Resolve` branch-free. Those structs only help if the active slot already points at the right one.
- `OpcodeInfo.OperandLength` and `BaseCycles` are one value per opcode. A single table cannot store both the 2-cycle and the 3-cycle immediate. The disassembler then repeats the M/X test the handlers are already doing. The debug copy of metadata that `Cpu65C02` takes at construction would be wrong for whichever width it did not record.
- The "+1 if M=0" cycle is a branch or a data-dependent add in the handler. On the width-specific table it is the constant stored in that entry.
- Native versus emulation page-cross stays a test of E inside the shared `AbsoluteX` resolver. The reference limits that penalty to emulation mode. A mode struct per machine removes the test.

A well-predicted branch is cheap next to a bus read. A workload that stays in one width will predict a single M test accurately, and the mispredict happens on the instruction after `REP`, once. This alternative can be close in a benchmark. The reason to prefer the tables is the one #288 is already paying for: the body the JIT compiles has concrete widths, a concrete operand length, and a concrete base cycle count. The in-handler check gives that up in exchange for a smaller native image and a simpler flag path.

A hybrid that is worth keeping, and that the recommendation already includes: address-dependent penalties stay as runtime checks inside the mode struct. Width and emulation-versus-native do not.

Rejected variant of the swap: mutate one table's slots when the flags change, by writing new delegates into the 256 entries. That is a large write on the flag path, and it is mutable shared state if two CPUs share the table. Prebuilt immutable tables plus an instance field avoid both.

## 6. `$42`, per CPU

Every primary table's `$42` slot is a named `DispatchWdm`. It is never `DispatchNopImmediate`, and a 65816 or 65832 table does not install `Instructions.WdmDispatch`.

| CPU | Slot `$42` | Behavior |
| --- | --- | --- |
| 65C02 | `Instructions.WdmDispatch` → `DispatchWdm` → `OpWdm` | 2-byte, 2-cycle NOP. Metadata mnemonic is `NOP`. |
| 65816 | That CPU's `DispatchWdm`, one `static readonly OpcodeDispatch` shared by all five tables | WDM. Fetch the signature byte, change no flags. The instruction-set reference gives opcode `$42`, immediate, 2 cycles, no flags. Metadata mnemonic is `WDM` once `CpuInstructions` has that member. |
| 65832, `CP` set (65816-compatible modes) | A `DispatchWdm` with that same WDM body | WDM, as above. The body uses `ICpu` only, so it can be the 65816 delegate or a `Cpu65832` copy of it. It is not `Instructions.WdmDispatch`. Legacy mode keeps the 65C816 opcode. |
| 65832, `CP` clear (32-bit mode) | That mode's own `DispatchWdm` | Extended-opcode prefix. See below. |

The 65C02 method stays the NOP. Replacing it in place would turn every 65C02 `$42` into a 65816 WDM or a prefix. The separate field is what makes the override a table entry rather than an edit to `OpWdm`.

WDM on the 65816 is installed by `Fill` like any other opcode. It does not depend on `TAcc` or `TIndex`. One cached delegate is enough for all five tables.

### The 65832 second-level table

`GetAccumulatorSize` returns 32 when `CP` is clear. The recommendation is one more primary table for that mode, built with `Fill` and `Acc32` / `Idx32`, plus two further immutable `OpcodeTable`s that are not in the primary 256:

- `Bank42`, indexed by the byte after `$42` (`$42 xx`).
- `Bank4242`, indexed by the byte after `$42 42` (`$42 42 xx`).

`Step` still fetches only the first opcode byte and calls `active.Execute`. In 32-bit mode that slot fetches one extension byte and dispatches into `Bank42`. It does not inspect the byte:

```csharp
static readonly OpcodeDispatch DispatchWdm = DispatchPrefix42;

static void DispatchPrefix42(ICpu cpu, in OpcodeEntry entry)
{
    var c = (Cpu65832)cpu;
    byte ext = c.FetchOpcodeByte();
    Cpu65832Tables.Bank42.Execute(ext, cpu);
}
```

`FetchByte` on `Cpu65C02` is private. The prefix needs the same operation on `Cpu65832`: advance PC, read through the instruction-fetch path, add one to `TCU`. That is one fetched byte, one cycle, matching the way `Step` charges the primary opcode. The second-level `Execute` is the same call the primary table uses. Its handlers are `OpcodeDispatch` values from the 32-bit cache (`Acc32`, `Idx32`). Unassigned second-level slots start on a halt delegate, the same rule as `IllegalOpcodeDispatch` on the 65C02 builder. That field is private, so the 65832 builder has its own static readonly halt delegate of the same shape.

The `$42` slot of `Bank42` is another named `DispatchWdm`. That one fetches the next byte and dispatches into `Bank4242`:

```csharp
static readonly OpcodeDispatch DispatchWdm = DispatchPrefix4242;

static void DispatchPrefix4242(ICpu cpu, in OpcodeEntry entry)
{
    var c = (Cpu65832)cpu;
    byte op = c.FetchOpcodeByte();
    Cpu65832Tables.Bank4242.Execute(op, cpu);
}
```

`$42 xx` is one extra table lookup. `$42 42 xx` is that lookup landing on a slot that owns a further table. A deeper bank is the same plug-in: a slot whose delegate fetches one byte and calls `Execute` on another `OpcodeTable`. `$42 42 xx` is the deeper bank this note specifies. Legacy opcodes never enter `DispatchWdm`. They take the single `active.Execute` path.

`Cpu65C02.Step` records `opcodeInfoTable[opcode]` before execute. For a prefix, that snapshot describes `$42`, not `xx`. `DispatchWdm` fills in the extended opcode on the trace after it has fetched `xx`. The listener-null path does not. Cycle totals for the extended instruction are the prefix bytes' fetch cycles plus whatever the second-level handler adds to `TCU`. This note does not assign cycle counts to specific extended opcodes. The references do not list them.

`Cpu65832.Step` and `Reset` are unimplemented. The prefix lands with that CPU, not as a change to the 65C02 table.

## 7. Rules

- No `unsafe`, no `delegate*`, no `AllowUnsafeBlocks`. The call site stays `entry.Fn(cpu, in entry)` on an `OpcodeDispatch`. The repository does not enable unsafe today. This design does not add it.
- No static mutable state. Dispatch delegates and the built tables are `static readonly`. The active table is an instance field on the CPU.
- One cached `static readonly OpcodeDispatch` per closed opcode. Five tables reuse a delegate when they share a closed method. They do not allocate a delegate at `Step` time.
- `$42` remains `DispatchWdm` on each table that has the slot. The 65C02 field `WdmDispatch` continues to point at the 65C02 `DispatchWdm`.
- #288's `Op<TCpu, TMode>` stays the form for width-independent instructions. Width-sensitive instructions add `TAcc` and, when they read X or Y, `TIndex`.

## 8. Open questions for Josh

These change the shape of the tables or the prefix. The recommendations above are the reading of the code and the in-repo references. They are not a guess to implement past.

1. **Is `RTI` in the swap set?** #568 names `REP`, `SEP`, `XCE`, and `PLP`. The emulator reference says `RTI` pulls P (and PBR in native mode), and reset always returns to emulation mode. This note calls `SelectTable` from `REP`, `SEP`, `XCE`, `PLP`, `RTI`, and `Reset`. Confirm `RTI` and `Reset`.

2. **Do M and X stay 1 for the whole time `E` is 1?** The instruction-set reference says emulation mode forces M and X to 1, and that `REP` / `SEP` cannot change them. Under that reading, `XCE` into native mode always selects the native 8/8 table. Confirm there is no latent M/X value, written by `SEP` during emulation, that should become the width on the way back to native mode.

3. **Where do the high bytes of X and Y go when the index width becomes 8?** The emulator reference says emulation mode forces those high bytes to zero. It does not spell out the native-mode case of X changing from 0 to 1 (`SEP`, `PLP`, `RTI`). Confirm that the flag writer clears them, in the same place that updates P, before `SelectTable`. The table swap itself does not touch X or Y.

4. **How many widths does 65832 mode have?** `GetAccumulatorSize` and `GetIndexSize` return 32 whenever `CP` is clear. `ProcessorStatusFlags` remarks say M and X are ignored when `CP` is clear; the helpers also ignore them in 65C02 mode and honor them only in 65816 mode (`CP` set, `E` clear). This note follows the helpers: five 65816 tables while `CP` is set, and one 32-bit table while `CP` is clear. Confirm before anyone adds an 8/16/32 matrix inside 65832 mode. The essay's native-32 mode (A, X, Y, S, and the bank registers become 32-bit) matches the single 32-bit table. It does not match a new M/X encoding.

5. **Which prefix map is binding, and in which mode is `$42` a prefix?** Three sources disagree. #568 and this note's brief: `$42 xx`, with a deeper bank such as `$42 42 xx`. `specs/Architecture Spec v1.0 Part 3.md` Phase 6: prefix opcodes `$42`, `$43`, and `$44`. The opcode-space essay: `$42 xx`, `$42 42 xx`, `$42 43 xx`, plus top-level `$43` and `$44`, while legacy mode preserves every 65C816 opcode. The instruction-set reference assigns `$42` to WDM (reserved, 2 cycles) and `$44` to `MVP`. Column `$x3` is the stack-relative column for the opcodes it lists (`LDA` `$A3`, `STA` `$83`, `ADC` `$63`, `SBC` `$E3`, `CMP` `$C3`). It does not give `$43` its own row. This note's recommendation: the only primary-map prefix is `$42`, and only when `CP` is clear; `CP` set keeps 65816 WDM. The only deeper bank specified here is `$42 42 xx`. Confirm before `$43` or `$44` become prefixes. They are real 65816 opcodes under the instruction-set reference (`$44` is `MVP`).

6. **What does an unassigned `$42 xx` byte do?** The recommendation is the 65C02 builder's rule for a slot this CPU did not define: the halt delegate. Confirm if an undefined extension should halt, or should behave as a NOP the way an unused 65C02 opcode does.

Cycle counts for specific extended opcodes are not an open design choice yet. The references do not list them. Each prefix byte charges one cycle through the same fetch the primary opcode uses. The second-level handler owns the rest of `TCU`.

## 9. Out of scope

- Any code change, including new `CpuInstructions` members (`WDM`, `REP`, `SEP`, `XCE`, `MVP`, `COP`), new mode structs, and a real `Cpu65816.Step`.
- The #288 edit that introduces `TCpu` on the 65C02 handlers.
- A 65832 opcode assignment inside `Bank42` and `Bank4242`. The plug-in is the table. The opcodes are not chosen here.
- Timing of address-dependent penalties beyond "they stay in the mode struct." The reference's full cycle matrix is an implementation checklist for the builder, not a second design.
