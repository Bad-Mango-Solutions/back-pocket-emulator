// <copyright file="Cpu65C02BusCycleAndPcWrapTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using System.Linq;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Signaling;
using BadMango.Emulator.Emulation.Cpu;

/// <summary>
/// Verifies bus-cycle stamping and 16-bit program-counter wrap behavior for the 65C02 core.
/// </summary>
[TestFixture]
public sealed class Cpu65C02BusCycleAndPcWrapTests
{
    private MainBus bus = null!;
    private Scheduler scheduler = null!;
    private SignalBus signals = null!;
    private Cpu65C02 cpu = null!;
    private RecordingTarget target = null!;

    /// <summary>
    /// Initializes a fresh 64KB bus/CPU test fixture before each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        bus = new MainBus(16);
        target = new RecordingTarget(0x10000);
        bus.MapPageRange(
            startPage: 0,
            pageCount: 16,
            deviceId: 0,
            regionTag: RegionTag.Ram,
            perms: PagePerms.All,
            caps: target.Capabilities,
            target: target,
            physicalBase: 0);

        scheduler = new Scheduler();
        signals = new SignalBus();
        cpu = new Cpu65C02(new EventContext(scheduler, signals, bus));
    }

    /// <summary>
    /// Verifies that each bus access uses instruction-start cycle plus current TCU offset.
    /// </summary>
    [Test]
    public void Step_IncAbsolute_StampsBusAccessCyclesByTcuOffset()
    {
        SetResetVector(0x2000);
        Write(0x2000, 0xEE); // INC $1234
        Write(0x2001, 0x34);
        Write(0x2002, 0x12);
        Write(0x1234, 0x10);

        cpu.Reset();
        target.ClearAccesses();

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(target.Accesses.Select(a => a.Address), Is.EqualTo(new Addr[] { 0x2000, 0x2001, 0x2002, 0x1234, 0x1234 }));
            Assert.That(target.Accesses.Select(a => a.Cycle), Is.EqualTo(new ulong[] { 0, 1, 1, 3, 3 }));
            Assert.That(target.Accesses.Select(a => a.Intent), Is.EqualTo(
                new[]
                {
                    AccessIntent.InstructionFetch,
                    AccessIntent.DataRead,
                    AccessIntent.DataRead,
                    AccessIntent.DataRead,
                    AccessIntent.DataWrite,
                }));
        });
    }

    /// <summary>
    /// Pins unused NOP <c>$5C</c>: a 3-byte fetch, one read of <c>$FF</c> plus the low operand byte,
    /// then four reads of <c>$FFFF</c>. The step takes 8 cycles. The high operand byte is not an address.
    /// </summary>
    /// <param name="low">The low operand byte. The fourth bus read is <c>$FF00 | low</c>.</param>
    /// <param name="high">The high operand byte. It is fetched and then discarded.</param>
    /// <remarks>
    /// Table 7-1 of the WDC W65C02S datasheet lists 8 cycles. KimKlone measured <c>5C 34 12</c> as one
    /// read of <c>$FF34</c> and then four reads of <c>$FFFF</c>
    /// (https://laughtonelectronics.com/Arcana/KimKlone/Kimklone_opcode_mapping.html), cited by
    /// https://github.com/SingleStepTests/65x02/issues/12.
    /// </remarks>
    [TestCase((byte)0x34, (byte)0x12)]
    [TestCase((byte)0xA7, (byte)0x56)]
    public void Step_Nop5C_ReadsFfLowOperandThenFourTimesFfff(byte low, byte high)
    {
        SetResetVector(0x2000);
        Write(0x2000, 0x5C);
        Write(0x2001, low);
        Write(0x2002, high);
        cpu.Reset();
        target.ClearAccesses();

        var step = cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(step.CyclesConsumed.Value, Is.EqualTo(8UL));
            Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x2003));
            Assert.That(target.Accesses.Select(a => a.Address), Is.EqualTo(new Addr[]
            {
                0x2000,
                0x2001,
                0x2002,
                (Addr)(0xFF00 | low),
                0xFFFF,
                0xFFFF,
                0xFFFF,
                0xFFFF,
            }));
            Assert.That(target.Accesses.Select(a => a.Intent), Is.EqualTo(new[]
            {
                AccessIntent.InstructionFetch,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
                AccessIntent.DataRead,
            }));
        });
    }

    /// <summary>
    /// Verifies that a one-byte opcode fetched at $FFFF wraps the PC to $0000.
    /// </summary>
    [Test]
    public void Step_OpcodeFetchAtFfff_WrapsProgramCounterToZero()
    {
        SetResetVector(0xFFFF);
        Write(0xFFFF, 0xEA); // NOP
        cpu.Reset();

        cpu.Step();

        Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x0000));
    }

    /// <summary>
    /// Verifies that a two-byte instruction straddling $FFFF reads its operand at $0000.
    /// </summary>
    [Test]
    public void Step_TwoByteInstructionAcrossFfff_ReadsOperandFromZero()
    {
        SetResetVector(0xFFFF);
        Write(0xFFFF, 0xA9); // LDA #$42
        Write(0x0000, 0x42);
        cpu.Reset();

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x42));
            Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x0001));
        });
    }

    /// <summary>
    /// Verifies that a three-byte instruction straddling $FFFF reads both operand bytes after wrap.
    /// </summary>
    [Test]
    public void Step_ThreeByteInstructionAcrossFfff_ReadsOperandsFromZeroAndOne()
    {
        SetResetVector(0xFFFF);
        Write(0xFFFF, 0xAD); // LDA $1234
        Write(0x0000, 0x34);
        Write(0x0001, 0x12);
        Write(0x1234, 0x99);
        cpu.Reset();

        cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Registers.A.GetByte(), Is.EqualTo(0x99));
            Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x0002));
        });
    }

    /// <summary>
    /// Verifies that RTS wraps from $FFFF to $0000 when applying its post-pop increment.
    /// </summary>
    [Test]
    public void Step_RtsWithReturnAddressFfff_WrapsProgramCounterToZero()
    {
        SetResetVector(0x2000);
        Write(0x2000, 0x60); // RTS
        cpu.Reset();

        cpu.Registers.SP.SetByte(0xFD);
        Write(0x01FE, 0xFF); // PCL
        Write(0x01FF, 0xFF); // PCH

        cpu.Step();

        Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x0000));
    }

    /// <summary>
    /// Verifies that RTI can return to $FFFF and the next fetch wraps to $0000.
    /// </summary>
    [Test]
    public void Step_RtiReturningToFfff_NextFetchWrapsToZero()
    {
        SetResetVector(0x2000);
        Write(0x2000, 0x40); // RTI
        Write(0xFFFF, 0xEA); // NOP at wrap boundary
        cpu.Reset();

        cpu.Registers.SP.SetByte(0xFC);
        Write(0x01FD, 0x20); // P
        Write(0x01FE, 0xFF); // PCL
        Write(0x01FF, 0xFF); // PCH

        cpu.Step(); // RTI -> PC=$FFFF
        cpu.Step(); // Fetch at $FFFF -> wraps to $0000

        Assert.That(cpu.Registers.PC.GetWord(), Is.EqualTo(0x0000));
    }

    private void SetResetVector(Word resetVector)
    {
        Write(0xFFFC, (byte)(resetVector & 0xFF));
        Write(0xFFFD, (byte)(resetVector >> 8));
    }

    private void Write(Addr address, byte value)
    {
        target.Write(address, value);
    }

    private sealed class RecordingTarget : IBusTarget
    {
        private readonly byte[] memory;

        public RecordingTarget(int size)
        {
            memory = new byte[size];
        }

        public List<BusAccess> Accesses { get; } = [];

        public TargetCaps Capabilities => TargetCaps.None;

        public byte Read8(Addr offset, in BusAccess access)
        {
            Accesses.Add(access);
            return memory[(int)(offset & 0xFFFF)];
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            Accesses.Add(access);
            memory[(int)(offset & 0xFFFF)] = value;
        }

        public void Write(Addr address, byte value)
        {
            memory[(int)(address & 0xFFFF)] = value;
        }

        public void ClearAccesses()
        {
            Accesses.Clear();
        }
    }
}