// <copyright file="CycleStepCpu.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

using System.Runtime.CompilerServices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;

/// <summary>
/// Spike-only 65C02 that runs a precomputed per-cycle step program.
/// </summary>
/// <remarks>
/// <para>
/// This is not a replacement for <see cref="Cpu65C02"/>. It covers the opcode slice
/// composed in <see cref="CycleProgramTable"/> and exists to compare a decode-table
/// plus cycle-step design with the shipping core. Opcode fetch is shared. The rest
/// of the instruction is a tight loop over that opcode's steps. When a trace buffer
/// is omitted, the whole instruction still runs to completion before the caller
/// advances the scheduler, which is the fast path used when nothing needs to stop
/// mid-instruction.
/// </para>
/// <para>
/// Every step that is not skipped performs exactly one bus read or write. Penalty
/// and branch steps are how the table stays shared across read, write, and
/// read-modify-write forms of the same addressing mode.
/// </para>
/// </remarks>
public sealed class CycleStepCpu
{
    private const byte NegativeFlag = 0x80;
    private const byte ZeroFlag = 0x02;
    private const byte CarryFlag = 0x01;

    private readonly IMemoryBus bus;
    private readonly Scheduler scheduler;
    private readonly SignalBus signals;
    private readonly byte[] steps;
    private readonly byte[] effects;

    private byte accumulator;
    private byte indexX;
    private byte indexY;
    private byte stackPointer;
    private byte status;
    private ushort programCounter;

