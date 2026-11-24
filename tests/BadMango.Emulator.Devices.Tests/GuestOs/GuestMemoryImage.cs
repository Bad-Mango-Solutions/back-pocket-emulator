// <copyright file="GuestMemoryImage.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests.GuestOs;

using BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// A 64K guest-memory image built in the test. It does not read a disk or a ROM.
/// </summary>
internal sealed class GuestMemoryImage : IGuestMemoryReader
{
    private readonly byte[] image = new byte[65536];

    /// <inheritdoc />
    public byte Read(Addr address)
    {
        if (address >= (uint)this.image.Length)
        {
            return 0xFF;
        }

        return this.image[address];
    }

    /// <summary>Writes one byte into the image.</summary>
    /// <param name="address">The guest address.</param>
    /// <param name="value">The byte to store.</param>
    public void Poke(ushort address, byte value) => this.image[address] = value;

    /// <summary>Clears the image to zeros.</summary>
    public void Clear() => Array.Clear(this.image);

    /// <summary>Plants a ProDOS 8 global page.</summary>
    /// <param name="kVersion"><c>KVERSION</c> at <c>$BFFF</c>.</param>
    /// <param name="machId"><c>MACHID</c> at <c>$BF98</c>.</param>
    /// <param name="iVersion"><c>IVERSION</c> at <c>$BFFD</c>.</param>
    /// <param name="dateTimeOpcode">The byte at <c>$BF06</c>.</param>
    /// <param name="driver">The <c>DATETIME</c> vector.</param>
    public void PlantProDos(
        byte kVersion,
        byte machId = 0,
        byte iVersion = 0,
        byte dateTimeOpcode = 0x60,
        ushort driver = 0xD000)
    {
        this.Poke(0xBF00, 0x4C);
        this.Poke(0xBF06, dateTimeOpcode);
        this.Poke(0xBF07, (byte)driver);
        this.Poke(0xBF08, (byte)(driver >> 8));
        this.Poke(0xBF98, machId);
        this.Poke(0xBFFD, iVersion);
        this.Poke(0xBFFF, kVersion);
    }

    /// <summary>Plants one <c>JMP</c>.</summary>
    /// <param name="address">The opcode address.</param>
    /// <param name="target">The little-endian target.</param>
    public void PlantJmp(ushort address, ushort target)
    {
        this.Poke(address, 0x4C);
        this.Poke((ushort)(address + 1), (byte)target);
        this.Poke((ushort)(address + 2), (byte)(target >> 8));
    }

    /// <summary>Plants the five DOS 3.3 page-3 vectors.</summary>
    /// <param name="standard"><see langword="true"/> for the 48K targets.</param>
    public void PlantDos33(bool standard = true)
    {
        this.PlantJmp(0x03D0, standard ? (ushort)0x9DBF : (ushort)0x1111);
        this.PlantJmp(0x03D3, standard ? (ushort)0x9D84 : (ushort)0x2222);
        this.PlantJmp(0x03D6, standard ? (ushort)0xAAFD : (ushort)0x3333);
        this.PlantJmp(0x03D9, standard ? (ushort)0xB7B5 : (ushort)0x4444);
        this.PlantJmp(0x03EA, standard ? (ushort)0xA851 : (ushort)0x5555);
    }
}