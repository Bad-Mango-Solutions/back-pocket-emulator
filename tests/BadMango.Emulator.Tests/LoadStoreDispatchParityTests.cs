// <copyright file="LoadStoreDispatchParityTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Compares direct-dispatch load and store opcodes with the 65C02 result for
/// registers, flags, memory, the program counter, and cycles. Each case runs
/// through <see cref="Cpu65C02.Step"/>, which fetches the opcode and calls the
/// cached <see cref="OpcodeDispatch"/> delegate.
/// </summary>
[TestFixture]
public class LoadStoreDispatchParityTests : CpuTestBase
{
    private const Word StartPc = 0x1000;
    private const ulong StartCycles = 10;
    private const byte IndexX = 0x05;
    private const byte IndexY = 0x06;
    private const byte SentinelA = 0x11;
    private const byte SentinelX = IndexX;
    private const byte SentinelY = IndexY;
    private const uint SentinelSp = 0x01FD;

    // C, I, D, R, and V. Loads must leave these bits alone.
    private const byte Preserved = 0x6D;

    // Preserved bits with Z and N already set, so a clear is visible.
    private const byte PreservedWithZeroAndNegative = 0xEF;

    private static readonly OpcodeTable Table = Cpu65C02OpcodeTableBuilder.Build();

    private static readonly LoadOpcode[] Loads =
    [
        new(0xA9, "LDA #", CpuInstructions.LDA, CpuAddressingModes.Immediate, 2, 2, LoadTarget.A),
        new(0xA5, "LDA zp", CpuInstructions.LDA, CpuAddressingModes.ZeroPage, 2, 3, LoadTarget.A),
        new(0xB5, "LDA zp,X", CpuInstructions.LDA, CpuAddressingModes.ZeroPageX, 2, 4, LoadTarget.A),
        new(0xAD, "LDA abs", CpuInstructions.LDA, CpuAddressingModes.Absolute, 3, 4, LoadTarget.A),
        new(0xBD, "LDA abs,X", CpuInstructions.LDA, CpuAddressingModes.AbsoluteX, 3, 4, LoadTarget.A),
        new(0xB9, "LDA abs,Y", CpuInstructions.LDA, CpuAddressingModes.AbsoluteY, 3, 4, LoadTarget.A),
        new(0xA1, "LDA (zp,X)", CpuInstructions.LDA, CpuAddressingModes.IndirectX, 2, 6, LoadTarget.A),
        new(0xB1, "LDA (zp),Y", CpuInstructions.LDA, CpuAddressingModes.IndirectY, 2, 5, LoadTarget.A),
        new(0xB2, "LDA (zp)", CpuInstructions.LDA, CpuAddressingModes.ZeroPageIndirect, 2, 5, LoadTarget.A),
        new(0xA2, "LDX #", CpuInstructions.LDX, CpuAddressingModes.Immediate, 2, 2, LoadTarget.X),
        new(0xA6, "LDX zp", CpuInstructions.LDX, CpuAddressingModes.ZeroPage, 2, 3, LoadTarget.X),
        new(0xB6, "LDX zp,Y", CpuInstructions.LDX, CpuAddressingModes.ZeroPageY, 2, 4, LoadTarget.X),
        new(0xAE, "LDX abs", CpuInstructions.LDX, CpuAddressingModes.Absolute, 3, 4, LoadTarget.X),
        new(0xBE, "LDX abs,Y", CpuInstructions.LDX, CpuAddressingModes.AbsoluteY, 3, 4, LoadTarget.X),
        new(0xA0, "LDY #", CpuInstructions.LDY, CpuAddressingModes.Immediate, 2, 2, LoadTarget.Y),
        new(0xA4, "LDY zp", CpuInstructions.LDY, CpuAddressingModes.ZeroPage, 2, 3, LoadTarget.Y),
        new(0xB4, "LDY zp,X", CpuInstructions.LDY, CpuAddressingModes.ZeroPageX, 2, 4, LoadTarget.Y),
        new(0xAC, "LDY abs", CpuInstructions.LDY, CpuAddressingModes.Absolute, 3, 4, LoadTarget.Y),
        new(0xBC, "LDY abs,X", CpuInstructions.LDY, CpuAddressingModes.AbsoluteX, 3, 4, LoadTarget.Y),
    ];

