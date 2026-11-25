// <copyright file="ProDosMliFileCall.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Minimal observe-only decode of the ProDOS MLI calls that move file bytes.
/// </summary>
/// <remarks>
/// <para>
/// The call is <c>JSR $BF00</c>, then the command byte, then the parameter-list pointer.
/// Layouts are the MLI parameter blocks from the ProDOS 8 Technical Reference.
/// This type is the only reader of those layouts. The OS call log (#686) can replace it
/// with that log's decoder; <see cref="ProDosFileLoadTracker"/> keeps calling this surface.
/// </para>
/// <para>
/// Returned fields (ref_num, trans_count, EOF, mark, blocks, prefix) are read by the caller
/// after the MLI returns. Nothing here writes guest memory.
/// </para>
/// </remarks>
internal static class ProDosMliFileCall
{
    /// <summary>MLI <c>GET_FILE_INFO</c> (<c>$C4</c>).</summary>
    internal const byte GetFileInfo = 0xC4;

    /// <summary>MLI <c>SET_PREFIX</c> (<c>$C6</c>).</summary>
    internal const byte SetPrefix = 0xC6;

    /// <summary>MLI <c>GET_PREFIX</c> (<c>$C7</c>).</summary>
    internal const byte GetPrefix = 0xC7;

    /// <summary>MLI <c>OPEN</c> (<c>$C8</c>).</summary>
    internal const byte Open = 0xC8;

    /// <summary>MLI <c>READ</c> (<c>$CA</c>).</summary>
    internal const byte Read = 0xCA;

    /// <summary>MLI <c>CLOSE</c> (<c>$CC</c>).</summary>
    internal const byte Close = 0xCC;

    /// <summary>MLI <c>FLUSH</c> (<c>$CD</c>).</summary>
    internal const byte Flush = 0xCD;

    /// <summary>MLI <c>SET_MARK</c> (<c>$CE</c>).</summary>
    internal const byte SetMark = 0xCE;

    /// <summary>MLI <c>GET_MARK</c> (<c>$CF</c>).</summary>
    internal const byte GetMark = 0xCF;

    /// <summary>MLI <c>GET_EOF</c> (<c>$D1</c>).</summary>
    internal const byte GetEof = 0xD1;

    private const int MaxPathLength = 64;
    private const int OpenRefOffset = 5;
    private const int RefOffset = 1;
    private const int PathPointerOffset = 1;
    private const int Value24Offset = 2;
    private const int TransCountOffset = 6;
    private const int BlocksOffset = 8;

    /// <summary>
    /// Reads the command and parameter pointer at <paramref name="commandAddress"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="commandAddress">The byte after <c>JSR $BF00</c>.</param>
    /// <param name="decoded">The call when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> for a command this tracker does not follow.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    internal static bool TryDecode(IGuestMemoryReader memory, ushort commandAddress, out Decoded decoded)
    {
        ArgumentNullException.ThrowIfNull(memory);
        byte command = memory.Read(commandAddress);
        if (!IsTracked(command))
        {
            decoded = default;
            return false;
        }

        ushort parameterList = ReadWord(memory, (uint)commandAddress + 1u);
        ushort returnAddress = (ushort)(commandAddress + 3u);
        decoded = new Decoded(command, parameterList, returnAddress, CompletesOnReturn(command));
        return true;
    }

    /// <summary>Reads OPEN's returned ref_num.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The OPEN parameter list.</param>
    /// <returns>The ref_num byte. Zero means the open failed.</returns>
    internal static byte ReadOpenRef(IGuestMemoryReader memory, ushort parameterList) =>
        memory.Read((uint)parameterList + OpenRefOffset);

    /// <summary>Reads the ref_num input of READ, CLOSE, FLUSH, GET_EOF, SET_MARK, and GET_MARK.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The parameter list.</param>
    /// <returns>The ref_num byte. Zero on CLOSE or FLUSH means every open file.</returns>
    internal static byte ReadRef(IGuestMemoryReader memory, ushort parameterList) =>
        memory.Read((uint)parameterList + RefOffset);

    /// <summary>Reads the length-prefixed pathname or prefix buffer pointed at by the parameter list.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The parameter list whose second field is a pointer.</param>
    /// <returns>The string. Empty when the length byte is zero.</returns>
    internal static string ReadReferencedPath(IGuestMemoryReader memory, ushort parameterList)
    {
        ushort pointer = ReadWord(memory, (uint)parameterList + PathPointerOffset);
        return ReadPascalString(memory, pointer);
    }

    /// <summary>Reads READ's returned trans_count.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The READ parameter list.</param>
    /// <returns>The transferred byte count.</returns>
    internal static ushort ReadTransCount(IGuestMemoryReader memory, ushort parameterList) =>
        ReadWord(memory, (uint)parameterList + TransCountOffset);

    /// <summary>Reads GET_EOF's returned EOF, or SET_MARK / GET_MARK's 24-bit position.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The parameter list.</param>
    /// <returns>The 24-bit value.</returns>
    internal static uint Read24(IGuestMemoryReader memory, ushort parameterList) =>
        ReadTriple(memory, (uint)parameterList + Value24Offset);

    /// <summary>Reads GET_FILE_INFO's returned block count.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="parameterList">The GET_FILE_INFO parameter list.</param>
    /// <returns>The blocks_used field.</returns>
    internal static ushort ReadBlocks(IGuestMemoryReader memory, ushort parameterList) =>
        ReadWord(memory, (uint)parameterList + BlocksOffset);

    private static bool IsTracked(byte command) => command is
        GetFileInfo or SetPrefix or GetPrefix or Open or Read or Close or Flush or SetMark or GetMark or GetEof;

    private static bool CompletesOnReturn(byte command) => command is
        Open or Read or GetEof or GetMark or GetFileInfo or GetPrefix;

    private static ushort ReadWord(IGuestMemoryReader memory, uint address)
    {
        uint low = memory.Read(address & 0xFFFFu);
        uint high = memory.Read((address + 1u) & 0xFFFFu);
        return (ushort)(low | (high << 8));
    }

    private static uint ReadTriple(IGuestMemoryReader memory, uint address)
    {
        uint low = memory.Read(address & 0xFFFFu);
        uint middle = memory.Read((address + 1u) & 0xFFFFu);
        uint high = memory.Read((address + 2u) & 0xFFFFu);
        return low | (middle << 8) | (high << 16);
    }

    private static string ReadPascalString(IGuestMemoryReader memory, uint address)
    {
        int length = memory.Read(address & 0xFFFFu);
        if (length > MaxPathLength)
        {
            length = MaxPathLength;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        Span<char> chars = stackalloc char[MaxPathLength];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)(memory.Read((address + 1u + (uint)i) & 0xFFFFu) & 0x7F);
        }

        return new string(chars[..length]);
    }

    /// <summary>One decoded file call. The parameter list is the guest pointer, not the command address.</summary>
    /// <param name="Command">The MLI command byte.</param>
    /// <param name="ParameterList">The parameter-list address.</param>
    /// <param name="ReturnAddress">The instruction after the three inline call bytes.</param>
    /// <param name="CompletesOnReturn">
    /// <see langword="true"/> when a result byte is written by the MLI and must be read on return.
    /// </param>
    internal readonly record struct Decoded(
        byte Command,
        ushort ParameterList,
        ushort ReturnAddress,
        bool CompletesOnReturn);
}