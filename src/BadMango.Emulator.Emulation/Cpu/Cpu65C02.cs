// <copyright file="Cpu65C02.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using System.Runtime.CompilerServices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;

using Core;
using Core.Cpu;
using Core.Debugger;
using Debugging;

/// <summary>
/// WDC 65C02 CPU emulator with cycle-accurate execution using bus-based memory access.
/// </summary>
/// <remarks>
/// <para>
/// The 65C02 is an enhanced version of the 6502 with additional instructions,
/// addressing modes, and bug fixes. This implementation provides a minimal but
/// functional CPU core with basic instruction support.
/// </para>
/// <para>
/// This CPU implementation uses the bus architecture for all memory operations.
/// The CPU computes intent; the bus enforces consequences (permissions, faults,
/// cycle counting for decomposed access).
/// </para>
/// <para>
/// Optimized with aggressive inlining for maximum performance.
/// </para>
/// </remarks>
public sealed class Cpu65C02 : CpuBase, ITrapAwareCpu
{
    private readonly OpcodeTable opcodeTable;
    private readonly OpcodeInfo[] opcodeInfoTable;

    /// <summary>
    /// The concrete scheduler, when the event context supplies one, so the
    /// per-instruction <c>Advance</c> call is a direct (non-interface) call.
    /// <see langword="null"/> for other <see cref="IScheduler"/> implementations.
    /// </summary>
    private readonly Scheduler? concreteScheduler;

    /// <summary>
    /// The concrete signal bus, when the event context supplies one, so the CPU can
    /// test <see cref="SignalBus.AnyInterruptPending"/> instead of running the full
    /// interrupt check every instruction. <see langword="null"/> for other
    /// <see cref="Core.Interfaces.Signaling.ISignalBus"/> implementations.
    /// </summary>
    private readonly SignalBus? concreteSignals;

    /// <summary>
    /// The I-bit value captured immediately before the previous instruction executed.
    /// Only meaningful while <see cref="interruptDisableChangePending"/> is set: CLI, SEI
    /// and PLP poll for IRQ using the I value from before they execute, so the next
    /// instruction boundary uses this sample instead of the live register.
    /// </summary>
    private bool interruptDisableSample;

    /// <summary>
    /// Set when the previous instruction was CLI, SEI or PLP, whose IRQ poll uses the I value
    /// from before they executed (the new I is only seen one instruction later). RTI and all
    /// other instructions leave this clear so the live I value is used at the next boundary.
    /// </summary>
    private bool interruptDisableChangePending;

    private ITrapRegistry trapRegistry;

    /// <summary>
    /// The trap registry as its concrete type, when it is one, so the CPU can skip
    /// the per-instruction lookup while it is empty.
    /// </summary>
    private TrapRegistry? concreteTrapRegistry;

    /// <summary>
    /// <see langword="true"/> when <see cref="trapRegistry"/> is <see cref="NullTrapRegistry"/>,
    /// which never contains traps.
    /// </summary>
    private bool trapRegistryIsNull;

    /// <summary>
    /// Set while the CPU is holding in RESET (the line was seen asserted at an instruction
    /// boundary). The next boundary with the line released runs the reset sequence.
    /// </summary>
    private bool resetHeld;

    /// <summary>
    /// How this CPU treats <see cref="SignalLine.Abort"/>. Defaults to <see cref="AbortReaction.NoOp"/>.
    /// A machine reset does not change it.
    /// </summary>
    private AbortReaction abortReaction;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cpu65C02"/> class with an event context.
    /// </summary>
    /// <param name="context">The event context providing access to the memory bus, signal bus, and scheduler.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public Cpu65C02(IEventContext context)
        : base(context)
    {
        opcodeTable = Cpu65C02OpcodeTableBuilder.Build();
        opcodeInfoTable = OpcodeTableAnalyzer.BuildOpcodeInfoArray(opcodeTable);
        concreteScheduler = context.Scheduler as Scheduler;
        concreteSignals = context.Signals as SignalBus;
        trapRegistry = NullTrapRegistry.Instance;
        trapRegistryIsNull = true;
    }

    /// <inheritdoc />
    public override CpuCapabilities Capabilities => CpuCapabilities.Base6502 | CpuCapabilities.Supports65C02Instructions;

