// <copyright file="CpuBase.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Interfaces.Signaling;
using BadMango.Emulator.Core.Signaling;

using Core.Cpu;
using Core.Interfaces.Cpu;
using Core.Interfaces.Debugging;

/// <summary>
/// Abstract base class for CPU emulators providing common functionality.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides common implementation patterns shared across
/// different CPU emulators in the 65xx family (65C02, 65816, 65832).
/// </para>
/// <para>
/// Derived classes must implement:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Capabilities"/> - CPU-specific feature flags</description></item>
/// <item><description><see cref="Step"/> - Single instruction execution</description></item>
/// <item><description><see cref="Reset"/> - CPU-specific reset behavior</description></item>
/// </list>
/// </remarks>
public abstract class CpuBase : ICpu
{
    /// <summary>
    /// The source ID used for bus access tracing.
    /// </summary>
    protected const int CpuSourceId = 0;

    /// <summary>
    /// The event context providing access to bus, signals, and scheduler.
    /// </summary>
    private readonly IEventContext context;

    /// <summary>
    /// The memory bus for all memory operations.
    /// </summary>
    private readonly IMemoryBus bus;

    /// <summary>
    /// The same bus when it is a <see cref="MainBus"/>, so a byte access can index the live page
    /// slot without a type test. <see langword="null"/> for every other <see cref="IMemoryBus"/>.
    /// </summary>
    private readonly MainBus? mainBus;

    /// <summary>
    /// The signal bus for interrupt handling.
    /// </summary>
    private readonly ISignalBus signals;

    /// <summary>
    /// CPU registers (directly stored, no CpuState wrapper).
    /// </summary>
    private Registers registers;

    /// <summary>
    /// Halt state managed directly by CPU.
    /// </summary>
    private HaltState haltReason;

    /// <summary>
    /// Instruction trace for debug information.
    /// </summary>
    private InstructionTrace trace;

    /// <summary>
    /// Whether a stop has been requested.
    /// </summary>
    private bool stopRequested;