    private static readonly StoreOpcode[] Stores =
    [
        new(0x85, "STA zp", CpuInstructions.STA, CpuAddressingModes.ZeroPage, 2, 3, StoreSource.A),
        new(0x95, "STA zp,X", CpuInstructions.STA, CpuAddressingModes.ZeroPageX, 2, 4, StoreSource.A),
        new(0x8D, "STA abs", CpuInstructions.STA, CpuAddressingModes.Absolute, 3, 4, StoreSource.A),
        new(0x9D, "STA abs,X", CpuInstructions.STA, CpuAddressingModes.AbsoluteX, 3, 5, StoreSource.A),
        new(0x99, "STA abs,Y", CpuInstructions.STA, CpuAddressingModes.AbsoluteY, 3, 5, StoreSource.A),
        new(0x81, "STA (zp,X)", CpuInstructions.STA, CpuAddressingModes.IndirectX, 2, 6, StoreSource.A),
        new(0x91, "STA (zp),Y", CpuInstructions.STA, CpuAddressingModes.IndirectY, 2, 6, StoreSource.A),
        new(0x92, "STA (zp)", CpuInstructions.STA, CpuAddressingModes.ZeroPageIndirect, 2, 5, StoreSource.A),
        new(0x86, "STX zp", CpuInstructions.STX, CpuAddressingModes.ZeroPage, 2, 3, StoreSource.X),
        new(0x96, "STX zp,Y", CpuInstructions.STX, CpuAddressingModes.ZeroPageY, 2, 4, StoreSource.X),
        new(0x8E, "STX abs", CpuInstructions.STX, CpuAddressingModes.Absolute, 3, 4, StoreSource.X),
        new(0x84, "STY zp", CpuInstructions.STY, CpuAddressingModes.ZeroPage, 2, 3, StoreSource.Y),
        new(0x94, "STY zp,X", CpuInstructions.STY, CpuAddressingModes.ZeroPageX, 2, 4, StoreSource.Y),
        new(0x8C, "STY abs", CpuInstructions.STY, CpuAddressingModes.Absolute, 3, 4, StoreSource.Y),
        new(0x64, "STZ zp", CpuInstructions.STZ, CpuAddressingModes.ZeroPage, 2, 3, StoreSource.Zero),
        new(0x74, "STZ zp,X", CpuInstructions.STZ, CpuAddressingModes.ZeroPageX, 2, 4, StoreSource.Zero),
        new(0x9C, "STZ abs", CpuInstructions.STZ, CpuAddressingModes.Absolute, 3, 4, StoreSource.Zero),
        new(0x9E, "STZ abs,X", CpuInstructions.STZ, CpuAddressingModes.AbsoluteX, 3, 5, StoreSource.Zero),
    ];

    private enum LoadTarget
    {
        A,
        X,
        Y,
    }

    private enum StoreSource
    {
        A,
        X,
        Y,
        Zero,
    }

    /// <summary>
    /// Every load opcode, in every addressing mode, copies the operand into its
    /// register and sets Z and N from that byte. Zero, a small positive, and a
    /// negative value are covered. C, I, D, R, and V stay set.
    /// </summary>
    /// <param name="opcode">The load opcode.</param>
    /// <param name="value">The byte the instruction must load.</param>
    [TestCaseSource(nameof(LoadValueCases))]
    public void Load_DirectDispatch_WritesRegisterAndSetsZeroAndNegative(int opcode, int value)
    {
        LoadOpcode spec = FindLoad((byte)opcode);
        byte loaded = (byte)value;
        PlantLoad(spec.Opcode, loaded);
        Prepare(SentinelA, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        byte expectedA = spec.Target == LoadTarget.A ? loaded : SentinelA;
        byte expectedX = spec.Target == LoadTarget.X ? loaded : SentinelX;
        byte expectedY = spec.Target == LoadTarget.Y ? loaded : SentinelY;
        AssertMachine(
            spec,
            expectedA,
            expectedX,
            expectedY,
            FlagsFor(loaded),
            result);
    }

    /// <summary>
    /// Every store opcode writes its source and leaves flags and the other
    /// registers alone. STZ writes zero. Indexed stores include the fixed
    /// write-cycle penalty and do not add a second page-cross cycle.
    /// </summary>
    /// <param name="opcode">The store opcode.</param>
    /// <param name="sourceValue">The register value to store. STZ ignores it and writes zero.</param>
    [TestCaseSource(nameof(StoreValueCases))]
    public void Store_DirectDispatch_WritesMemoryAndPreservesFlags(int opcode, int sourceValue)
    {
        StoreOpcode spec = FindStore((byte)opcode);
        byte stored = spec.Source == StoreSource.Zero ? (byte)0 : (byte)sourceValue;
        ushort destination = PlantStore(spec.Opcode, 0xAA);
        byte a = spec.Source == StoreSource.A ? (byte)sourceValue : SentinelA;
        byte x = spec.Source == StoreSource.X ? (byte)sourceValue : SentinelX;
        byte y = spec.Source == StoreSource.Y ? (byte)sourceValue : SentinelY;
        Prepare(a, x, y, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(spec, a, x, y, PreservedWithZeroAndNegative, result);
            Assert.That(Read(destination), Is.EqualTo(stored), $"{spec.Name} memory");
        });
    }