    private byte bal;
    private byte bah;
    private byte operand;
    private byte data;
    private ushort effective;
    private ushort pointer;
    private ushort wrapped;
    private ushort fixAddress;
    private bool penalty;
    private int cycles;
    private ulong baseStamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="CycleStepCpu"/> class.
    /// </summary>
    /// <param name="bus">The memory bus. The same bus implementation the shipping core uses.</param>
    /// <param name="scheduler">The scheduler advanced once per instruction by <see cref="Step"/>.</param>
    /// <param name="signals">The signal bus polled once per instruction, matching the shipping fast path.</param>
    public CycleStepCpu(IMemoryBus bus, Scheduler scheduler, SignalBus signals)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(signals);
        this.bus = bus;
        this.scheduler = scheduler;
        this.signals = signals;
        steps = CycleProgramTable.Steps;
        effects = CycleProgramTable.Effects;
    }

    /// <summary>
    /// Gets or sets the architectural registers.
    /// </summary>
    public CycleStepRegisters Registers
    {
        get => new(programCounter, accumulator, indexX, indexY, stackPointer, status);
        set
        {
            programCounter = value.PC;
            accumulator = value.A;
            indexX = value.X;
            indexY = value.Y;
            stackPointer = value.S;
            status = value.P;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the spike has a program for the opcode.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <returns><see langword="true"/> when the spike can execute the opcode.</returns>
    public static bool Covers(byte opcode) => CycleProgramTable.Covers(opcode);

    /// <summary>
    /// Executes one instruction and returns the number of bus cycles it took.
    /// </summary>
    /// <param name="trace">
    /// Receives one entry per bus cycle when the span is non-empty. Pass an empty
    /// span to run the instruction without recording. The scheduler is not advanced.
    /// </param>
    /// <returns>The number of bus cycles, including the opcode fetch.</returns>
    public int Execute(Span<ObservedCycle> trace)
    {
        cycles = 0;
        baseStamp = scheduler.Now.Value;
        byte opcode = BusRead(programCounter, AccessIntent.InstructionFetch, trace);
        programCounter++;

        ReadOnlySpan<byte> row = steps.AsSpan(opcode * CycleProgramTable.MaxSteps, CycleProgramTable.MaxSteps);
        if (row[0] == (byte)CycleProgramTable.Step.End)
        {
            throw new NotSupportedException($"Opcode ${opcode:X2} is outside the cycle-step spike.");
        }

        CycleProgramTable.Effect effect = (CycleProgramTable.Effect)effects[opcode];
        for (int i = 0; i < row.Length; i++)
        {
            var step = (CycleProgramTable.Step)row[i];
            if (step == CycleProgramTable.Step.End)
            {
                break;
            }

            if (!Apply(step, effect, trace))
            {
                break;
            }
        }

        return cycles;
    }

    /// <summary>
    /// Executes one instruction, polls interrupt attention, and advances the scheduler
    /// by the instruction's cycle count. This is the path the benchmark compares with
    /// <see cref="Cpu65C02.Step"/>.
    /// </summary>
    /// <returns>The number of bus cycles the instruction took. Zero when attention is pending.</returns>
    /// <remarks>
    /// The spike does not take interrupts. The attention read is the same poll the
    /// shipping core uses to skip that work. Pending attention ends the step so a
    /// benchmark never silently ignores a line that was actually asserted.
    /// </remarks>
    public int Step()
    {
        if (signals.AttentionPending)
        {
            return 0;
        }

        int taken = Execute(Span<ObservedCycle>.Empty);
        scheduler.Advance((ulong)taken);
        return taken;
    }

    private bool Apply(CycleProgramTable.Step step, CycleProgramTable.Effect effect, Span<ObservedCycle> trace)
    {
        switch (step)
        {
            case CycleProgramTable.Step.ReadImm:
                data = BusRead(programCounter, AccessIntent.DataRead, trace);
                programCounter++;
                if (effect == CycleProgramTable.Effect.LoadA)
                {
                    accumulator = data;
                    SetNZ(data);
                }

                return true;

            case CycleProgramTable.Step.ReadBal:
                bal = BusRead(programCounter, AccessIntent.DataRead, trace);
                programCounter++;
                operand = bal;
                effective = bal;
                return true;

            case CycleProgramTable.Step.ReadBah:
                bah = BusRead(programCounter, AccessIntent.DataRead, trace);
                programCounter++;
                pointer = (ushort)((bah << 8) | bal);
                effective = pointer;
                return true;

            case CycleProgramTable.Step.ReadBahIndexX:
                bah = BusRead(programCounter, AccessIntent.DataRead, trace);
                programCounter++;
                AddIndex(indexX);
                return true;

            case CycleProgramTable.Step.DummyZpIndexX:
                BusRead(bal, AccessIntent.DataRead, trace);
                effective = (byte)(bal + indexX);
                return true;

            case CycleProgramTable.Step.ReadEa:
                data = BusRead(effective, AccessIntent.DataRead, trace);
                if (effect == CycleProgramTable.Effect.LoadA)
                {
                    accumulator = data;
                    SetNZ(data);
                }

                return true;

            case CycleProgramTable.Step.OptionalPenalty:
                if (!penalty)
                {
                    return true;
                }

                BusRead((ushort)(programCounter - 1), AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.ForcedPenalty:
                BusRead((ushort)(programCounter - 1), AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.WriteEa:
                BusWrite(effective, StoreValue(effect), trace);
                return true;

            case CycleProgramTable.Step.RmwIdle:
                BusRead(effective, AccessIntent.DataRead, trace);
                ApplyRmw(effect);
                return true;

            case CycleProgramTable.Step.ReadPtrLo:
                bal = BusRead(operand, AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.ReadPtrHi:
                bah = BusRead((byte)(operand + 1), AccessIntent.DataRead, trace);
                effective = (ushort)((bah << 8) | bal);
                return true;

            case CycleProgramTable.Step.ReadPtrHiIndexY:
                bah = BusRead((byte)(operand + 1), AccessIntent.DataRead, trace);
                AddIndex(indexY);
                return true;

            case CycleProgramTable.Step.ReadTargetLo:
                bal = BusRead(pointer, AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.ReadTargetHiWrapped:
                wrapped = (ushort)((pointer & 0xFF00) | ((pointer + 1) & 0xFF));
                bah = BusRead(wrapped, AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.JmpFixup:
                if ((pointer & 0xFF) == 0xFF)
                {
                    bah = BusRead((ushort)(pointer + 1), AccessIntent.DataRead, trace);
                }
                else
                {
                    BusRead(wrapped, AccessIntent.DataRead, trace);
                }

                programCounter = (ushort)((bah << 8) | bal);
                return true;

            case CycleProgramTable.Step.BranchIf:
                return TakeBranch(effect, trace);

            case CycleProgramTable.Step.BranchPageFix:
                BusRead(fixAddress, AccessIntent.DataRead, trace);
                programCounter = effective;
                return true;

            case CycleProgramTable.Step.DummyReadPc:
                BusRead(programCounter, AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.DummyReadStack:
                BusRead(StackAddress(), AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.PushA:
                BusWrite(StackAddress(), accumulator, trace);
                stackPointer--;
                return true;

            case CycleProgramTable.Step.PullA:
                stackPointer++;
                accumulator = BusRead(StackAddress(), AccessIntent.DataRead, trace);
                SetNZ(accumulator);
                return true;

            case CycleProgramTable.Step.PushReturnHi:
                BusWrite(StackAddress(), (byte)(programCounter >> 8), trace);
                stackPointer--;
                return true;

            case CycleProgramTable.Step.PushReturnLo:
                BusWrite(StackAddress(), (byte)programCounter, trace);
                stackPointer--;
                return true;

            case CycleProgramTable.Step.ReadBahJump:
                bah = BusRead(programCounter, AccessIntent.DataRead, trace);
                programCounter = (ushort)((bah << 8) | bal);
                return true;

            case CycleProgramTable.Step.PullBal:
                stackPointer++;
                bal = BusRead(StackAddress(), AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.PullBah:
                stackPointer++;
                bah = BusRead(StackAddress(), AccessIntent.DataRead, trace);
                return true;

            case CycleProgramTable.Step.ReadReturnBump:
                effective = (ushort)((bah << 8) | bal);
                BusRead(effective, AccessIntent.DataRead, trace);
                programCounter = (ushort)(effective + 1);
                return true;

            case CycleProgramTable.Step.End:
                return false;

            default:
                throw new InvalidOperationException($"Unknown cycle step {(byte)step}.");
        }
    }

    private bool TakeBranch(CycleProgramTable.Effect effect, Span<ObservedCycle> trace)
    {
        bool zeroSet = (status & ZeroFlag) != 0;
        bool take = effect == CycleProgramTable.Effect.BranchIfZ ? zeroSet : !zeroSet;
        if (!take)
        {
            return false;
        }

        BusRead(programCounter, AccessIntent.DataRead, trace);
        ushort target = (ushort)(programCounter + (sbyte)bal);
        if ((target & 0xFF00) == (programCounter & 0xFF00))
        {
            programCounter = target;
            return false;
        }

        fixAddress = (ushort)((programCounter & 0xFF00) | (target & 0x00FF));
        effective = target;
        return true;
    }

    private void AddIndex(byte index)
    {
        ushort baseAddress = (ushort)((bah << 8) | bal);
        effective = (ushort)(baseAddress + index);
        penalty = (baseAddress & 0xFF00) != (effective & 0xFF00);
    }

    private byte StoreValue(CycleProgramTable.Effect effect)
    {
        if (effect == CycleProgramTable.Effect.StoreA)
        {
            return accumulator;
        }

        if (effect == CycleProgramTable.Effect.StoreZero)
        {
            return 0;
        }

        return data;
    }

    private void ApplyRmw(CycleProgramTable.Effect effect)
    {
        if (effect == CycleProgramTable.Effect.Inc)
        {
            data = (byte)(data + 1);
            SetNZ(data);
            return;
        }

        if (effect == CycleProgramTable.Effect.Asl)
        {
            bool carry = (data & NegativeFlag) != 0;
            data = (byte)(data << 1);
            if (carry)
            {
                status |= CarryFlag;
            }
            else
            {
                status = (byte)(status & ~CarryFlag);
            }

            SetNZ(data);
        }
    }

    private void SetNZ(byte value)
    {
        status = (byte)(status & ~(NegativeFlag | ZeroFlag));
        status |= (byte)(value & NegativeFlag);
        if (value == 0)
        {
            status |= ZeroFlag;
        }
    }

    private ushort StackAddress() => (ushort)(0x0100 | stackPointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte BusRead(ushort address, AccessIntent intent, Span<ObservedCycle> trace)
    {
        var access = new BusAccess(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: intent,
            SourceId: 0,
            Cycle: baseStamp + (ulong)cycles,
            Flags: AccessFlags.None);
        var result = bus.TryRead8(access);
        byte value = result.Failed ? (byte)0xFF : result.Value;
        Record(trace, address, value, isWrite: false);
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BusWrite(ushort address, byte value, Span<ObservedCycle> trace)
    {
        var access = new BusAccess(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataWrite,
            SourceId: 0,
            Cycle: baseStamp + (ulong)cycles,
            Flags: AccessFlags.None);
        bus.TryWrite8(access, value);
        Record(trace, address, value, isWrite: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Record(Span<ObservedCycle> trace, ushort address, byte value, bool isWrite)
    {
        if ((uint)cycles < (uint)trace.Length)
        {
            trace[cycles] = new ObservedCycle(address, value, isWrite);
        }

        cycles++;
    }
}