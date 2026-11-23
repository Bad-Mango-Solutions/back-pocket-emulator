// <copyright file="AppleCatalogName.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using System.Globalization;
using System.Text;

/// <summary>
/// Turns Apple II catalog bytes into a host string that keeps inverse and flashing visible.
/// </summary>
/// <remarks>
/// <para>
/// DOS 3.3 stores each catalog character with the high bit set. That is normal text,
/// including lowercase in <c>$E0</c>–<c>$FF</c>. Those bytes keep the low seven bits, so
/// <c>$C8 $E5 $EC $EC $EF</c> is <c>Hello</c>. A clear high bit is the inverse / flashing
/// catalog-name trick: bit 6 clear is inverse, and bit 6 set is flashing. Only those bytes
/// use the Apple II 64-character set. <c>$00</c>–<c>$1F</c> are <c>@</c> through <c>_</c>,
/// and <c>$20</c>–<c>$3F</c> are space through <c>?</c>. High-bit controls (<c>$80</c>–<c>$9F</c>)
/// display as <c>\c{G}</c> and use that same glyph as the lookup name.
/// </para>
/// <para>
/// <see cref="TryDecodeDos"/> trims trailing <see cref="Dos33Layout.CatalogEntry.HighBitSpace"/>
/// padding. <see cref="FileEntry.Name"/> is the glyph, so lookup still finds the file.
/// <see cref="FileEntry.DisplayName"/> escapes inverse as <c>\i{X}</c> and flashing as
/// <c>\f{X}</c>. A backslash, brace, or non-printable glyph is escaped inside that text.
/// ProDOS and Pascal names are already printable; <see cref="EscapePlain"/> is for those
/// strings and for volume names.
/// </para>
/// </remarks>
public static class AppleCatalogName
{
    /// <summary>
    /// Decodes one DOS 3.3 catalog name.
    /// </summary>
    /// <param name="catalogName">On-disk name bytes, including <c>$A0</c> padding.</param>
    /// <param name="name">The visible glyphs, or empty when the name is only padding.</param>
    /// <param name="displayName">
    /// <paramref name="name"/> with inverse and flashing characters escaped, or empty when
    /// decoding fails.
    /// </param>
    /// <returns><see langword="false"/> when every remaining byte is padding.</returns>
    public static bool TryDecodeDos(ReadOnlySpan<byte> catalogName, out string name, out string displayName)
    {
        var length = catalogName.Length;
        while (length > 0 && catalogName[length - 1] == Dos33Layout.CatalogEntry.HighBitSpace)
        {
            length--;
        }

        if (length == 0)
        {
            name = string.Empty;
            displayName = string.Empty;
            return false;
        }

        var glyphs = new StringBuilder(length);
        var display = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            var value = catalogName[i];
            var glyph = Glyph(value);
            glyphs.Append(glyph);
            AppendDosGlyph(display, value, glyph);
        }

        name = glyphs.ToString();
        displayName = display.ToString();
        return true;
    }

    /// <summary>
    /// Escapes a host string that has no inverse or flashing metadata.
    /// </summary>
    /// <param name="text">The volume or file name. Not <see langword="null"/>.</param>
    /// <returns>
    /// <paramref name="text"/> when every character is printable ASCII other than a backslash
    /// or a brace; otherwise a string with those characters escaped.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="text"/> is <see langword="null"/>.</exception>
    public static string EscapePlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!NeedsPlainEscape(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            AppendPlain(builder, ch);
        }

        return builder.ToString();
    }

    private static char Glyph(byte value)
    {
        if ((value & 0x80) == 0)
        {
            var low = value & 0x3F;
            return low < 0x20 ? (char)(low + 0x40) : (char)low;
        }

        var seven = value & 0x7F;
        if (seven is >= 0x20 and <= 0x7E)
        {
            return (char)seven;
        }

        return (char)((value & 0x1F) + 0x40);
    }

    private static void AppendDosGlyph(StringBuilder display, byte value, char glyph)
    {
        if ((value & 0x80) != 0)
        {
            var seven = value & 0x7F;
            if (seven is >= 0x20 and <= 0x7E)
            {
                AppendPlain(display, glyph);
                return;
            }

            display.Append("\\c{");
            AppendPlain(display, glyph);
            display.Append('}');
            return;
        }

        display.Append((value & 0x40) != 0 ? "\\f{" : "\\i{");
        AppendPlain(display, glyph);
        display.Append('}');
    }

    private static void AppendPlain(StringBuilder display, char glyph)
    {
        if (glyph == '\\')
        {
            display.Append("\\\\");
            return;
        }

        if (glyph is '{' or '}')
        {
            display.Append('\\');
            display.Append(glyph);
            return;
        }

        if (glyph is >= ' ' and <= '~')
        {
            display.Append(glyph);
            return;
        }

        display.Append("\\u");
        display.Append(((int)glyph).ToString("X4", CultureInfo.InvariantCulture));
    }

    private static bool NeedsPlainEscape(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '\\' or '{' or '}' || ch is < ' ' or > '~')
            {
                return true;
            }
        }

        return false;
    }
}