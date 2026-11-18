// <copyright file="CycleProgramTable.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// Builds the spike's decode table by concatenating shared addressing-mode step
/// sequences with an operation effect. Cycle counts are not stored; each step that
/// touches the bus is one cycle, and penalty steps are skipped when the hardware
/// skips them.
/// </summary>
internal static class CycleProgramTable
{
    /// <summary>
    /// Maximum step bytes stored for one opcode, including the unused tail.
    /// </summary>
    internal const int MaxSteps = 8;

    /// <summary>
    /// Precomputed step bytes, <see cref="MaxSteps"/> per opcode, terminated by <see cref="Step.End"/>.
    /// Filled once when the type is initialized and not written afterwards.
    /// </summary>
    internal static readonly byte[] Steps;

    /// <summary>
    /// Per-opcode operation applied by the shared memory steps.
    /// Filled once when the type is initialized and not written afterwards.
    /// </summary>
    internal static readonly byte[] Effects;

    static CycleProgramTable()
    {
        var builtSteps = new byte[256 * MaxSteps];
        var builtEffects = new byte[256];

        // Read family. The optional penalty re-reads the last operand byte only
        // when a page is crossed; the forced penalty always does, which is what
        // makes STZ/STA/INC abs,X a 5- or 7-cycle instruction on a 65C02.
        Compose(builtSteps, builtEffects, 0xA9, Effect.LoadA, [Step.ReadImm]);
        Compose(builtSteps, builtEffects, 0xA5, Effect.LoadA, [Step.ReadBal, Step.ReadEa]);
        Compose(builtSteps, builtEffects, 0xB5, Effect.LoadA, [Step.ReadBal, Step.DummyZpIndexX, Step.ReadEa]);
        Compose(builtSteps, builtEffects, 0xAD, Effect.LoadA, [Step.ReadBal, Step.ReadBah, Step.ReadEa]);
        Compose(builtSteps, builtEffects, 0xBD, Effect.LoadA, [Step.ReadBal, Step.ReadBahIndexX, Step.OptionalPenalty, Step.ReadEa]);
        Compose(builtSteps, builtEffects, 0xB2, Effect.LoadA, [Step.ReadBal, Step.ReadPtrLo, Step.ReadPtrHi, Step.ReadEa]);
        Compose(builtSteps, builtEffects, 0xB1, Effect.LoadA, [Step.ReadBal, Step.ReadPtrLo, Step.ReadPtrHiIndexY, Step.OptionalPenalty, Step.ReadEa]);

        // Write and read-modify-write. INC abs,X always pays the index penalty
        // (7 cycles). ASL abs,X pays it only on a page cross (6 or 7).
        Compose(builtSteps, builtEffects, 0x9D, Effect.StoreA, [Step.ReadBal, Step.ReadBahIndexX, Step.ForcedPenalty, Step.WriteEa]);
        Compose(builtSteps, builtEffects, 0x9E, Effect.StoreZero, [Step.ReadBal, Step.ReadBahIndexX, Step.ForcedPenalty, Step.WriteEa]);
        Compose(builtSteps, builtEffects, 0xFE, Effect.Inc, [Step.ReadBal, Step.ReadBahIndexX, Step.ForcedPenalty, Step.ReadEa, Step.RmwIdle, Step.WriteEa]);
        Compose(builtSteps, builtEffects, 0x1E, Effect.Asl, [Step.ReadBal, Step.ReadBahIndexX, Step.OptionalPenalty, Step.ReadEa, Step.RmwIdle, Step.WriteEa]);

        Compose(builtSteps, builtEffects, 0x6C, Effect.None, [Step.ReadBal, Step.ReadBah, Step.ReadTargetLo, Step.ReadTargetHiWrapped, Step.JmpFixup]);
        Compose(builtSteps, builtEffects, 0xF0, Effect.BranchIfZ, [Step.ReadBal, Step.BranchIf, Step.BranchPageFix]);
        Compose(builtSteps, builtEffects, 0x20, Effect.None, [Step.ReadBal, Step.DummyReadStack, Step.PushReturnHi, Step.PushReturnLo, Step.ReadBahJump]);
        Compose(builtSteps, builtEffects, 0x60, Effect.None, [Step.DummyReadPc, Step.DummyReadStack, Step.PullBal, Step.PullBah, Step.ReadReturnBump]);
        Compose(builtSteps, builtEffects, 0x48, Effect.None, [Step.DummyReadPc, Step.PushA]);
        Compose(builtSteps, builtEffects, 0x68, Effect.None, [Step.DummyReadPc, Step.DummyReadStack, Step.PullA]);

        Steps = builtSteps;
        Effects = builtEffects;
    }

