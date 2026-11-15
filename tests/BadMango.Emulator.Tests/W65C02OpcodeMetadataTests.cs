// <copyright file="W65C02OpcodeMetadataTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests;

using Core.Cpu;

using Emulation.Cpu;

using TestHelpers;

/// <summary>
/// Locks the 65C02 opcode table to an explicit metadata record and to the
/// cycles each handler charges when no penalty applies.
/// </summary>
[TestFixture]
public class W65C02OpcodeMetadataTests : CpuTestBase
{
    /// <summary>
    /// Frozen W65C02S metadata for all 256 opcodes. Each line is
    /// <c>opcode mnemonic addressing-mode instruction-length base-cycles</c>.
    /// Instruction length includes the opcode byte. Base cycles omit page-cross,
    /// branch-taken and decimal-mode penalties. BRA is always taken, so its
    /// minimum (no page cross) is 3.
    /// </summary>
    private const string ExpectedMetadata = """
        00 BRK Implied 1 7
        01 ORA IndirectX 2 6
        02 NOP Immediate 2 2
        03 NOP Implied 1 1
        04 TSB ZeroPage 2 5
        05 ORA ZeroPage 2 3
        06 ASL ZeroPage 2 5
        07 RMB ZeroPage 2 5
        08 PHP Implied 1 3
        09 ORA Immediate 2 2
        0A ASL Accumulator 1 2
        0B NOP Implied 1 1
        0C TSB Absolute 3 6
        0D ORA Absolute 3 4
        0E ASL Absolute 3 6
        0F BBR ZeroPageRelative 3 5
        10 BPL Relative 2 2
        11 ORA IndirectY 2 5
        12 ORA ZeroPageIndirect 2 5
        13 NOP Implied 1 1
        14 TRB ZeroPage 2 5
        15 ORA ZeroPageX 2 4
        16 ASL ZeroPageX 2 6
        17 RMB ZeroPage 2 5
        18 CLC Implied 1 2
        19 ORA AbsoluteY 3 4
        1A INA Accumulator 1 2
        1B NOP Implied 1 1
        1C TRB Absolute 3 6
        1D ORA AbsoluteX 3 4
        1E ASL AbsoluteX 3 6
        1F BBR ZeroPageRelative 3 5
        20 JSR Absolute 3 6
        21 AND IndirectX 2 6
        22 NOP Immediate 2 2
        23 NOP Implied 1 1
        24 BIT ZeroPage 2 3
        25 AND ZeroPage 2 3
        26 ROL ZeroPage 2 5
        27 RMB ZeroPage 2 5
        28 PLP Implied 1 4
        29 AND Immediate 2 2
        2A ROL Accumulator 1 2
        2B NOP Implied 1 1
        2C BIT Absolute 3 4
        2D AND Absolute 3 4
        2E ROL Absolute 3 6
        2F BBR ZeroPageRelative 3 5
        30 BMI Relative 2 2
        31 AND IndirectY 2 5
        32 AND ZeroPageIndirect 2 5
        33 NOP Implied 1 1
        34 BIT ZeroPageX 2 4
        35 AND ZeroPageX 2 4
        36 ROL ZeroPageX 2 6
        37 RMB ZeroPage 2 5
        38 SEC Implied 1 2
        39 AND AbsoluteY 3 4
        3A DEA Accumulator 1 2
        3B NOP Implied 1 1
        3C BIT AbsoluteX 3 4
        3D AND AbsoluteX 3 4
        3E ROL AbsoluteX 3 6
        3F BBR ZeroPageRelative 3 5
        40 RTI Implied 1 6
        41 EOR IndirectX 2 6
        42 NOP Immediate 2 2
        43 NOP Implied 1 1
        44 NOP ZeroPage 2 3
        45 EOR ZeroPage 2 3
        46 LSR ZeroPage 2 5
        47 RMB ZeroPage 2 5
        48 PHA Implied 1 3
        49 EOR Immediate 2 2
        4A LSR Accumulator 1 2
        4B NOP Implied 1 1
        4C JMP Absolute 3 3
        4D EOR Absolute 3 4
        4E LSR Absolute 3 6
        4F BBR ZeroPageRelative 3 5
        50 BVC Relative 2 2
        51 EOR IndirectY 2 5
        52 EOR ZeroPageIndirect 2 5
        53 NOP Implied 1 1
        54 NOP ZeroPageX 2 4
        55 EOR ZeroPageX 2 4
        56 LSR ZeroPageX 2 6
        57 RMB ZeroPage 2 5
        58 CLI Implied 1 2
        59 EOR AbsoluteY 3 4
        5A PHY Implied 1 3
        5B NOP Implied 1 1
        5C NOP Absolute 3 8
        5D EOR AbsoluteX 3 4
        5E LSR AbsoluteX 3 6
        5F BBR ZeroPageRelative 3 5
        60 RTS Implied 1 6
        61 ADC IndirectX 2 6
        62 NOP Immediate 2 2
        63 NOP Implied 1 1
        64 STZ ZeroPage 2 3
        65 ADC ZeroPage 2 3
        66 ROR ZeroPage 2 5
        67 RMB ZeroPage 2 5
        68 PLA Implied 1 4
        69 ADC Immediate 2 2
        6A ROR Accumulator 1 2
        6B NOP Implied 1 1
        6C JMP Indirect 3 6
        6D ADC Absolute 3 4
        6E ROR Absolute 3 6
        6F BBR ZeroPageRelative 3 5
        70 BVS Relative 2 2
        71 ADC IndirectY 2 5
        72 ADC ZeroPageIndirect 2 5
        73 NOP Implied 1 1
        74 STZ ZeroPageX 2 4
        75 ADC ZeroPageX 2 4
        76 ROR ZeroPageX 2 6
        77 RMB ZeroPage 2 5
        78 SEI Implied 1 2
        79 ADC AbsoluteY 3 4
        7A PLY Implied 1 4
        7B NOP Implied 1 1
        7C JMP AbsoluteIndirectX 3 6
        7D ADC AbsoluteX 3 4
        7E ROR AbsoluteX 3 6
        7F BBR ZeroPageRelative 3 5
        80 BRA Relative 2 3
        81 STA IndirectX 2 6
        82 NOP Immediate 2 2
        83 NOP Implied 1 1
        84 STY ZeroPage 2 3
        85 STA ZeroPage 2 3
        86 STX ZeroPage 2 3
        87 SMB ZeroPage 2 5
        88 DEY Implied 1 2
        89 BIT Immediate 2 2
        8A TXA Implied 1 2
        8B NOP Implied 1 1
        8C STY Absolute 3 4
        8D STA Absolute 3 4
        8E STX Absolute 3 4
        8F BBS ZeroPageRelative 3 5
        90 BCC Relative 2 2
        91 STA IndirectY 2 6
        92 STA ZeroPageIndirect 2 5
        93 NOP Implied 1 1
        94 STY ZeroPageX 2 4
        95 STA ZeroPageX 2 4
        96 STX ZeroPageY 2 4
        97 SMB ZeroPage 2 5
        98 TYA Implied 1 2
        99 STA AbsoluteY 3 5
        9A TXS Implied 1 2
        9B NOP Implied 1 1
        9C STZ Absolute 3 4
        9D STA AbsoluteX 3 5
        9E STZ AbsoluteX 3 5
        9F BBS ZeroPageRelative 3 5
        A0 LDY Immediate 2 2
        A1 LDA IndirectX 2 6
        A2 LDX Immediate 2 2
        A3 NOP Implied 1 1
        A4 LDY ZeroPage 2 3
        A5 LDA ZeroPage 2 3
        A6 LDX ZeroPage 2 3
        A7 SMB ZeroPage 2 5
        A8 TAY Implied 1 2
        A9 LDA Immediate 2 2
        AA TAX Implied 1 2
        AB NOP Implied 1 1
        AC LDY Absolute 3 4
        AD LDA Absolute 3 4
        AE LDX Absolute 3 4
        AF BBS ZeroPageRelative 3 5
        B0 BCS Relative 2 2
        B1 LDA IndirectY 2 5
        B2 LDA ZeroPageIndirect 2 5
        B3 NOP Implied 1 1
        B4 LDY ZeroPageX 2 4
        B5 LDA ZeroPageX 2 4
        B6 LDX ZeroPageY 2 4
        B7 SMB ZeroPage 2 5
        B8 CLV Implied 1 2
        B9 LDA AbsoluteY 3 4
        BA TSX Implied 1 2
        BB NOP Implied 1 1
        BC LDY AbsoluteX 3 4
        BD LDA AbsoluteX 3 4
        BE LDX AbsoluteY 3 4
        BF BBS ZeroPageRelative 3 5
        C0 CPY Immediate 2 2
        C1 CMP IndirectX 2 6
        C2 NOP Immediate 2 2
        C3 NOP Implied 1 1
        C4 CPY ZeroPage 2 3
        C5 CMP ZeroPage 2 3
        C6 DEC ZeroPage 2 5
        C7 SMB ZeroPage 2 5
        C8 INY Implied 1 2
        C9 CMP Immediate 2 2
        CA DEX Implied 1 2
        CB WAI Implied 1 3
        CC CPY Absolute 3 4
        CD CMP Absolute 3 4
        CE DEC Absolute 3 6
        CF BBS ZeroPageRelative 3 5
        D0 BNE Relative 2 2
        D1 CMP IndirectY 2 5
        D2 CMP ZeroPageIndirect 2 5
        D3 NOP Implied 1 1
        D4 NOP ZeroPageX 2 4
        D5 CMP ZeroPageX 2 4
        D6 DEC ZeroPageX 2 6
        D7 SMB ZeroPage 2 5
        D8 CLD Implied 1 2
        D9 CMP AbsoluteY 3 4
        DA PHX Implied 1 3
        DB STP Implied 1 3
        DC NOP Absolute 3 4
        DD CMP AbsoluteX 3 4
        DE DEC AbsoluteX 3 7
        DF BBS ZeroPageRelative 3 5
        E0 CPX Immediate 2 2
        E1 SBC IndirectX 2 6
        E2 NOP Immediate 2 2
        E3 NOP Implied 1 1
        E4 CPX ZeroPage 2 3
        E5 SBC ZeroPage 2 3
        E6 INC ZeroPage 2 5
        E7 SMB ZeroPage 2 5
        E8 INX Implied 1 2
        E9 SBC Immediate 2 2
        EA NOP Implied 1 2
        EB NOP Implied 1 1
        EC CPX Absolute 3 4
        ED SBC Absolute 3 4
        EE INC Absolute 3 6
        EF BBS ZeroPageRelative 3 5
        F0 BEQ Relative 2 2
        F1 SBC IndirectY 2 5
        F2 SBC ZeroPageIndirect 2 5
        F3 NOP Implied 1 1
        F4 NOP ZeroPageX 2 4
        F5 SBC ZeroPageX 2 4
        F6 INC ZeroPageX 2 6
        F7 SMB ZeroPage 2 5
        F8 SED Implied 1 2
        F9 SBC AbsoluteY 3 4
        FA PLX Implied 1 4
        FB NOP Implied 1 1
        FC NOP Absolute 3 4
        FD SBC AbsoluteX 3 4
        FE INC AbsoluteX 3 7
        FF BBS ZeroPageRelative 3 5
        """;

