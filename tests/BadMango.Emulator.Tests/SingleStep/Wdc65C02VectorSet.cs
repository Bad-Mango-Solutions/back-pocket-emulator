// <copyright file="Wdc65C02VectorSet.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Tests.SingleStep;

using System.IO.Compression;
using System.Reflection;
using System.Text;

/// <summary>
/// The committed stride sample of the SingleStepTests <c>wdc65c02/v1</c> set.
/// </summary>
internal sealed class Wdc65C02VectorSet
{
    /// <summary>Upstream commit the bytes were cut from.</summary>
    internal const string UpstreamCommit = "2f6980a2d95757486c7bee24355c360e40e2a224";

    /// <summary>Keep one case out of every this many, starting at index 0.</summary>
    internal const int Stride = 50;

    /// <summary>Cases kept for an opcode whose upstream file has 10,000 cases.</summary>
    internal const int CasesPerOpcode = 10_000 / Stride;

    /// <summary>Cases in each non-empty upstream opcode file.</summary>
    internal const int FullCasesPerOpcode = 10_000;

    private const string ResourceName = "wdc65c02-subset.bin.gz";

    private readonly Wdc65C02VectorCase[][] cases;

    private Wdc65C02VectorSet(int sampleStride, Wdc65C02VectorCase[][] cases)
    {
        SampleStride = sampleStride;
        this.cases = cases;
    }

    /// <summary>Gets the stride stored in the embedded file.</summary>
    internal int SampleStride { get; }

    /// <summary>
    /// <c>$CB</c> (WAI) and <c>$DB</c> (STP) have empty files. The upstream README omits them
    /// because a single-instruction test cannot describe those instructions.
    /// </summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <returns><see langword="true"/> when the upstream file is intentionally empty.</returns>
    internal static bool IsOmitted(byte opcode) => opcode is 0xCB or 0xDB;

    /// <summary>Loads the gzipped subset embedded in the test assembly.</summary>
    /// <returns>The 256 opcode slices.</returns>
    internal static Wdc65C02VectorSet LoadEmbedded()
    {
        Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        }

        using (resource)
        {
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.ASCII, leaveOpen: true);
            return Read(reader);
        }
    }

    /// <summary>Gets the cases for one opcode.</summary>
    /// <param name="opcode">The opcode byte.</param>
    /// <returns>The committed cases. Empty for an omitted opcode.</returns>
    internal Wdc65C02VectorCase[] CasesFor(byte opcode) => cases[opcode];

    private static Wdc65C02VectorSet Read(BinaryReader reader)
    {
        byte[] magic = ReadExact(reader, 4);
        if (magic[0] != (byte)'W' || magic[1] != (byte)'6' || magic[2] != (byte)'5' || magic[3] != (byte)'V')
        {
            throw new InvalidDataException("The wdc65c02 subset magic does not match.");
        }

        ushort version = reader.ReadUInt16();
        if (version != 1)
        {
            throw new InvalidDataException($"Unsupported wdc65c02 subset version {version}.");
        }

        int stride = reader.ReadUInt16();
        if (stride != Stride)
        {
            throw new InvalidDataException($"Subset stride {stride} does not match {Stride}.");
        }

        int opcodeCount = reader.ReadUInt16();
        if (opcodeCount != 256)
        {
            throw new InvalidDataException($"Subset opcode count {opcodeCount} is not 256.");
        }

        string commit = Encoding.ASCII.GetString(ReadExact(reader, UpstreamCommit.Length));
        if (!string.Equals(commit, UpstreamCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Subset commit {commit} does not match {UpstreamCommit}.");
        }

        var opcodes = new Wdc65C02VectorCase[256][];
        for (int opcode = 0; opcode < opcodes.Length; opcode++)
        {
            int count = reader.ReadUInt16();
            var slice = new Wdc65C02VectorCase[count];
            for (int index = 0; index < count; index++)
            {
                slice[index] = ReadCase(reader);
            }

            opcodes[opcode] = slice;
        }

        return new Wdc65C02VectorSet(stride, opcodes);
    }

    private static Wdc65C02VectorCase ReadCase(BinaryReader reader)
    {
        string name = Encoding.ASCII.GetString(ReadExact(reader, reader.ReadByte()));
        ushort pc = reader.ReadUInt16();
        byte a = reader.ReadByte();
        byte x = reader.ReadByte();
        byte y = reader.ReadByte();
        byte s = reader.ReadByte();
        byte p = reader.ReadByte();
        ushort finalPc = reader.ReadUInt16();
        byte finalA = reader.ReadByte();
        byte finalX = reader.ReadByte();
        byte finalY = reader.ReadByte();
        byte finalS = reader.ReadByte();
        byte finalP = reader.ReadByte();
        int cycles = reader.ReadByte();
        (ushort Address, byte Value)[] initialRam = ReadRam(reader);
        (ushort Address, byte Value)[] finalRam = ReadRam(reader);
        return new Wdc65C02VectorCase(
            name,
            pc,
            a,
            x,
            y,
            s,
            p,
            finalPc,
            finalA,
            finalX,
            finalY,
            finalS,
            finalP,
            cycles,
            initialRam,
            finalRam);
    }

    private static (ushort Address, byte Value)[] ReadRam(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        var bytes = new (ushort Address, byte Value)[count];
        for (int index = 0; index < count; index++)
        {
            bytes[index] = (reader.ReadUInt16(), reader.ReadByte());
        }

        return bytes;
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new InvalidDataException("The wdc65c02 subset ended early.");
        }

        return bytes;
    }
}