    /// <summary>
    /// Gets or sets the trap registry for ROM routine interception.
    /// </summary>
    /// <value>
    /// The trap registry. Defaults to <see cref="NullTrapRegistry.Instance"/> when
    /// no custom registry is assigned, ensuring a non-null value is always available
    /// in the hot loop without requiring null checks.
    /// </value>
    /// <remarks>
    /// <para>
    /// When a trap registry is attached, the CPU will check for traps at each instruction
    /// fetch address before executing the opcode. If a trap is registered and its handler
    /// returns <see cref="TrapResult.Handled"/> = <see langword="true"/>, the CPU will
    /// perform an RTS to return to the calling code instead of executing the ROM instruction.
    /// </para>
    /// <para>
    /// This enables native implementations of ROM routines for performance optimization
    /// while maintaining compatibility with code that calls those routines.
    /// </para>
    /// <para>
    /// Setting this property to <see langword="null"/> resets it to the default
    /// <see cref="NullTrapRegistry.Instance"/>.
    /// </para>
    /// </remarks>
    public ITrapRegistry TrapRegistry
    {
        get => trapRegistry;
        set
        {
            trapRegistry = value ?? NullTrapRegistry.Instance;
            concreteTrapRegistry = trapRegistry as TrapRegistry;
            trapRegistryIsNull = trapRegistry is NullTrapRegistry;
        }
    }