    private const byte AslAbsoluteXOpcode = 0x1E;
    private const byte RolAbsoluteXOpcode = 0x3E;
    private const byte LsrAbsoluteXOpcode = 0x5E;
    private const byte RorAbsoluteXOpcode = 0x7E;
    private const byte StzAbsoluteXOpcode = 0x9E;
    private const byte DecAbsoluteXOpcode = 0xDE;
    private const byte IncAbsoluteXOpcode = 0xFE;

    /// <summary>
    /// Verifies that every opcode's explicit metadata matches the frozen table.
    /// </summary>
    [Test]
    public void OpcodeTable_Metadata_MatchesFrozenTable()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        var expected = ParseExpected();

        Assert.That(expected, Has.Count.EqualTo(256));
        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                var info = table.GetMetadata((byte)opcode);
                var row = expected[opcode];
                Assert.That(info.Instruction.ToString(), Is.EqualTo(row.Mnemonic), $"${opcode:X2} mnemonic");
                Assert.That(info.AddressingMode.ToString(), Is.EqualTo(row.AddressingMode), $"${opcode:X2} addressing mode");
                Assert.That(info.TotalLength, Is.EqualTo(row.Length), $"${opcode:X2} instruction length");
                Assert.That(info.BaseCycles, Is.EqualTo(row.BaseCycles), $"${opcode:X2} base cycles");
            }
        });
    }

    /// <summary>
    /// Verifies that executing each opcode, with no page-cross, branch-taken or
    /// decimal-mode penalty, consumes exactly <see cref="OpcodeInfo.BaseCycles"/>.
    /// </summary>
    [Test]
    public void OpcodeTable_BaseCycles_MatchHandlerCostWithoutPenalties()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();

        Assert.Multiple(() =>
        {
            for (int opcode = 0; opcode < 256; opcode++)
            {
                byte code = (byte)opcode;
                ulong consumed = MeasureBaseCycles(code);
                byte expected = table.GetMetadata(code).BaseCycles;
                Assert.That(consumed, Is.EqualTo(expected), $"${opcode:X2} consumed {consumed}, BaseCycles {expected}");
            }
        });
    }

    /// <summary>
    /// Verifies that <c>STZ abs,X</c> (<c>$9E</c>) charges 5 cycles.
    /// </summary>
    [Test]
    public void StzAbsoluteX_HandlerChargesDatasheetBaseCycles()
    {
        var table = Cpu65C02OpcodeTableBuilder.Build();
        ulong consumed = MeasureBaseCycles(StzAbsoluteXOpcode);

        Assert.Multiple(() =>
        {
            Assert.That(table.GetMetadata(StzAbsoluteXOpcode).BaseCycles, Is.EqualTo(5));
            Assert.That(consumed, Is.EqualTo(5), $"STZ abs,X consumed {consumed}; datasheet base is 5");
        });
    }

    /// <summary>
    /// Verifies that <c>STZ abs,X</c> always consumes 5 cycles, including page crossing.
    /// </summary>
    /// <param name="baseAddress">The instruction operand before indexing.</param>
    /// <param name="x">The X register value added to <paramref name="baseAddress"/>.</param>
    [TestCase((ushort)0x2000, (byte)0x10)]
    [TestCase((ushort)0x20FF, (byte)0x01)]
    public void StzAbsoluteX_AlwaysConsumesFiveCycles(ushort baseAddress, byte x)
    {
        ushort effectiveAddress = (ushort)(baseAddress + x);
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, StzAbsoluteXOpcode);
        WriteWord(0x1001, baseAddress);
        Write(effectiveAddress, 0xAA);
        Cpu.Reset();
        Cpu.Registers.X.SetByte(x);

        var result = Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(5UL));
            Assert.That(Read(effectiveAddress), Is.EqualTo(0x00));
        });
    }

    /// <summary>
    /// Verifies that <c>INC abs,X</c> and <c>DEC abs,X</c> consume 7 cycles both with and without page crossing.
    /// </summary>
    /// <param name="opcode">The opcode under test.</param>
    /// <param name="baseAddress">The instruction operand before indexing.</param>
    /// <param name="x">The X register value added to <paramref name="baseAddress"/>.</param>
    /// <param name="initialValue">The value to place at the effective address before execution.</param>
    /// <param name="expectedValue">The expected value after execution.</param>
    /// <param name="expectZero">Whether Z should be set after execution.</param>
    /// <param name="expectNegative">Whether N should be set after execution.</param>
    [TestCase(IncAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, (byte)0xFF, (byte)0x00, true, false)]
    [TestCase(IncAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, (byte)0x7F, (byte)0x80, false, true)]
    [TestCase(DecAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, (byte)0x01, (byte)0x00, true, false)]
    [TestCase(DecAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, (byte)0x00, (byte)0xFF, false, true)]
    public void IncAndDecAbsoluteX_AlwaysConsumeSevenCyclesAndUpdateFlags(
        byte opcode,
        ushort baseAddress,
        byte x,
        byte initialValue,
        byte expectedValue,
        bool expectZero,
        bool expectNegative)
    {
        ushort effectiveAddress = (ushort)(baseAddress + x);
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, opcode);
        WriteWord(0x1001, baseAddress);
        Write(effectiveAddress, initialValue);
        Cpu.Reset();
        Cpu.Registers.P = ProcessorStatusFlags.R;
        Cpu.Registers.X.SetByte(x);

        var result = Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(7UL));
            Assert.That(Read(effectiveAddress), Is.EqualTo(expectedValue));
            Assert.That((Cpu.Registers.P & ProcessorStatusFlags.Z) != 0, Is.EqualTo(expectZero));
            Assert.That((Cpu.Registers.P & ProcessorStatusFlags.N) != 0, Is.EqualTo(expectNegative));
        });
    }

    /// <summary>
    /// Verifies shift/rotate <c>abs,X</c> remain at 6 cycles (+1 on page crossing)
    /// and write the exact result of operating on <c>0x12</c> with carry clear:
    /// ASL and ROL store <c>0x24</c>; LSR and ROR store <c>0x09</c>.
    /// </summary>
    /// <param name="opcode">The shift/rotate opcode under test.</param>
    /// <param name="baseAddress">The instruction operand before indexing.</param>
    /// <param name="x">The X register value added to <paramref name="baseAddress"/>.</param>
    /// <param name="expectedCycles">The expected cycle count for this addressing case.</param>
    /// <param name="expectedValue">The byte stored at the effective address.</param>
    [TestCase(AslAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, 6UL, (byte)0x24)]
    [TestCase(AslAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, 7UL, (byte)0x24)]
    [TestCase(LsrAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, 6UL, (byte)0x09)]
    [TestCase(LsrAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, 7UL, (byte)0x09)]
    [TestCase(RolAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, 6UL, (byte)0x24)]
    [TestCase(RolAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, 7UL, (byte)0x24)]
    [TestCase(RorAbsoluteXOpcode, (ushort)0x2000, (byte)0x10, 6UL, (byte)0x09)]
    [TestCase(RorAbsoluteXOpcode, (ushort)0x20FF, (byte)0x01, 7UL, (byte)0x09)]
    public void ShiftAndRotateAbsoluteX_KeepSixPlusPageCrossTiming(
        byte opcode,
        ushort baseAddress,
        byte x,
        ulong expectedCycles,
        byte expectedValue)
    {
        ushort effectiveAddress = (ushort)(baseAddress + x);
        WriteWord(0xFFFC, 0x1000);
        Write(0x1000, opcode);
        WriteWord(0x1001, baseAddress);
        Write(effectiveAddress, 0x12);
        Cpu.Reset();
        Cpu.Registers.P = ProcessorStatusFlags.R;
        Cpu.Registers.X.SetByte(x);

        var result = Cpu.Step();

        Assert.Multiple(() =>
        {
            Assert.That(result.CyclesConsumed.Value, Is.EqualTo(expectedCycles));
            Assert.That(Read(effectiveAddress), Is.EqualTo(expectedValue));
        });
    }

    /// <summary>
    /// Verifies that the table keeps its own copy of the metadata array.
    /// </summary>
    [Test]
    public void OpcodeTable_CopiesMetadataArray()
    {
        var handlers = new OpcodeHandler[256];
        var metadata = new OpcodeInfo[256];
        metadata[0xEA] = new(CpuInstructions.NOP, CpuAddressingModes.Implied, 0, 2);

        var table = new OpcodeTable(handlers, metadata);
        metadata[0xEA] = default;

        var info = table.GetMetadata(0xEA);
        Assert.Multiple(() =>
        {
            Assert.That(info.Instruction, Is.EqualTo(CpuInstructions.NOP));
            Assert.That(info.BaseCycles, Is.EqualTo((byte)2));
        });
    }

    /// <summary>
    /// Runs one opcode at <c>$0200</c> with X = Y = 0, D clear, and operands that
    /// do not cross a page. Conditional branches and BBR/BBS are not taken.
    /// BRA uses a zero offset, so it is taken inside the same page.
    /// </summary>
    /// <param name="opcode">The opcode to execute.</param>
    /// <returns>The cycles <see cref="Cpu65C02.Step"/> reports.</returns>
    private ulong MeasureBaseCycles(byte opcode)
    {
        Write(0x0200, opcode);
        Write(0x0201, 0x00);
        Write(0x0202, 0x00);

        // BBR branches when the bit is clear. Set every bit so it falls through.
        // Every other opcode, including BBS, wants a zero operand or pointer.
        Write(0x0000, IsBranchOnBitReset(opcode) ? (byte)0xFF : (byte)0x00);
        Write(0x0001, 0x00);

        WriteWord(0xFFFC, 0x0200);
        Cpu.Reset();
        ApplyNotTakenBranchFlags(opcode);

        return Cpu.Step().CyclesConsumed.Value;
    }

    private bool IsBranchOnBitReset(byte opcode)
    {
        return opcode is 0x0F or 0x1F or 0x2F or 0x3F or 0x4F or 0x5F or 0x6F or 0x7F;
    }

    private Dictionary<int, ExpectedOpcode> ParseExpected()
    {
        var parsed = new Dictionary<int, ExpectedOpcode>(256);
        foreach (var rawLine in ExpectedMetadata.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(' ');
            parsed[Convert.ToInt32(parts[0], 16)] = new(parts[1], parts[2], int.Parse(parts[3]), byte.Parse(parts[4]));
        }

        return parsed;
    }

    private void ApplyNotTakenBranchFlags(byte opcode)
    {
        switch (opcode)
        {
            case 0x10: // BPL is taken when N is clear
                Cpu.Registers.P |= ProcessorStatusFlags.N;
                break;
            case 0x50: // BVC is taken when V is clear
                Cpu.Registers.P |= ProcessorStatusFlags.V;
                break;
            case 0x90: // BCC is taken when C is clear
                Cpu.Registers.P |= ProcessorStatusFlags.C;
                break;
            case 0xD0: // BNE is taken when Z is clear
                Cpu.Registers.P |= ProcessorStatusFlags.Z;
                break;
        }
    }

    private readonly record struct ExpectedOpcode(string Mnemonic, string AddressingMode, int Length, byte BaseCycles);
}