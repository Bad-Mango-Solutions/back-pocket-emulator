// <copyright file="SpikeInstructionMix.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Emulation.Cpu.Spike;

/// <summary>
/// A fixed loop that uses every opcode in the spike, including page-cross and
/// non-crossing forms of the indexed instructions. Both cores run this image.
/// </summary>
public static class SpikeInstructionMix
{
    /// <summary>Program counter at the start of each loop.</summary>
    public const ushort Entry = 0x07C8;

    /// <summary>X value required by the indexed operands in the loop.</summary>
    public const byte IndexX = 0x05;

    /// <summary>Y value required by the indirect-indexed operand in the loop.</summary>
    public const byte IndexY = 0x20;

    /// <summary>Stack pointer at loop entry. PHA/PLA and JSR/RTS restore it.</summary>
    public const byte Stack = 0xF0;

    /// <summary>Instructions executed by one trip around the loop, including RTS and the indirect jump.</summary>
    public const int InstructionsPerLoop = 25;

    /// <summary>
    /// Writes the loop into a zeroed 64KB image.
    /// </summary>
    /// <param name="ram">The RAM image. Must be at least 64KB.</param>
    public static void Load(Span<byte> ram)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ram.Length, 0x10000, nameof(ram));

        ushort address = Entry;
        Emit(ram, ref address, 0xA9, 0x00);
        Emit(ram, ref address, 0xF0, 0x00);
        Emit(ram, ref address, 0xA9, 0x01);
        Emit(ram, ref address, 0xF0, 0x00);
        Emit(ram, ref address, 0xA5, 0x10);
        Emit(ram, ref address, 0xB5, 0x20);
        Emit(ram, ref address, 0xAD, 0x00, 0x50);
        Emit(ram, ref address, 0xBD, 0x00, 0x50);
        Emit(ram, ref address, 0xBD, 0xFE, 0x50);
        Emit(ram, ref address, 0x9D, 0x00, 0x60);
        Emit(ram, ref address, 0x9E, 0x00, 0x61);
        Emit(ram, ref address, 0x9E, 0xFE, 0x61);
        Emit(ram, ref address, 0xFE, 0x00, 0x70);
        Emit(ram, ref address, 0xFE, 0xFE, 0x70);
        Emit(ram, ref address, 0x1E, 0x00, 0x80);
        Emit(ram, ref address, 0x1E, 0xFE, 0x80);
        Emit(ram, ref address, 0xB2, 0x30);
        Emit(ram, ref address, 0xB1, 0x50);
        Emit(ram, ref address, 0x48);
        Emit(ram, ref address, 0x68);
        Emit(ram, ref address, 0x20, 0x10, 0x07);
        Emit(ram, ref address, 0xA9, 0x00);
        if (address != 0x07FD)
        {
            throw new InvalidOperationException($"Spike mix layout drifted: page-cross branch is at ${address:X4}, expected $07FD.");
        }

        Emit(ram, ref address, 0xF0, 0x01);
        ram[0x0710] = 0x60;
        ram[0x0800] = 0x6C;
        ram[0x0801] = 0x00;
        ram[0x0802] = 0x06;
        ram[0x0600] = 0xC8;
        ram[0x0601] = 0x07;

        ram[0x0010] = 0x42;
        ram[0x0020] = 0x99;
        ram[0x0025] = 0x77;
        ram[0x0030] = 0x00;
        ram[0x0031] = 0x40;
        ram[0x4000] = 0x11;
        ram[0x0050] = 0xF0;
        ram[0x0051] = 0x42;
        ram[0x4310] = 0x22;
        ram[0x5000] = 0x33;
        ram[0x5005] = 0x44;
        ram[0x5103] = 0x55;
        ram[0x7005] = 0x10;
        ram[0x7103] = 0x20;
        ram[0x8005] = 0x40;
        ram[0x8103] = 0x81;
    }

    private static void Emit(Span<byte> ram, ref ushort address, params ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            ram[address++] = bytes[i];
        }
    }
}