    /// <summary>
    /// Indexed loads add one cycle only when the index crosses a page.
    /// The same-page form stays on the datasheet base count.
    /// </summary>
    /// <param name="opcode">LDA abs,X, LDA abs,Y, LDX abs,Y, or LDY abs,X.</param>
    /// <param name="baseAddress">The absolute operand before indexing.</param>
    /// <param name="index">The index register added to <paramref name="baseAddress"/>.</param>
    /// <param name="expectedCycles">Base cycles, plus one when the addition crosses a page.</param>
    [TestCase(0xBD, 0x2000, 0x10, 4)]
    [TestCase(0xBD, 0x20F0, 0x20, 5)]
    [TestCase(0xBD, 0x20FF, 0x01, 5)]
    [TestCase(0xB9, 0x2100, 0x06, 4)]
    [TestCase(0xB9, 0x21FF, 0x01, 5)]
    [TestCase(0xBE, 0x2200, 0x06, 4)]
    [TestCase(0xBE, 0x22FE, 0x02, 5)]
    [TestCase(0xBC, 0x2300, 0x05, 4)]
    [TestCase(0xBC, 0x23FF, 0x01, 5)]
    public void IndexedLoad_PageCrossPenalty_MatchesDatasheet(int opcode, int baseAddress, int index, int expectedCycles)
    {
        LoadOpcode spec = FindLoad((byte)opcode);
        bool usesY = opcode is 0xB9 or 0xBE;
        byte x = usesY ? SentinelX : (byte)index;
        byte y = usesY ? (byte)index : SentinelY;
        ushort effective = (ushort)((baseAddress + index) & 0xFFFF);
        Write(StartPc, spec.Opcode);
        WriteWord(StartPc + 1, (ushort)baseAddress);
        Write(effective, 0x80);
        Prepare(SentinelA, x, y, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        byte expectedA = spec.Target == LoadTarget.A ? (byte)0x80 : SentinelA;
        byte expectedX = spec.Target == LoadTarget.X ? (byte)0x80 : x;
        byte expectedY = spec.Target == LoadTarget.Y ? (byte)0x80 : y;
        AssertMachine(spec, expectedA, expectedX, expectedY, FlagsFor(0x80), result, expectedCycles);
    }

    /// <summary>
    /// LDA (zp),Y adds a page-cross cycle. STA (zp),Y always takes the write penalty.
    /// </summary>
    /// <param name="opcode">LDA (zp),Y or STA (zp),Y.</param>
    /// <param name="pointer">The 16-bit pointer stored at zero page <c>$40</c>.</param>
    /// <param name="y">The Y index added to <paramref name="pointer"/>.</param>
    /// <param name="expectedCycles">Five or six for the load. Six for both store cases.</param>
    [TestCase(0xB1, 0x2000, 0x20, 5)]
    [TestCase(0xB1, 0x20FF, 0x01, 6)]
    [TestCase(0x91, 0x3000, 0x10, 6)]
    [TestCase(0x91, 0x30FF, 0x02, 6)]
    public void IndirectY_PageCrossAndWritePenalty_MatchDatasheet(int opcode, int pointer, int y, int expectedCycles)
    {
        Write(0x0040, (byte)(pointer & 0xFF));
        Write(0x0041, (byte)((pointer >> 8) & 0xFF));
        Write(StartPc, (byte)opcode);
        Write(StartPc + 1, 0x40);
        ushort effective = (ushort)((pointer + y) & 0xFFFF);
        Prepare(0x5A, SentinelX, (byte)y, PreservedWithZeroAndNegative);

        if (opcode == 0xB1)
        {
            Write(effective, 0x01);
            CpuStepResult result = Cpu.Step();
            AssertMachine(FindLoad(0xB1), 0x01, SentinelX, (byte)y, FlagsFor(0x01), result, expectedCycles);
            return;
        }

        Write(effective, 0xFF);
        CpuStepResult store = Cpu.Step();
        Assert.Multiple(() =>
        {
            AssertMachine(FindStore(0x91), 0x5A, SentinelX, (byte)y, PreservedWithZeroAndNegative, store, expectedCycles);
            Assert.That(Read(effective), Is.EqualTo(0x5A), "STA (zp),Y data");
        });
    }

    /// <summary>
    /// STA abs,X, STA abs,Y, and STZ abs,X stay on their write-cycle count
    /// whether or not the index crosses a page.
    /// </summary>
    /// <param name="opcode">The indexed store opcode.</param>
    /// <param name="baseAddress">The absolute operand before indexing.</param>
    /// <param name="index">X for abs,X and Y for abs,Y.</param>
    /// <param name="expectedCycles">The datasheet count, including the fixed write penalty.</param>
    [TestCase(0x9D, 0x4000, 0x10, 5)]
    [TestCase(0x9D, 0x40FF, 0x01, 5)]
    [TestCase(0x99, 0x4100, 0x06, 5)]
    [TestCase(0x99, 0x41FF, 0x01, 5)]
    [TestCase(0x9E, 0x4200, 0x05, 5)]
    [TestCase(0x9E, 0x42FF, 0x01, 5)]
    public void IndexedStore_AlwaysChargesWritePenalty(int opcode, int baseAddress, int index, int expectedCycles)
    {
        StoreOpcode spec = FindStore((byte)opcode);
        bool usesY = opcode == 0x99;
        byte x = usesY ? SentinelX : (byte)index;
        byte y = usesY ? (byte)index : SentinelY;
        ushort effective = (ushort)((baseAddress + index) & 0xFFFF);
        Write(effective, 0xFF);
        Write(StartPc, spec.Opcode);
        WriteWord(StartPc + 1, (ushort)baseAddress);
        Prepare(0x42, x, y, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        byte stored = spec.Source == StoreSource.Zero ? (byte)0 : (byte)0x42;
        Assert.Multiple(() =>
        {
            AssertMachine(spec, 0x42, x, y, PreservedWithZeroAndNegative, result, expectedCycles);
            Assert.That(Read(effective), Is.EqualTo(stored), spec.Name);
        });
    }

    /// <summary>
    /// 65C02 <c>LDA (zp)</c> and <c>STA (zp)</c> read the pointer with zero-page
    /// wrap. A pointer at <c>$FF</c> takes its high byte from <c>$00</c>, not <c>$0100</c>.
    /// </summary>
    /// <param name="opcode"><c>$B2</c> loads. <c>$92</c> stores.</param>
    [TestCase(0xB2)]
    [TestCase(0x92)]
    public void ZeroPageIndirect_PointerAtFf_WrapsInsideZeroPage(int opcode)
    {
        Write(0x00FF, 0x34);
        Write(0x0000, 0x12);
        Write(0x0100, 0x99);
        Write(0x1234, opcode == 0xB2 ? (byte)0xAB : (byte)0x00);
        Write(0x9934, 0x77);
        Write(StartPc, (byte)opcode);
        Write(StartPc + 1, 0xFF);
        Prepare(0x42, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        if (opcode == 0xB2)
        {
            AssertMachine(FindLoad(0xB2), 0xAB, SentinelX, SentinelY, FlagsFor(0xAB), result);
            Assert.That(Read(0x9934), Is.EqualTo(0x77), "wrong-page byte");
            return;
        }

        Assert.Multiple(() =>
        {
            AssertMachine(FindStore(0x92), 0x42, SentinelX, SentinelY, PreservedWithZeroAndNegative, result);
            Assert.That(Read(0x1234), Is.EqualTo(0x42), "wrapped STA (zp)");
            Assert.That(Read(0x9934), Is.EqualTo(0x77), "unwrapped page");
        });
    }

    /// <summary>
    /// <c>LDA (zp,X)</c> wraps the pointer fetch inside zero page when <c>zp+X</c> is <c>$FF</c>.
    /// </summary>
    [Test]
    public void IndirectX_PointerAtFf_WrapsInsideZeroPage()
    {
        Write(0x00FF, 0x34);
        Write(0x0000, 0x12);
        Write(0x0100, 0x99);
        Write(0x1234, 0x7F);
        Write(0x9934, 0x01);
        Write(StartPc, 0xA1);
        Write(StartPc + 1, 0x80);
        Prepare(SentinelA, 0x7F, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        AssertMachine(FindLoad(0xA1), 0x7F, 0x7F, SentinelY, FlagsFor(0x7F), result);
        Assert.That(Read(0x9934), Is.EqualTo(0x01), "wrong-page byte");
    }

    /// <summary>
    /// An unmapped data read returns the floating-bus value <c>$FF</c>, sets N,
    /// and stops the CPU. The addressing operand itself is still fetched from RAM.
    /// </summary>
    [Test]
    public void Load_UnmappedData_ReturnsFloatingBusAndStops()
    {
        UnmapPage(0x3000);
        Write(StartPc, 0xAD);
        WriteWord(StartPc + 1, 0x3000);
        Prepare(SentinelA, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(FindLoad(0xAD), 0xFF, SentinelX, SentinelY, FlagsFor(0xFF), result);
            Assert.That(result.State, Is.EqualTo(CpuRunState.Stopped), "run state");
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.Stp), "halt");
        });
    }

    /// <summary>
    /// An unmapped store is dropped. Flags, registers, and the running state stay put.
    /// </summary>
    /// <param name="opcode">STA absolute or STZ absolute.</param>
    [TestCase(0x8D)]
    [TestCase(0x9C)]
    public void Store_UnmappedData_DropsTheWriteAndKeepsRunning(int opcode)
    {
        UnmapPage(0x3000);
        Write(StartPc, (byte)opcode);
        WriteWord(StartPc + 1, 0x3000);
        Prepare(0x42, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(FindStore((byte)opcode), 0x42, SentinelX, SentinelY, PreservedWithZeroAndNegative, result);
            Assert.That(result.State, Is.EqualTo(CpuRunState.Running), "run state");
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None), "halt");
            Assert.That(TryRead(0x3000).Failed, Is.True, "page stays unmapped");
        });
    }

    /// <summary>
    /// A permitted side-effect target supplies the loaded byte. A read-denied
    /// page faults before the target runs, so the counter stays at zero and the
    /// CPU does not stop: permission denial is not an unmapped fault.
    /// </summary>
    /// <param name="allowRead">Whether the data page grants read permission.</param>
    /// <param name="expectedA">The accumulator after LDA absolute.</param>
    /// <param name="expectedReads">How many times the target's read method runs.</param>
    [TestCase(true, 0x3C, 1)]
    [TestCase(false, 0xFF, 0)]
    public void Load_SideEffectTarget_ObservesPermissionBeforeTheDevice(bool allowRead, int expectedA, int expectedReads)
    {
        var device = new SideEffectTarget(0x3C);
        MapDevice(0x2000, device, allowRead ? PagePerms.All : PagePerms.None);
        Write(StartPc, 0xAD);
        WriteWord(StartPc + 1, 0x2000);
        Prepare(SentinelA, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(FindLoad(0xAD), (byte)expectedA, SentinelX, SentinelY, FlagsFor((byte)expectedA), result);
            Assert.That(device.Reads, Is.EqualTo(expectedReads), "data reads");
            Assert.That(device.Writes, Is.EqualTo(0), "data writes");
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None), "halt");
        });
    }

    /// <summary>
    /// STA delivers A to a side-effect target. A write-denied page drops the
    /// store before the target sees it.
    /// </summary>
    /// <param name="allowWrite">Whether the data page grants write permission.</param>
    /// <param name="expectedWrites">How many times the target's write method runs.</param>
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void Store_SideEffectTarget_ObservesPermissionBeforeTheDevice(bool allowWrite, int expectedWrites)
    {
        var device = new SideEffectTarget(0x00);
        MapDevice(0x2000, device, allowWrite ? PagePerms.All : PagePerms.None);
        Write(StartPc, 0x8D);
        WriteWord(StartPc + 1, 0x2000);
        Prepare(0x5A, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(FindStore(0x8D), 0x5A, SentinelX, SentinelY, PreservedWithZeroAndNegative, result);
            Assert.That(device.Writes, Is.EqualTo(expectedWrites), "data writes");
            Assert.That(device.LastWrite, Is.EqualTo(allowWrite ? 0x5A : -1), "written byte");
            Assert.That(Cpu.HaltReason, Is.EqualTo(HaltState.None), "halt");
        });
    }

    /// <summary>
    /// 65C02 <c>LDA (zp)</c> still performs the data read on a side-effect target
    /// after the pointer has been fetched from RAM.
    /// </summary>
    [Test]
    public void ZeroPageIndirect_SideEffectTarget_ReadsTheEffectiveAddress()
    {
        var device = new SideEffectTarget(0x80);
        MapDevice(0x2000, device, PagePerms.All);
        Write(0x0040, 0x00);
        Write(0x0041, 0x20);
        Write(StartPc, 0xB2);
        Write(StartPc + 1, 0x40);
        Prepare(SentinelA, SentinelX, SentinelY, PreservedWithZeroAndNegative);

        CpuStepResult result = Cpu.Step();

        Assert.Multiple(() =>
        {
            AssertMachine(FindLoad(0xB2), 0x80, SentinelX, SentinelY, FlagsFor(0x80), result);
            Assert.That(device.Reads, Is.EqualTo(1), "data reads");
        });
    }

    private static IEnumerable<TestCaseData> LoadValueCases()
    {
        byte[] values = [0x00, 0x01, 0x80];
        foreach (LoadOpcode spec in Loads)
        {
            foreach (byte value in values)
            {
                yield return new TestCaseData(spec.Opcode, value).SetArgDisplayNames(spec.Name, $"${value:X2}");
            }
        }
    }

    private static IEnumerable<TestCaseData> StoreValueCases()
    {
        foreach (StoreOpcode spec in Stores)
        {
            if (spec.Source == StoreSource.Zero)
            {
                yield return new TestCaseData(spec.Opcode, 0xFF).SetArgDisplayNames(spec.Name, "zero");
                continue;
            }

            yield return new TestCaseData(spec.Opcode, 0x00).SetArgDisplayNames(spec.Name, "$00");
            yield return new TestCaseData(spec.Opcode, 0x80).SetArgDisplayNames(spec.Name, "$80");
        }
    }

    private static LoadOpcode FindLoad(byte opcode)
    {
        foreach (LoadOpcode spec in Loads)
        {
            if (spec.Opcode == opcode)
            {
                return spec;
            }
        }

        throw new InvalidOperationException($"${opcode:X2} is not a load opcode under test.");
    }

    private static StoreOpcode FindStore(byte opcode)
    {
        foreach (StoreOpcode spec in Stores)
        {
            if (spec.Opcode == opcode)
            {
                return spec;
            }
        }

        throw new InvalidOperationException($"${opcode:X2} is not a store opcode under test.");
    }

    private static byte FlagsFor(byte value)
    {
        byte flags = Preserved;
        if (value == 0)
        {
            flags |= 0x02;
        }

        if ((value & 0x80) != 0)
        {
            flags |= 0x80;
        }

        return flags;
    }

    private void PlantLoad(byte opcode, byte value)
    {
        Write(StartPc, opcode);
        switch (opcode)
        {
            case 0xA9:
            case 0xA2:
            case 0xA0:
                Write(StartPc + 1, value);
                break;
            case 0xA5:
            case 0xA6:
            case 0xA4:
                Write(StartPc + 1, 0x40);
                Write(0x0040, value);
                break;
            case 0xB5:
            case 0xB4:
                Write(StartPc + 1, 0x40);
                Write(0x0045, value);
                break;
            case 0xB6:
                Write(StartPc + 1, 0x40);
                Write(0x0046, value);
                break;
            case 0xAD:
                WriteWord(StartPc + 1, 0x2000);
                Write(0x2000, value);
                break;
            case 0xBD:
            case 0xBC:
                WriteWord(StartPc + 1, 0x2100);
                Write(0x2105, value);
                break;
            case 0xB9:
            case 0xBE:
                WriteWord(StartPc + 1, 0x2200);
                Write(0x2206, value);
                break;
            case 0xAE:
                WriteWord(StartPc + 1, 0x2300);
                Write(0x2300, value);
                break;
            case 0xAC:
                WriteWord(StartPc + 1, 0x2400);
                Write(0x2400, value);
                break;
            case 0xA1:
                Write(StartPc + 1, 0x20);
                Write(0x0025, 0x00);
                Write(0x0026, 0x25);
                Write(0x2500, value);
                break;
            case 0xB1:
                Write(StartPc + 1, 0x30);
                Write(0x0030, 0x00);
                Write(0x0031, 0x26);
                Write(0x2606, value);
                break;
            case 0xB2:
                Write(StartPc + 1, 0x50);
                Write(0x0050, 0x00);
                Write(0x0051, 0x27);
                Write(0x2700, value);
                break;
            default:
                throw new InvalidOperationException($"${opcode:X2} has no load layout.");
        }
    }

    private ushort PlantStore(byte opcode, byte previous)
    {
        Write(StartPc, opcode);
        ushort destination;
        switch (opcode)
        {
            case 0x85:
                destination = 0x0060;
                Write(StartPc + 1, 0x60);
                break;
            case 0x95:
                destination = 0x0065;
                Write(StartPc + 1, 0x60);
                break;
            case 0x86:
                destination = 0x0061;
                Write(StartPc + 1, 0x61);
                break;
            case 0x96:
                destination = 0x0068;
                Write(StartPc + 1, 0x62);
                break;
            case 0x84:
                destination = 0x0063;
                Write(StartPc + 1, 0x63);
                break;
            case 0x94:
                destination = 0x0069;
                Write(StartPc + 1, 0x64);
                break;
            case 0x64:
                destination = 0x006A;
                Write(StartPc + 1, 0x6A);
                break;
            case 0x74:
                destination = 0x0070;
                Write(StartPc + 1, 0x6B);
                break;
            case 0x8D:
                destination = 0x3000;
                WriteWord(StartPc + 1, destination);
                break;
            case 0x9D:
                destination = 0x3105;
                WriteWord(StartPc + 1, 0x3100);
                break;
            case 0x99:
                destination = 0x3206;
                WriteWord(StartPc + 1, 0x3200);
                break;
            case 0x8E:
                destination = 0x3300;
                WriteWord(StartPc + 1, destination);
                break;
            case 0x8C:
                destination = 0x3400;
                WriteWord(StartPc + 1, destination);
                break;
            case 0x9C:
                destination = 0x3500;
                WriteWord(StartPc + 1, destination);
                break;
            case 0x9E:
                destination = 0x3605;
                WriteWord(StartPc + 1, 0x3600);
                break;
            case 0x81:
                destination = 0x3700;
                Write(StartPc + 1, 0x70);
                Write(0x0075, 0x00);
                Write(0x0076, 0x37);
                break;
            case 0x91:
                destination = 0x3806;
                Write(StartPc + 1, 0x72);
                Write(0x0072, 0x00);
                Write(0x0073, 0x38);
                break;
            case 0x92:
                destination = 0x3900;
                Write(StartPc + 1, 0x74);
                Write(0x0074, 0x00);
                Write(0x0075, 0x39);
                break;
            default:
                throw new InvalidOperationException($"${opcode:X2} has no store layout.");
        }

        Write(destination, previous);
        return destination;
    }

    private void Prepare(byte a, byte x, byte y, byte flags)
    {
        Cpu.Registers.Reset(true);
        Cpu.Registers.A.SetByte(a);
        Cpu.Registers.X.SetByte(x);
        Cpu.Registers.Y.SetByte(y);
        Cpu.Registers.SP.SetDWord(SentinelSp);
        Cpu.Registers.P = (ProcessorStatusFlags)flags;
        Cpu.Registers.PC.SetWord(StartPc);
        Cpu.HaltReason = HaltState.None;
        Cpu.SetCycles(StartCycles);
    }

    private void AssertMachine(LoadOpcode spec, byte a, byte x, byte y, byte flags, CpuStepResult result, int cycles = -1)
    {
        AssertDirect(spec.Opcode, spec.Name, spec.Instruction, spec.Mode, spec.Length, cycles < 0 ? spec.Cycles : cycles, a, x, y, flags, result);
    }

    private void AssertMachine(StoreOpcode spec, byte a, byte x, byte y, byte flags, CpuStepResult result, int cycles = -1)
    {
        AssertDirect(spec.Opcode, spec.Name, spec.Instruction, spec.Mode, spec.Length, cycles < 0 ? spec.Cycles : cycles, a, x, y, flags, result);
    }

    private void AssertDirect(
        byte opcode,
        string name,
        CpuInstructions instruction,
        CpuAddressingModes mode,
        int length,
        int cycles,
        byte a,
        byte x,
        byte y,
        byte flags,
        CpuStepResult result)
    {
        OpcodeInfo info = Table.GetMetadata(opcode);
        Assert.Multiple(() =>
        {
            Assert.That(Table.GetHandler(opcode), Is.Null, $"{name} legacy handler");
            Assert.That(info.Instruction, Is.EqualTo(instruction), $"{name} mnemonic");
            Assert.That(info.AddressingMode, Is.EqualTo(mode), $"{name} mode");
            Assert.That(info.TotalLength, Is.EqualTo(length), $"{name} length");
            Assert.That(Cpu.Registers.A.GetDWord(), Is.EqualTo((DWord)a), $"{name} A");
            Assert.That(Cpu.Registers.X.GetDWord(), Is.EqualTo((DWord)x), $"{name} X");
            Assert.That(Cpu.Registers.Y.GetDWord(), Is.EqualTo((DWord)y), $"{name} Y");
            Assert.That(Cpu.Registers.SP.GetDWord(), Is.EqualTo((DWord)SentinelSp), $"{name} SP");
            Assert.That(Cpu.Registers.P, Is.EqualTo((ProcessorStatusFlags)flags), $"{name} P");
            Assert.That(Cpu.Registers.PC.GetWord(), Is.EqualTo((Word)(StartPc + length)), $"{name} PC");
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo((ulong)cycles), $"{name} cycles");
            Assert.That(Cpu.GetCycles(), Is.EqualTo(StartCycles + (ulong)cycles), $"{name} scheduler");
        });
    }

    private void UnmapPage(uint address)
    {
        Bus.MapPage((int)(address >> 12), default);
    }

    private void MapDevice(uint address, IBusTarget target, PagePerms perms)
    {
        Bus.MapPage(
            (int)(address >> 12),
            new PageEntry(1, RegionTag.Ram, perms, target.Capabilities, target, 0));
    }

    private BusResult<byte> TryRead(uint address)
    {
        var access = new BusAccess(
            Address: address,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
        return Bus.TryRead8(access);
    }

    private readonly record struct LoadOpcode(
        byte Opcode,
        string Name,
        CpuInstructions Instruction,
        CpuAddressingModes Mode,
        int Length,
        int Cycles,
        LoadTarget Target);

    private readonly record struct StoreOpcode(
        byte Opcode,
        string Name,
        CpuInstructions Instruction,
        CpuAddressingModes Mode,
        int Length,
        int Cycles,
        StoreSource Source);

    private sealed class SideEffectTarget(byte readValue) : IBusTarget
    {
        public TargetCaps Capabilities => TargetCaps.HasSideEffects;

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public int LastWrite { get; private set; } = -1;

        public byte Read8(Addr offset, in BusAccess access)
        {
            Reads++;
            return readValue;
        }

        public void Write8(Addr offset, byte value, in BusAccess access)
        {
            Writes++;
            LastWrite = value;
        }
    }
}