    /// <summary>
    /// One bus cycle, or a penalty step the interpreter may skip.
    /// </summary>
    internal enum Step : byte
    {
        /// <summary>End of the step list.</summary>
        End = 0,

        /// <summary>Read an immediate operand and, when the effect says so, load A.</summary>
        ReadImm,

        /// <summary>Read the low operand byte at PC and advance PC.</summary>
        ReadBal,

        /// <summary>Read the high operand byte, form a pointer, and advance PC.</summary>
        ReadBah,

        /// <summary>Read the high operand byte and add X, recording a page cross.</summary>
        ReadBahIndexX,

        /// <summary>Dummy-read the zero-page base, then wrap X into the page.</summary>
        DummyZpIndexX,

        /// <summary>Read the effective address. Load A when the effect says so.</summary>
        ReadEa,

        /// <summary>On a page cross, re-read the last operand byte. Otherwise skip.</summary>
        OptionalPenalty,

        /// <summary>Always re-read the last operand byte.</summary>
        ForcedPenalty,

        /// <summary>Write the effect's store value to the effective address.</summary>
        WriteEa,

        /// <summary>Dummy-read the effective address and apply INC or ASL.</summary>
        RmwIdle,

        /// <summary>Read the zero-page pointer low byte.</summary>
        ReadPtrLo,

        /// <summary>Read the zero-page pointer high byte, wrapping inside page zero.</summary>
        ReadPtrHi,

        /// <summary>Read the pointer high byte and add Y, recording a page cross.</summary>
        ReadPtrHiIndexY,

        /// <summary>Read the jump target low byte through the pointer.</summary>
        ReadTargetLo,

        /// <summary>Read the jump target high byte with 6502 page wrap.</summary>
        ReadTargetHiWrapped,

        /// <summary>Fix JMP (abs) when the pointer ends in $FF, then load PC.</summary>
        JmpFixup,

        /// <summary>Resolve a branch: not taken ends, taken may request a page fix.</summary>
        BranchIf,

        /// <summary>Read the uncorrected branch target and commit the new page.</summary>
        BranchPageFix,

        /// <summary>Read the byte at PC without advancing PC.</summary>
        DummyReadPc,

        /// <summary>Read the current stack slot without moving the stack pointer.</summary>
        DummyReadStack,

        /// <summary>Push A and decrement the stack pointer.</summary>
        PushA,

        /// <summary>Pull A, increment the stack pointer, and set N and Z.</summary>
        PullA,

        /// <summary>Push the high byte of the JSR return address.</summary>
        PushReturnHi,

        /// <summary>Push the low byte of the JSR return address.</summary>
        PushReturnLo,

        /// <summary>Read the JSR high byte and load PC with the target.</summary>
        ReadBahJump,

        /// <summary>Pull the low return byte.</summary>
        PullBal,

        /// <summary>Pull the high return byte.</summary>
        PullBah,

        /// <summary>Dummy-read the pulled return address, then set PC to that address plus one.</summary>
        ReadReturnBump,
    }

    /// <summary>
    /// Operation applied by the shared memory steps. Addressing does not know
    /// which instruction it serves; the effect byte selects the internal action.
    /// </summary>
    internal enum Effect : byte
    {
        /// <summary>No data effect. Control steps carry their own action.</summary>
        None = 0,

        /// <summary>Load the accumulator and set N and Z.</summary>
        LoadA,

        /// <summary>Store the accumulator.</summary>
        StoreA,

        /// <summary>Store zero.</summary>
        StoreZero,

        /// <summary>Increment the latched memory byte.</summary>
        Inc,

        /// <summary>Arithmetic shift left of the latched memory byte.</summary>
        Asl,

        /// <summary>Branch when the zero flag is set.</summary>
        BranchIfZ,
    }

    /// <summary>
    /// Gets a value indicating whether the opcode has a composed program.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <returns><see langword="true"/> when the spike executes this opcode.</returns>
    internal static bool Covers(byte opcode) => Steps[opcode * MaxSteps] != (byte)Step.End;

    private static void Compose(byte[] steps, byte[] effects, byte opcode, Effect effect, ReadOnlySpan<Step> program)
    {
        if (program.Length > MaxSteps)
        {
            throw new InvalidOperationException($"Opcode ${opcode:X2} has {program.Length} steps; the spike allows {MaxSteps}.");
        }

        int offset = opcode * MaxSteps;
        for (int i = 0; i < program.Length; i++)
        {
            steps[offset + i] = (byte)program[i];
        }

        effects[opcode] = (byte)effect;
    }
}