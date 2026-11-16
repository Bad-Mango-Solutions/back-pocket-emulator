// <copyright file="TextScreen.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Rendering;

/// <summary>
/// Apple II text-page layout and character-code decoding shared by the renderer and debug readers.
/// </summary>
/// <remarks>
/// <para>
/// Row addresses follow the hardware text-page map used by <see cref="Pocket2VideoRenderer"/>:
/// page 1 is based at <c>$0400</c>, and each row is <c>base + (row % 8) * 128 + (row / 8) * 40</c>.
/// Page 2 uses the same map with a <c>$0400</c> offset (<c>$0800</c>). 80-column mode always reads
/// page 1 addresses; even columns come from auxiliary memory and odd columns from main memory.
/// PAGE2 does not select a second 80-column page.
/// </para>
/// <para>
/// Character codes use the Apple II video generator ranges: <c>$00-$3F</c> inverse,
/// <c>$40-$7F</c> flashing, and <c>$80-$FF</c> normal. Decoding returns the visible glyph.
/// Inverse and flashing are display attributes, not different characters.
/// </para>
/// </remarks>
public static class TextScreen
{
    /// <summary>
    /// The number of text rows on an Apple II screen.
    /// </summary>
    public const int Rows = 24;

    /// <summary>
    /// The number of columns in 40-column text mode.
    /// </summary>
    public const int Columns40 = 40;

    /// <summary>
    /// The number of columns in 80-column text mode.
    /// </summary>
    public const int Columns80 = 80;

    /// <summary>
    /// Text page 1 base address.
    /// </summary>
    public const ushort Page1Base = 0x0400;

    /// <summary>
    /// Offset from a page 1 row address to the corresponding page 2 address.
    /// </summary>
    public const ushort Page2Offset = 0x0400;

    /// <summary>
    /// Page 1 text-row base addresses, in row order.
    /// </summary>
    private static readonly ushort[] Page1RowAddresses = CreatePage1RowAddresses();

    /// <summary>
    /// Gets the page 1 base address of a text row.
    /// </summary>
    /// <param name="row">The zero-based row, from 0 through <see cref="Rows"/> - 1.</param>
    /// <returns>The page 1 address of column 0 of <paramref name="row"/>.</returns>
    public static ushort Page1RowAddress(int row) => Page1RowAddresses[row];

    /// <summary>
    /// Gets a value indicating whether a character code is displayed inverse.
    /// </summary>
    /// <param name="charCode">The video character code.</param>
    /// <returns><see langword="true"/> when <paramref name="charCode"/> is in <c>$00-$3F</c>.</returns>
    public static bool IsInverse(byte charCode) => charCode < 0x40;

    /// <summary>
    /// Gets a value indicating whether a character code is displayed flashing.
    /// </summary>
    /// <param name="charCode">The video character code.</param>
    /// <returns><see langword="true"/> when <paramref name="charCode"/> is in <c>$40-$7F</c>.</returns>
    public static bool IsFlashing(byte charCode) => charCode is >= 0x40 and < 0x80;

    /// <summary>
    /// Maps a video character code to its glyph, including non-printable results.
    /// </summary>
    /// <param name="charCode">The video character code.</param>
    /// <returns>The glyph before the printable-ASCII filter.</returns>
    public static char GlyphFor(int charCode)
    {
        int mapped = charCode switch
        {
            < 0x20 => charCode + 0x40,
            < 0x40 => charCode,
            < 0x60 => charCode,
            < 0x80 => charCode - 0x40,
            < 0xA0 => charCode - 0x40,
            _ => charCode - 0x80,
        };

        return (char)mapped;
    }

    /// <summary>
    /// Maps a video character code to a printable glyph.
    /// </summary>
    /// <param name="charCode">The video character code.</param>
    /// <returns>
    /// The visible character, or <c>'.'</c> when the mapped glyph is outside printable ASCII.
    /// Inverse and flashing codes return the same glyph as the corresponding normal character.
    /// </returns>
    public static char DecodeCharacter(byte charCode)
    {
        char glyph = GlyphFor(charCode);
        return glyph is >= (char)0x20 and <= (char)0x7E ? glyph : '.';
    }

    /// <summary>
    /// Reads the current text screen from main memory and, in 80-column mode, auxiliary memory.
    /// </summary>
    /// <param name="readMain">Reads one byte of physical main RAM.</param>
    /// <param name="readAux">
    /// Reads one byte of auxiliary RAM. Required for 80-column mode. When <see langword="null"/>,
    /// 80-column mode falls back to 40 columns, matching <see cref="Pocket2VideoRenderer"/>.
    /// </param>
    /// <param name="is80Column">Whether 80-column mode is selected.</param>
    /// <param name="isPage2">Whether page 2 is selected. Ignored in 80-column mode.</param>
    /// <returns>Twenty-four lines of decoded text. Each line is 40 or 80 characters.</returns>
    public static string[] ReadLines(
        Func<ushort, byte> readMain,
        Func<ushort, byte>? readAux,
        bool is80Column,
        bool isPage2)
    {
        ArgumentNullException.ThrowIfNull(readMain);

        bool use80Columns = is80Column && readAux is not null;
        int columns = use80Columns ? Columns80 : Columns40;
        var lines = new string[Rows];
        for (int row = 0; row < Rows; row++)
        {
            ushort rowAddress = Page1RowAddresses[row];
            if (!use80Columns && isPage2)
            {
                rowAddress += Page2Offset;
            }

            var chars = new char[columns];
            if (use80Columns)
            {
                for (int col = 0; col < columns; col++)
                {
                    ushort address = (ushort)(rowAddress + (col / 2));
                    byte charCode = (col & 1) == 0
                        ? readAux!(address)
                        : readMain(address);
                    chars[col] = DecodeCharacter(charCode);
                }
            }
            else
            {
                for (int col = 0; col < columns; col++)
                {
                    chars[col] = DecodeCharacter(readMain((ushort)(rowAddress + col)));
                }
            }

            lines[row] = new string(chars);
        }

        return lines;
    }

    /// <summary>
    /// Builds the immutable page 1 row-address table.
    /// </summary>
    /// <returns>The 24 page 1 row base addresses.</returns>
    private static ushort[] CreatePage1RowAddresses()
    {
        var addresses = new ushort[Rows];
        for (int row = 0; row < Rows; row++)
        {
            int group = row / 8;
            int offset = row % 8;
            addresses[row] = (ushort)(Page1Base + (offset * 128) + (group * 40));
        }

        return addresses;
    }
}