    /// <summary>
    /// The attached debug listener, if any.
    /// </summary>
    private IDebugStepListener? debugListener;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuBase"/> class with an event context.
    /// </summary>
    /// <param name="context">The event context providing access to the memory bus, signal bus, and scheduler.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    protected CpuBase(IEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        this.context = context;
        bus = context.Bus;
        mainBus = context.Bus as MainBus;
        signals = context.Signals;
    }

    /// <summary>
    /// Gets the event context providing access to bus, signals, and scheduler.
    /// </summary>
    /// <value>The event context for this CPU.</value>
    public IEventContext EventContext => context;

    /// <inheritdoc />
    public abstract CpuCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ref Registers Registers
    {
        get => ref registers;
    }

    /// <inheritdoc/>
    public bool Halted
    {
        get => haltReason != HaltState.None;
    }

    /// <inheritdoc/>
    public HaltState HaltReason
    {
        get => haltReason;
        set => haltReason = value;
    }

    /// <inheritdoc/>
    public bool IsWaitingForInterrupt
    {
        get => haltReason == HaltState.Wai;
    }

    /// <inheritdoc/>
    public bool IsDebuggerAttached
    {
        get => debugListener is not null;
    }

    /// <inheritdoc/>
    public bool IsStopRequested
    {
        get => stopRequested;
    }

    /// <inheritdoc/>
    public InstructionTrace Trace
    {
        get => trace;
        set => trace = value;
    }

    /// <summary>
    /// Gets the memory bus for all memory operations.
    /// </summary>
    protected IMemoryBus Bus => bus;

    /// <summary>
    /// Gets the signal bus for interrupt handling.
    /// </summary>
    protected ISignalBus Signals => signals;

    /// <summary>
    /// Gets the attached debug listener, or <see langword="null"/> if none is attached.
    /// </summary>
    protected IDebugStepListener? DebugListener => debugListener;

    /// <inheritdoc/>
    public abstract CpuStepResult Step();

    /// <inheritdoc/>
    public abstract void Reset();

    /// <inheritdoc/>
    public void Execute(uint startAddress)
    {
        registers.PC.SetAddr(startAddress);
        haltReason = HaltState.None;
        stopRequested = false;

        while (!Halted && !stopRequested)
        {
            Step();
        }
    }

    /// <inheritdoc/>
    public Registers GetRegisters()
    {
        return registers;
    }

    /// <inheritdoc/>
    public ulong GetCycles()
    {
        // Return the scheduler's current cycle count plus any pending TCU cycles.
        // TCU holds cycles accumulated during instruction execution.
        // When called via Step(), TCU will have already been cleared for the next instruction,
        // so this effectively returns scheduler.Now.
        // When called after a direct handler invocation (unit test pattern), TCU holds
        // the cycles that haven't been flushed to the scheduler yet.
        return context.Now.Value + registers.TCU.Value;
    }

    /// <inheritdoc/>
    public void SetCycles(ulong cycles)
    {
        // If the new cycle count is greater than the current scheduler time, advance the scheduler to match
        // This maintains backward compatibility with code that sets cycles via state
        if (cycles > context.Now.Value)
        {
            context.Scheduler.Advance(new(cycles - context.Now.Value));
        }
    }

    /// <inheritdoc/>
    public void SignalIRQ()
    {
        signals.Assert(SignalLine.IRQ, CpuSourceId, context.Now);
    }

    /// <inheritdoc/>
    public void SignalNMI()
    {
        signals.Assert(SignalLine.NMI, CpuSourceId, context.Now);
    }

    /// <inheritdoc/>
    public void AttachDebugger(IDebugStepListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        debugListener = listener;
    }

    /// <inheritdoc/>
    public void DetachDebugger()
    {
        debugListener = null;

        // Clear trace state so handlers that inspect it (e.g. immediate-mode operand capture)
        // do not keep acting on a stale trace from the last debugged instruction.
        trace = default;
    }

    /// <inheritdoc/>
    public void SetPC(Addr address)
    {
        registers.PC.SetAddr(address);
    }

    /// <inheritdoc/>
    public Addr GetPC()
    {
        return registers.PC.GetAddr();
    }

    /// <inheritdoc/>
    public void RequestStop()
    {
        stopRequested = true;
    }

    /// <inheritdoc/>
    public void ClearStopRequest()
    {
        stopRequested = false;
    }

    // ─── Memory Access Methods (Bus-based) ──────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// A <see cref="MainBus"/> page that publishes <see cref="FastPathOps.Read"/> is indexed.
    /// Every other read calls <see cref="IMemoryBus.TryRead8"/>.
    /// </remarks>
    public virtual byte Read8(Addr address)
    {
        if (TryReadFastByte(address, FastPathOps.Read, out byte fastValue))
        {
            return fastValue;
        }

        var access = CreateReadAccess(address, 8);
        var result = bus.TryRead8(access);

        if (result.Failed)
        {
            // Handle bus fault - for now, return 0xFF (floating bus) and halt on unmapped
            if (result.Fault.Kind == FaultKind.Unmapped)
            {
                haltReason = HaltState.Stp;
            }

            return 0xFF;
        }

        return result.Value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A <see cref="MainBus"/> page that publishes <see cref="FastPathOps.Write"/> is indexed.
    /// A <see cref="RomTarget"/> never publishes write, so a ROM store stays on
    /// <see cref="IMemoryBus.TryWrite8"/>.
    /// </remarks>
    public virtual void Write8(Addr address, byte value)
    {
        if (TryWriteFastByte(address, value))
        {
            return;
        }

        var access = CreateWriteAccess(address, 8);
        var result = bus.TryWrite8(access, value);

        if (result.Failed && result.Fault.Kind == FaultKind.Unmapped)
        {
            // Write to unmapped page - silently ignore for write operations
            // (some systems have write-only regions or ignore writes to ROM)
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A debug read indexes a page that publishes <see cref="FastPathOps.Read"/>, because that
    /// page has no side effects. Every other debug read calls <see cref="IMemoryBus.TryRead8"/>.
    /// </remarks>
    public virtual byte Peek8(Addr address)
    {
        if (TryReadFastByte(address, FastPathOps.Read, out byte fastValue))
        {
            return fastValue;
        }

        var access = CreateDebugReadAccess(address);
        var result = bus.TryRead8(access);

        if (result.Failed)
        {
            // Return 0xFF for any bus fault (unmapped, permission denied, etc.)
            // This matches Apple II floating bus behavior and is safe for debug reads
            return 0xFF;
        }

        return result.Value;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A debug write indexes a page only when it publishes <see cref="FastPathOps.Write"/>.
    /// A ROM poke, and every poke of a page that is not eligible, calls
    /// <see cref="IMemoryBus.TryWrite8"/> so <see cref="RomTarget.Write8"/> keeps the debug-write rule.
    /// </remarks>
    public virtual void Poke8(Addr address, byte value)
    {
        if (TryWriteFastByte(address, value))
        {
            return;
        }

        var access = CreateDebugWriteAccess(address);
        bus.TryWrite8(access, value);

        // Debug writes silently ignore failures for these reasons:
        // 1. Unmapped regions: acceptable in test scenarios
        // 2. Permission denied: handled by bus bypassing check for DebugWrite
        // 3. Target doesn't support poke: RomTarget constructed with ReadOnlyMemory
        // If a test expects poke to work and it doesn't, the test will fail
        // on subsequent read verification, which is the appropriate failure point.
    }

    /// <inheritdoc/>
    public virtual Word Read16(Addr address)
    {
        var access = CreateReadAccess(address, 16);
        var result = bus.TryRead16(access);

        if (result.Failed)
        {
            if (result.Fault.Kind == FaultKind.Unmapped)
            {
                haltReason = HaltState.Stp;
            }

            return 0xFFFF;
        }

        return result.Value;
    }

    /// <inheritdoc/>
    public virtual void Write16(Addr address, Word value)
    {
        var access = CreateWriteAccess(address, 16);
        bus.TryWrite16(access, value);
    }

    /// <inheritdoc/>
    public virtual DWord ReadValue(Addr address, byte sizeInBits)
    {
        return sizeInBits switch
        {
            8 => Read8(address),
            16 => Read16(address),
            32 => Read32(address),
            _ => throw new ArgumentException($"Invalid size: {sizeInBits}. Must be 8, 16, or 32.", nameof(sizeInBits)),
        };
    }

    /// <inheritdoc/>
    public virtual void WriteValue(Addr address, DWord value, byte sizeInBits)
    {
        switch (sizeInBits)
        {
            case 8:
                Write8(address, (byte)(value & 0xFF));
                break;
            case 16:
                Write16(address, (Word)(value & 0xFFFF));
                break;
            case 32:
                Write32(address, value);
                break;
            default:
                throw new ArgumentException($"Invalid size: {sizeInBits}. Must be 8, 16, or 32.", nameof(sizeInBits));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The 6502/65C02 stack pointer is 8 bits wide: pushing at SP=$00 writes
    /// <c>stackBase + $00</c> and wraps SP to $FF. CPUs with a wider native
    /// stack (e.g. 65816 native mode) must override this method.
    /// </remarks>
    public virtual Addr PushByte(Addr stackBase = 0)
    {
        byte sp = registers.SP.GetByte();
        registers.SP.SetDWord(unchecked((byte)(sp - 1)));
        return stackBase + sp;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The 6502/65C02 stack pointer is 8 bits wide: popping at SP=$FF wraps SP
    /// to $00 and reads <c>stackBase + $00</c>. CPUs with a wider native stack
    /// (e.g. 65816 native mode) must override this method.
    /// </remarks>
    public virtual Addr PopByte(Addr stackBase = 0)
    {
        byte sp = unchecked((byte)(registers.SP.GetByte() + 1));
        registers.SP.SetDWord(sp);
        return stackBase + sp;
    }

    // ─── Protected Helper Methods ───────────────────────────────────────

    /// <summary>
    /// Reads one instruction byte, indexing a <see cref="MainBus"/> page when
    /// <paramref name="fetchOperation"/> is published for it.
    /// </summary>
    /// <param name="address">The address to fetch from.</param>
    /// <param name="fetchOperation">
    /// The fast-path operation this fetch may use. The 65C02 passes <see cref="FastPathOps.Read"/>
    /// and does not consult <see cref="FastPathOps.Fetch"/>. A native CPU passes
    /// <see cref="FastPathOps.Fetch"/> when the emulation flag is clear.
    /// </param>
    /// <returns>The byte at <paramref name="address"/>, or <c>0xFF</c> when the checked read faults.</returns>
    /// <remarks>
    /// <para>
    /// A hit does not build a <see cref="BusAccess"/>, does not call <see cref="GetEmulationFlag"/>,
    /// does not read <see cref="IEventContext.Now"/>, and does not change <see cref="HaltReason"/>.
    /// A miss calls <see cref="IMemoryBus.TryRead8"/> with <see cref="AccessIntent.InstructionFetch"/>.
    /// An unmapped miss sets <see cref="HaltState.Stp"/> and returns <c>0xFF</c>. Any other fault
    /// returns <c>0xFF</c> without setting <see cref="HaltState.Stp"/>.
    /// </para>
    /// <para>
    /// The method does not add to <see cref="Registers.TCU"/>. <c>FetchByte</c> still adds the
    /// instruction-fetch cycle.
    /// </para>
    /// </remarks>
    protected byte ReadInstructionByte(Addr address, FastPathOps fetchOperation)
    {
        if (TryReadFastByte(address, fetchOperation, out byte fastValue))
        {
            return fastValue;
        }

        var access = CreateInstructionFetchAccess(address, 8);
        var result = bus.TryRead8(access);

        if (result.Failed)
        {
            if (result.Fault.Kind == FaultKind.Unmapped)
            {
                haltReason = HaltState.Stp;
            }

            return 0xFF;
        }

        return result.Value;
    }

    /// <summary>
    /// Reads a 32-bit value from memory.
    /// </summary>
    /// <param name="address">The address to read from.</param>
    /// <returns>The 32-bit value at the address.</returns>
    protected DWord Read32(Addr address)
    {
        var access = CreateReadAccess(address, 32);
        var result = bus.TryRead32(access);

        if (result.Failed)
        {
            if (result.Fault.Kind == FaultKind.Unmapped)
            {
                haltReason = HaltState.Stp;
            }

            return 0xFFFFFFFF;
        }

        return result.Value;
    }

    /// <summary>
    /// Writes a 32-bit value to memory.
    /// </summary>
    /// <param name="address">The address to write to.</param>
    /// <param name="value">The 32-bit value to write.</param>
    protected void Write32(Addr address, DWord value)
    {
        var access = CreateWriteAccess(address, 32);
        bus.TryWrite32(access, value);
    }

    /// <summary>
    /// Creates a bus access context for read operations.
    /// </summary>
    /// <param name="address">The address being accessed.</param>
    /// <param name="widthBits">The width of the access in bits.</param>
    /// <returns>A configured <see cref="BusAccess"/> for reading.</returns>
    protected BusAccess CreateReadAccess(Addr address, byte widthBits)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: GetEmulationFlag(),
            Intent: AccessIntent.DataRead,
            SourceId: CpuSourceId,
            Cycle: context.Now + registers.TCU,
            Flags: AccessFlags.None);
    }

    /// <summary>
    /// Creates a bus access context for write operations.
    /// </summary>
    /// <param name="address">The address being accessed.</param>
    /// <param name="widthBits">The width of the access in bits.</param>
    /// <returns>A configured <see cref="BusAccess"/> for writing.</returns>
    protected BusAccess CreateWriteAccess(Addr address, byte widthBits)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: GetEmulationFlag(),
            Intent: AccessIntent.DataWrite,
            SourceId: CpuSourceId,
            Cycle: context.Now + registers.TCU,
            Flags: AccessFlags.None);
    }

    /// <summary>
    /// Creates a bus access context for instruction fetch operations.
    /// </summary>
    /// <param name="address">The address being accessed.</param>
    /// <param name="widthBits">The width of the access in bits (8, 16, etc.).</param>
    /// <returns>A configured <see cref="BusAccess"/> for instruction fetch.</returns>
    /// <remarks>
    /// Instruction fetches use <see cref="AccessIntent.InstructionFetch"/> to allow the bus
    /// to differentiate between data reads and code fetches. This enables NX enforcement
    /// and trap interception at ROM entry points.
    /// </remarks>
    protected BusAccess CreateInstructionFetchAccess(Addr address, byte widthBits)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: widthBits,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: GetEmulationFlag(),
            Intent: AccessIntent.InstructionFetch,
            SourceId: CpuSourceId,
            Cycle: context.Now + registers.TCU,
            Flags: AccessFlags.None);
    }

    /// <summary>
    /// Creates a bus access context for debug read (peek) operations.
    /// </summary>
    /// <param name="address">The address being accessed.</param>
    /// <returns>A configured <see cref="BusAccess"/> for debug read.</returns>
    /// <remarks>
    /// Debug reads use <see cref="AccessIntent.DebugRead"/> to read memory without
    /// triggering side effects such as soft switches or flag clearing.
    /// </remarks>
    protected BusAccess CreateDebugReadAccess(Addr address)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: GetEmulationFlag(),
            Intent: AccessIntent.DebugRead,
            SourceId: CpuSourceId,
            Cycle: context.Now,
            Flags: AccessFlags.NoSideEffects);
    }

    /// <summary>
    /// Creates a bus access context for debug write (poke) operations.
    /// </summary>
    /// <param name="address">The address being accessed.</param>
    /// <returns>A configured <see cref="BusAccess"/> for debug write.</returns>
    /// <remarks>
    /// Debug writes use <see cref="AccessIntent.DebugWrite"/> to write memory,
    /// bypassing ROM write protection and avoiding side effects on I/O addresses.
    /// </remarks>
    protected BusAccess CreateDebugWriteAccess(Addr address)
    {
        return new(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: GetEmulationFlag(),
            Intent: AccessIntent.DebugWrite,
            SourceId: CpuSourceId,
            Cycle: context.Now,
            Flags: AccessFlags.NoSideEffects);
    }

    /// <summary>
    /// Gets the emulation flag value for bus access operations.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the CPU is in emulation mode (65C02 compatibility);
    /// <see langword="false"/> if in native mode (65816/65832).
    /// </returns>
    /// <remarks>
    /// The 65C02 is always in emulation mode. The 65816 and 65832 can switch between
    /// emulation and native modes. Derived classes should override this method to
    /// return the appropriate value based on their current mode.
    /// </remarks>
    protected virtual bool GetEmulationFlag() => true;

    /// <summary>
    /// Indexes the live page slot when <paramref name="operation"/> is published for <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="operation">The single fast-path operation this access requires.</param>
    /// <param name="value">The indexed byte when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the page index is in range and <see cref="PageEntry.FastOps"/>
    /// contains <paramref name="operation"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Eligibility is <see cref="PageEntry.FastOps"/>, not a null backing array. An eligible page
    /// that grants no operation keeps the array and stays on the checked path. The index is
    /// <c>FastBase + PhysicalBase + (address &amp; page mask)</c> into the live slot. This method
    /// takes no lock and does not read a mapping-trace flag.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadFastByte(Addr address, FastPathOps operation, out byte value)
    {
        if (!TryGetFastIndex(address, operation, out byte[]? backing, out int index))
        {
            value = 0;
            return false;
        }

        value = backing[index];
        return true;
    }

    /// <summary>
    /// Stores one byte when the live page publishes <see cref="FastPathOps.Write"/>.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="value">The byte to store.</param>
    /// <returns>
    /// <see langword="true"/> when the store was an array index; otherwise <see langword="false"/>
    /// and the caller uses <see cref="IMemoryBus.TryWrite8"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteFastByte(Addr address, byte value)
    {
        if (!TryGetFastIndex(address, FastPathOps.Write, out byte[]? backing, out int index))
        {
            return false;
        }

        backing[index] = value;
        return true;
    }

    /// <summary>
    /// Resolves the live backing array and index for one fast-path byte.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="operation">The operation that must be published on the page.</param>
    /// <param name="backing">The page array when this method returns <see langword="true"/>.</param>
    /// <param name="index">The index of <paramref name="address"/> in <paramref name="backing"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the page can be indexed for <paramref name="operation"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetFastIndex(
        Addr address,
        FastPathOps operation,
        [NotNullWhen(true)] out byte[]? backing,
        out int index)
    {
        backing = null;
        index = 0;

        MainBus? fastBus = mainBus;
        if (fastBus is null)
        {
            return false;
        }

        int pageIndex = (int)(address >> fastBus.PageShift);
        if ((uint)pageIndex >= (uint)fastBus.PageCount)
        {
            return false;
        }

        // Read the live slot. A copied PageEntry would freeze FastBase and PhysicalBase.
        ref readonly PageEntry page = ref fastBus.GetLivePage(pageIndex);
        byte[]? slot = page.FastBacking;
        if ((page.FastOps & operation) == 0 || slot is null)
        {
            return false;
        }

        backing = slot;
        index = page.FastBase + (int)page.PhysicalBase + (int)(address & fastBus.PageMask);
        return true;
    }
}