    /// <summary>
    /// Gets or sets how this CPU treats an asserted <see cref="SignalLine.Abort"/>.
    /// </summary>
    /// <value>
    /// <see cref="AbortReaction.NoOp"/> unless the debugger selects <see cref="AbortReaction.HostBreak"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// <see cref="AbortReaction.NoOp"/> ignores the line. Fault status stays latched and an
    /// unmapped fetch still sets <see cref="HaltState.Stp"/>. <see cref="AbortReaction.HostBreak"/>
    /// calls <see cref="CpuBase.RequestStop"/> at the next instruction boundary and does not set
    /// <see cref="HaltState.Stp"/>, push a vector, or change registers.
    /// </para>
    /// <para>
    /// The debugger is the only writer of <see cref="AbortReaction.HostBreak"/>. Reset of this
    /// CPU, including the reset a machine performs, does not change the property.
    /// </para>
    /// </remarks>
    public AbortReaction AbortReaction
    {
        get => abortReaction;
        set
        {
            abortReaction = value;
            SetHaltOnUnmapped(value != AbortReaction.HostBreak);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is the immediate power-on reset: registers are initialized to their power-on
    /// values and PC is loaded from the reset vector through the current bus mapping. It
    /// replaces the signal-driven reset sequence, so any pending RESET hold or release edge
    /// is discarded. The RESET signal path (hold while the line is low, sequence on release)
    /// is handled in <see cref="Step"/>. <see cref="AbortReaction"/> is a host setting and is
    /// left unchanged.
    /// </remarks>
    public override void Reset()
    {
        // Note: Scheduler reset is handled by Machine.Reset(), not the CPU.
        // The CPU only resets its own state (registers, halt reason, stop flag).
        Registers = new(true, Read16(Cpu65C02Constants.ResetVector));
        interruptDisableSample = Registers.P.IsInterruptDisabled();
        interruptDisableChangePending = false;
        HaltReason = HaltState.None;
        resetHeld = false;
        concreteSignals?.ConsumeResetReleaseEdge();
        ClearStopRequest();
        this.ClearRetiredInstruction();
    }

    /// <inheritdoc/>
    public override CpuStepResult Step()
    {
        // Clear TCU at the start of each instruction
        Registers.TCU = Cycle.Zero;

        // Instruction-boundary attention: machine service requests, RESET hold/release,
        // interrupts, and ABORT. With the concrete SignalBus a single cached flag covers all
        // of them, so the common path costs one field read and one branch. ABORT is part of
        // that flag. A NoOp reaction ignores the line inside the boundary, which is not on
        // this path.
        if ((concreteSignals is null || concreteSignals.AttentionPending) && TryServiceBoundary(out var boundaryResult))
        {
            return boundaryResult;
        }

        if (Halted)
        {
            var haltedState = HaltReason switch
            {
                HaltState.Wai => CpuRunState.WaitingForInterrupt,
                HaltState.Stp => CpuRunState.Stopped,
                _ => CpuRunState.Halted,
            };
            return new(haltedState, Cycle.Zero);
        }

        // Capture state before execution for debug listener
        Addr pcBefore = Registers.PC.GetAddr();

        // Check for a call trap before fetching the opcode. Read and write traps
        // are not call traps. The lookup is skipped when the registry cannot
        // contain call traps (NullTrapRegistry, or a concrete registry with none).
        if (MayHaveTrapAt(pcBefore))
        {
            var trapResult = trapRegistry.TryExecute(pcBefore, this, Bus, EventContext);
            if (trapResult.Handled)
            {
                // The trapped instruction replaces a real one and is not CLI/SEI/PLP, so any
                // delayed I sample from the previous instruction has now been consumed.
                interruptDisableChangePending = false;

                // Trap was handled - the handler determines how to return
                // Add cycles from the trap result
                Registers.TCU += trapResult.CyclesConsumed;

                // Handle return based on the method specified by the trap handler
                switch (trapResult.ReturnMethod)
                {
                    case TrapReturnMethod.Rts:
                        // Perform RTS: pull return address from stack and set PC
                        // RTS pulls low byte first, then high byte, and adds 1 to the address
                        Addr rtsLowAddr = PopByte(Cpu65C02Constants.StackBase);
                        byte rtsLowByte = Read8(rtsLowAddr);
                        Addr rtsHighAddr = PopByte(Cpu65C02Constants.StackBase);
                        byte rtsHighByte = Read8(rtsHighAddr);
                        ushort rtsReturnAddress = (ushort)((rtsHighByte << 8) | rtsLowByte);
                        Registers.PC.SetAddr((Addr)((rtsReturnAddress + 1u) & 0xFFFF));

                        // Add RTS cycles (6 cycles for RTS)
                        Registers.TCU += 6;
                        break;

                    case TrapReturnMethod.Rti:
                        // Perform RTI: pull status, then return address from stack
                        // RTI pulls status first, then PC low, then PC high (no +1)
                        Addr statusAddr = PopByte(Cpu65C02Constants.StackBase);
                        byte status = Read8(statusAddr);
                        Registers.P = Instructions.PulledProcessorStatus(status);

                        Addr rtiLowAddr = PopByte(Cpu65C02Constants.StackBase);
                        byte rtiLowByte = Read8(rtiLowAddr);
                        Addr rtiHighAddr = PopByte(Cpu65C02Constants.StackBase);
                        byte rtiHighByte = Read8(rtiHighAddr);
                        ushort rtiReturnAddress = (ushort)((rtiHighByte << 8) | rtiLowByte);
                        Registers.PC.SetAddr((Addr)rtiReturnAddress);

                        // Add RTI cycles (6 cycles for RTI)
                        Registers.TCU += 6;
                        break;

                    case TrapReturnMethod.None:
                        // No automatic return - use ReturnAddress if specified,
                        // otherwise continue at current PC (for JMP targets or when
                        // the handler has already set PC)
                        if (trapResult.ReturnAddress.HasValue)
                        {
                            Registers.PC.SetAddr(trapResult.ReturnAddress.Value);
                        }

                        // No additional cycles for direct PC manipulation
                        break;
                }

                // Capture TCU before advancing scheduler (for return value)
                Cycle trapCycles = Registers.TCU;

                // Advance the scheduler by the TCU value
                AdvanceScheduler(trapCycles);

                // Clear TCU after advancing scheduler
                Registers.TCU = Cycle.Zero;

                this.NoteRetiredInstruction(pcBefore);
                return new(CpuRunState.Running, trapCycles);
            }
        }

        byte opcode = FetchByte(); // Advances TCU by 1 for the opcode fetch

        // Notify debug listener before execution
        if (DebugListener is not null)
        {
            OpcodeInfo opcodeInfo = opcodeInfoTable[opcode];

            // Initialize trace for this instruction
            var opcodeBuffer = default(OpcodeBuffer);
            opcodeBuffer[0] = opcode;
            Trace = new(
                StartPC: pcBefore,
                OpCode: opcodeBuffer,
                Instruction: opcodeInfo.Instruction,
                AddressingMode: opcodeInfo.AddressingMode,
                OperandSize: 0,
                Operands: default,
                EffectiveAddress: 0,
                StartCycle: EventContext.Now,
                InstructionCycles: Cycle.Zero); // TCU is the source of truth for cycles

            var beforeArgs = new DebugStepEventArgs
            {
                PC = pcBefore,
                Opcode = opcode,
                Registers = Registers,
                Cycles = EventContext.Now.Value,
                Halted = false,
                HaltReason = HaltState.None,
            };
            DebugListener.OnBeforeStep(in beforeArgs);
        }

        // Capture I before execution: CLI/SEI/PLP poll for IRQ using the pre-execute I value.
        bool iBeforeExecute = Registers.P.IsInterruptDisabled();

        // Execute opcode - handlers now access memory through this CPU instance
        opcodeTable.Execute(opcode, this);

        // Latch the I state for the next IRQ poll. CLI (0x58), SEI (0x78) and PLP (0x28)
        // poll using the I value from before they executed, so their new I is seen one
        // instruction later (back-to-back CLI/SEI/PLP each use their own pre-execute I).
        // RTI and every other instruction use the live I value at the next boundary.
        interruptDisableSample = iBeforeExecute;
        interruptDisableChangePending = opcode is 0x58 or 0x78 or 0x28;

        // Capture TCU before advancing scheduler (for return value)
        Cycle instructionCycles = Registers.TCU;

        // Advance the scheduler by the TCU value (total cycles for this instruction)
        AdvanceScheduler(instructionCycles);

        // Notify debug listener after execution
        if (DebugListener is not null)
        {
            // Apply TCU to the trace (this is a debug-only operation)
            Trace = Trace with { InstructionCycles = instructionCycles };

            var afterArgs = new DebugStepEventArgs
            {
                PC = pcBefore,
                Opcode = opcode,
                Instruction = Trace.Instruction,
                AddressingMode = Trace.AddressingMode,
                OperandSize = Trace.OperandSize,
                Operands = Trace.Operands,
                EffectiveAddress = Trace.EffectiveAddress,
                Registers = Registers,
                Cycles = EventContext.Now.Value,
                InstructionCycles = (byte)instructionCycles.Value,
                Halted = Halted,
                HaltReason = HaltReason,
            };
            DebugListener.OnAfterStep(in afterArgs);
        }

        // Clear TCU after advancing scheduler (cycles have been committed)
        Registers.TCU = Cycle.Zero;

        // Determine the run state after execution
        CpuRunState runState = HaltReason switch
        {
            HaltState.None => CpuRunState.Running,
            HaltState.Wai => CpuRunState.WaitingForInterrupt,
            HaltState.Stp => CpuRunState.Stopped,
            _ => CpuRunState.Halted,
        };

        this.NoteRetiredInstruction(pcBefore);
        return new(runState, instructionCycles);
    }

    // ─── Private Helper Methods ─────────────────────────────────────────
    private void AdvanceScheduler(Cycle cycles)
    {
        if (concreteScheduler is not null)
        {
            concreteScheduler.Advance(cycles);
        }
        else
        {
            EventContext.Scheduler.Advance(cycles);
        }
    }

    private bool MayHaveTrapAt(Addr address)
    {
        // Same shape as the empty-registry check: one type test, then a field test.
        // MayHaveCallTrap stays out of line so a bare CPU does not pay for the lookup.
        if (concreteTrapRegistry is not null)
        {
            return concreteTrapRegistry.MayHaveCallTrap(address);
        }

        return !trapRegistryIsNull && trapRegistry.HasTrap(address, TrapOperation.Call);
    }

    /// <summary>
    /// Handles everything that can need the CPU's attention at an instruction boundary:
    /// pending machine service requests, the RESET line, ABORT, and interrupts.
    /// </summary>
    /// <param name="result">The step result when the boundary consumed the step.</param>
    /// <returns>
    /// <see langword="true"/> if a RESET hold cycle, the reset sequence or an interrupt entry
    /// consumed this step; <see langword="false"/> to continue with normal instruction execution.
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryServiceBoundary(out CpuStepResult result)
    {
        // Run work the machine marshalled onto this thread (for example a RESET request
        // from the REPL) before sampling the lines, so its effects are seen right away.
        concreteSignals?.ServiceRequests();

        // RESET is level-sensitive while asserted and edge-triggered on release.
        // While held, the CPU does not execute instructions but still advances time.
        // On release, execute the reset entry sequence.
        if (Signals.IsAsserted(SignalLine.Reset))
        {
            result = ProcessResetHoldCycle();
            return true;
        }

        // Always consume the latched release edge so the attention flag clears. The edge
        // also covers a RESET that was asserted and released between two boundaries.
        bool releaseEdge = concreteSignals is not null && concreteSignals.ConsumeResetReleaseEdge();
        if (resetHeld || releaseEdge)
        {
            result = ProcessResetReleaseSequence();
            return true;
        }

        // Host-break stops before the instruction and before any interrupt entry, so the
        // reaction does not push a vector or change registers. NoOp falls through.
        if (abortReaction == AbortReaction.HostBreak && Signals.IsAsserted(SignalLine.Abort))
        {
            RequestStop();
            result = new(CpuRunState.Stopped, Cycle.Zero);
            return true;
        }

        // Determine the I value visible at this instruction boundary. Normally this is the
        // current register value; after CLI/SEI/PLP the poll uses the I value from before
        // that instruction executed, so its new I is seen one instruction later.
        bool irqMaskedAtBoundary = interruptDisableChangePending ? interruptDisableSample : Registers.P.IsInterruptDisabled();

        if (CheckInterrupts(irqMaskedAtBoundary))
        {
            // Interrupt entry updates I immediately; keep the latched boundary sample in sync.
            interruptDisableSample = Registers.P.IsInterruptDisabled();
            interruptDisableChangePending = false;

            // Interrupt was processed, TCU was updated by ProcessInterrupt
            // Advance the scheduler by the TCU value
            Cycle interruptCycles = Registers.TCU;
            AdvanceScheduler(interruptCycles);

            // Clear TCU after advancing scheduler
            Registers.TCU = Cycle.Zero;
            result = new(CpuRunState.Running, interruptCycles);
            return true;
        }

        result = default;
        return false;
    }

    private CpuStepResult ProcessResetHoldCycle()
    {
        resetHeld = true;
        HaltReason = HaltState.None;
        Registers.P.SetInterruptDisable(true);
        Registers.P.SetDecimalMode(false);
        Registers.TCU += 1;

        Cycle holdCycle = Registers.TCU;
        AdvanceScheduler(holdCycle);
        Registers.TCU = Cycle.Zero;
        return new(CpuRunState.Running, holdCycle);
    }

    private CpuStepResult ProcessResetReleaseSequence()
    {
        resetHeld = false;
        HaltReason = HaltState.None;

        // Two internal cycles while releasing reset.
        Registers.TCU += 2;

        // Three suppressed stack pushes: no writes, SP decrements by 3.
        byte sp = Registers.SP.GetByte();
        Registers.SP.SetByte(unchecked((byte)(sp - 3)));
        Registers.TCU += 3;

        // Reset always enforces I=1 and D=0 before vector fetch (W65C02S datasheet;
        // Sather, Understanding the Apple IIe, p. 4-14). The 65C02 has no native mode,
        // so RESET also returns the extended register model to the same 8-bit
        // emulation/compatibility state that a power-on reset (new Registers(true, ...))
        // establishes: E=1, CP=1, M=1, X=1. A, X, Y and the rest of P are unaffected.
        Registers.E = true;
        Registers.CP = true;
        Registers.P |= ProcessorStatusFlags.M | ProcessorStatusFlags.X;
        Registers.P.SetInterruptDisable(true);
        Registers.P.SetDecimalMode(false);

        Registers.PC.SetAddr((Addr)(Read16(Cpu65C02Constants.ResetVector) & 0xFFFF));
        this.ClearRetiredInstruction();
        Registers.TCU += 2;

        interruptDisableSample = Registers.P.IsInterruptDisabled();
        interruptDisableChangePending = false;

        Cycle releaseCycles = Registers.TCU;
        AdvanceScheduler(releaseCycles);
        Registers.TCU = Cycle.Zero;
        return new(CpuRunState.Running, releaseCycles);
    }

    private byte FetchByte()
    {
        var pc = Registers.PC.GetAddr();
        Registers.PC.Advance();

        byte value = InstructionFetch8(pc);

        // Advance TCU for the opcode fetch cycle
        Registers.TCU += 1;

        return value;
    }

    /// <summary>
    /// Fetches a byte from memory as part of instruction fetch.
    /// </summary>
    /// <param name="address">The address to fetch from.</param>
    /// <returns>The byte value at the address.</returns>
    /// <remarks>
    /// The 65C02 treats a fetch as a read. It passes <see cref="FastPathOps.Read"/> and does not
    /// consult <see cref="FastPathOps.Fetch"/>, so a missing execute permission does not raise
    /// <see cref="FaultKind.Nx"/>. A miss still uses <see cref="AccessIntent.InstructionFetch"/>.
    /// </remarks>
    private byte InstructionFetch8(Addr address) => ReadInstructionByte(address, FastPathOps.Read);

    /// <summary>
    /// Checks for pending interrupts and processes them if applicable.
    /// Uses the signal bus to poll interrupt lines.
    /// </summary>
    /// <returns>True if an interrupt was processed, false otherwise.</returns>
    /// <remarks>
    /// NMI has priority over IRQ. IRQ is maskable via the I flag.
    /// If the CPU is in WAI state, interrupts will resume execution.
    /// STP state cannot be resumed by interrupts.
    /// </remarks>
    private bool CheckInterrupts(bool irqMaskedAtBoundary)
    {
        // STP cannot be resumed by interrupts
        if (HaltReason == HaltState.Stp)
        {
            return false;
        }

        // Check for NMI (non-maskable, highest priority, edge-triggered)
        if (Signals.ConsumeNmiEdge())
        {
            // Resume from WAI if halted
            if (HaltReason == HaltState.Wai)
            {
                HaltReason = HaltState.None;
            }

            ProcessInterrupt(Cpu65C02Constants.NmiVector);
            return true;
        }

        // Check for IRQ (maskable by I flag, level-triggered)
        if (Signals.IsAsserted(SignalLine.IRQ))
        {
            // WAI clears the halt on any asserted IRQ, but only vectors when I is clear.
            if (HaltReason == HaltState.Wai)
            {
                HaltReason = HaltState.None;

                if (irqMaskedAtBoundary)
                {
                    return false;
                }
            }
            else if (irqMaskedAtBoundary)
            {
                return false;
            }

            ProcessInterrupt(Cpu65C02Constants.IrqVector);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes an interrupt by pushing state to the stack and loading the interrupt vector.
    /// </summary>
    /// <param name="vector">The address of the interrupt vector to load.</param>
    /// <remarks>
    /// Interrupt processing:
    /// 1. Push PC high byte to stack
    /// 2. Push PC low byte to stack
    /// 3. Push processor status (with B flag clear) to stack
    /// 4. Set I flag to disable interrupts
    /// 5. Load PC from interrupt vector
    /// Total: 7 cycles (handled by caller).
    /// </remarks>
    private void ProcessInterrupt(Addr vector)
    {
        var pc = Registers.PC.GetWord();

        // Push PC to stack (high byte first)
        Write8(PushByte(Cpu65C02Constants.StackBase), pc.HighByte());
        Write8(PushByte(Cpu65C02Constants.StackBase), pc.LowByte());

        // Push processor status (with B flag clear for hardware interrupts)
        Write8(PushByte(Cpu65C02Constants.StackBase), (byte)((Registers.P | ProcessorStatusFlags.R) & ~ProcessorStatusFlags.B));

        // Set I flag to disable further IRQs
        Registers.P.SetInterruptDisable(true);

        // 65C02: clear D flag on interrupt entry (NMOS 6502 leaves D unchanged)
        Registers.P.SetDecimalMode(false);

        // Load PC from interrupt vector
        Registers.PC.SetAddr(Read16(vector));

        // Account for 7 cycles for interrupt processing
        Registers.TCU += 7;
    }
}