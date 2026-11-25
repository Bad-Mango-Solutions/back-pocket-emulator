// <copyright file="ProDosMliCall.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using System.Diagnostics.CodeAnalysis;

using BadMango.Emulator.Core.Interfaces.Cpu;

/// <summary>
/// One ProDOS MLI call: <c>JSR $BF00</c>, a command byte, and a parameter-list pointer.
/// </summary>
/// <remarks>
/// <para>
/// <c>JSR</c> pushes the address of its last byte. <see cref="ReadStackedReturn"/> reads that
/// word with <see cref="ICpu.Peek8"/>. The command is the byte at that address plus one, and the
/// parameter pointer is the little-endian word that follows. This is the only MLI name table and
/// pathname reader. Callers that watch <c>$BF00</c> decode through here.
/// </para>
/// <para>
/// A pathname is a length byte followed by that many characters, at most <see cref="MaxPathnameLength"/>.
/// Path-carrying calls are CREATE, DESTROY, RENAME, GET_FILE_INFO, SET_PREFIX, and OPEN.
/// RENAME also has a new pathname at <see cref="RenamePathPointerOffset"/>.
/// </para>
/// </remarks>
public readonly struct ProDosMliCall
{
    /// <summary>The longest ProDOS pathname, not counting the length byte.</summary>
    public const int MaxPathnameLength = 64;

    /// <summary>Offset of the pathname pointer in a path-carrying parameter list.</summary>
    public const int PathPointerOffset = 1;

    /// <summary>Offset of RENAME's new pathname pointer.</summary>
    public const int RenamePathPointerOffset = 3;

    private ProDosMliCall(byte number, ushort parameters)
    {
        this.Number = number;
        this.Parameters = parameters;
    }

    /// <summary>Gets the MLI command byte.</summary>
    /// <value>The byte immediately after the <c>JSR</c>.</value>
    public byte Number { get; }

    /// <summary>Gets the parameter-list address.</summary>
    /// <value>The little-endian word after the command byte.</value>
    public ushort Parameters { get; }

    /// <summary>
    /// Reads the 16-bit value <c>JSR</c> pushed. The command byte is at this address plus one.
    /// </summary>
    /// <param name="cpu">The CPU whose stack is peeked. The stack is not changed.</param>
    /// <returns>The address of the last byte of the <c>JSR</c>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="cpu"/> is <see langword="null"/>.</exception>
    public static ushort ReadStackedReturn(ICpu cpu)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        uint stackPointer = cpu.Registers.SP.GetByte();
        uint lowAddress = 0x0100u + ((stackPointer + 1u) & 0xFFu);
        uint highAddress = 0x0100u + ((stackPointer + 2u) & 0xFFu);
        return (ushort)(cpu.Peek8(lowAddress) | ((uint)cpu.Peek8(highAddress) << 8));
    }

    /// <summary>
    /// Decodes the command and parameter pointer that follow <paramref name="stackedReturn"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="stackedReturn">The value <see cref="ReadStackedReturn"/> returns.</param>
    /// <returns>The command byte and the parameter-list address.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static ProDosMliCall Decode(IGuestMemoryReader memory, ushort stackedReturn)
    {
        ArgumentNullException.ThrowIfNull(memory);
        byte number = memory.Read((ushort)(stackedReturn + 1));
        ushort parameters = ReadPointer(memory, (ushort)(stackedReturn + 2));
        return new ProDosMliCall(number, parameters);
    }

    /// <summary>
    /// Maps a TechRef MLI command onto its name.
    /// </summary>
    /// <param name="number">The command byte.</param>
    /// <param name="name">The TechRef name, or <see langword="null"/> when <paramref name="number"/> is not in the table.</param>
    /// <returns><see langword="true"/> when the table has a name.</returns>
    public static bool TryGetName(byte number, [NotNullWhen(true)] out string? name)
    {
        name = number switch
        {
            0x40 => "ALLOC_INTERRUPT",
            0x41 => "DEALLOC_INTERRUPT",
            0x65 => "QUIT",
            0x80 => "READ_BLOCK",
            0x81 => "WRITE_BLOCK",
            0x82 => "GET_TIME",
            0xC0 => "CREATE",
            0xC1 => "DESTROY",
            0xC2 => "RENAME",
            0xC3 => "SET_FILE_INFO",
            0xC4 => "GET_FILE_INFO",
            0xC5 => "ON_LINE",
            0xC6 => "SET_PREFIX",
            0xC7 => "GET_PREFIX",
            0xC8 => "OPEN",
            0xC9 => "NEWLINE",
            0xCA => "READ",
            0xCB => "WRITE",
            0xCC => "CLOSE",
            0xCD => "FLUSH",
            0xCE => "SET_MARK",
            0xCF => "GET_MARK",
            0xD0 => "SET_EOF",
            0xD1 => "GET_EOF",
            0xD2 => "SET_BUF",
            0xD3 => "GET_BUF",
            _ => null,
        };
        return name is not null;
    }

    /// <summary>
    /// Returns the TechRef name, or <c>$xx (unknown)</c> when the command is not in the table.
    /// </summary>
    /// <param name="number">The command byte.</param>
    /// <returns>A non-empty name. Unknown commands use two uppercase hex digits.</returns>
    public static string FormatName(byte number) =>
        TryGetName(number, out string? name) ? name : $"${number:X2} (unknown)";

    /// <summary>
    /// Reports whether the call's parameter list starts with a pathname pointer.
    /// </summary>
    /// <param name="number">The command byte.</param>
    /// <returns>
    /// <see langword="true"/> for CREATE, DESTROY, RENAME, GET_FILE_INFO, SET_PREFIX, and OPEN.
    /// </returns>
    public static bool CarriesPathname(byte number) =>
        number is 0xC0 or 0xC1 or 0xC2 or 0xC4 or 0xC6 or 0xC8;

    /// <summary>Reads a little-endian pointer.</summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="address">The address of the low byte.</param>
    /// <returns>The 16-bit value.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static ushort ReadPointer(IGuestMemoryReader memory, ushort address)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return (ushort)(memory.Read(address) | (memory.Read((ushort)(address + 1)) << 8));
    }

    /// <summary>
    /// Copies a length-prefixed pathname. The length is capped at <see cref="MaxPathnameLength"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="pathname">The address of the length byte.</param>
    /// <param name="destination">The buffer that receives the characters.</param>
    /// <returns>The number of characters copied. Stops early on a byte that is not printable ASCII.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static int CopyPathname(IGuestMemoryReader memory, ushort pathname, Span<char> destination)
    {
        ArgumentNullException.ThrowIfNull(memory);
        int length = memory.Read(pathname);
        if (length == 0 || destination.IsEmpty)
        {
            return 0;
        }

        int count = length;
        if (count > MaxPathnameLength)
        {
            count = MaxPathnameLength;
        }

        if (count > destination.Length)
        {
            count = destination.Length;
        }

        for (int index = 0; index < count; index++)
        {
            char ch = (char)(memory.Read((ushort)(pathname + 1 + index)) & 0x7F);
            if (ch < ' ' || ch > '~')
            {
                return index;
            }

            destination[index] = ch;
        }

        return count;
    }

    /// <summary>
    /// Copies the pathname pointer at <see cref="PathPointerOffset"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="destination">The buffer that receives the characters. Not the length byte.</param>
    /// <returns>The number of characters copied. Zero when this call has no pathname.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public int CopyPrimaryPath(IGuestMemoryReader memory, Span<char> destination)
    {
        if (!CarriesPathname(this.Number))
        {
            return 0;
        }

        ushort path = ReadPointer(memory, (ushort)(this.Parameters + PathPointerOffset));
        return CopyPathname(memory, path, destination);
    }

    /// <summary>
    /// Copies RENAME's new pathname into <paramref name="destination"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <param name="destination">The buffer that receives the characters.</param>
    /// <returns>The number of characters copied. Zero when this call is not RENAME.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public int CopyNewPath(IGuestMemoryReader memory, Span<char> destination)
    {
        if (this.Number != 0xC2)
        {
            return 0;
        }

        ushort path = ReadPointer(memory, (ushort)(this.Parameters + RenamePathPointerOffset));
        return CopyPathname(memory, path, destination);
    }
}