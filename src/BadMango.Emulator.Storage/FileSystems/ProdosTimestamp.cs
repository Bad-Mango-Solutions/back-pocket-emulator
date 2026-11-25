// <copyright file="ProdosTimestamp.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using System.Globalization;

/// <summary>
/// One ProDOS creation or modification stamp.
/// </summary>
/// <remarks>
/// <para>
/// A date word of zero is <see cref="HasDate"/> <see langword="false"/>, which a catalog prints
/// as <c>&lt;NO DATE&gt;</c>. The packed year is seven bits. A field of 40–99 is 1940–1999.
/// A field of 0–39 is 2000–2039. Technical Note #28 leaves 100–127 undefined; decode maps
/// those values to 1900 plus the field.
/// </para>
/// <para>
/// The default value is a zero date word.
/// </para>
/// </remarks>
/// <param name="HasDate">Whether the date word is non-zero.</param>
/// <param name="Year">The full year when <paramref name="HasDate"/> is set, otherwise 0.</param>
/// <param name="Month">The month, 1–12, when the date word stores one.</param>
/// <param name="Day">The day, 1–31, when the date word stores one.</param>
/// <param name="Hour">The hour, 0–23.</param>
/// <param name="Minute">The minute, 0–59.</param>
/// <param name="DateWord">The on-disk date word. Zero means no date.</param>
/// <param name="TimeWord">The on-disk time word.</param>
public readonly record struct ProdosTimestamp(
    bool HasDate,
    int Year,
    int Month,
    int Day,
    int Hour,
    int Minute,
    ushort DateWord,
    ushort TimeWord)
{
    /// <summary>
    /// Decodes a ProDOS date word and time word.
    /// </summary>
    /// <param name="dateWord">Bits 15–9 year, 8–5 month, 4–0 day.</param>
    /// <param name="timeWord">Bits 12–8 hour, 5–0 minute.</param>
    /// <returns>The stamp. A zero <paramref name="dateWord"/> has <see cref="HasDate"/> clear.</returns>
    public static ProdosTimestamp Decode(ushort dateWord, ushort timeWord)
    {
        if (dateWord == 0)
        {
            return new ProdosTimestamp(false, 0, 0, 0, 0, 0, 0, timeWord);
        }

        var day = dateWord & 0x1F;
        var month = (dateWord >> 5) & 0x0F;
        var yearField = (dateWord >> 9) & 0x7F;
        var year = yearField <= 39 ? 2000 + yearField : 1900 + yearField;
        var minute = timeWord & 0x3F;
        var hour = (timeWord >> 8) & 0x1F;
        return new ProdosTimestamp(true, year, month, day, hour, minute, dateWord, timeWord);
    }

    /// <summary>
    /// Packs a catalog stamp. The year argument is the seven-bit ProDOS field, not the full year.
    /// </summary>
    /// <param name="yearField">0–39 for 2000–2039, or 40–99 for 1940–1999.</param>
    /// <param name="month">Month, 1–12.</param>
    /// <param name="day">Day, 1–31.</param>
    /// <param name="hour">Hour, 0–23.</param>
    /// <param name="minute">Minute, 0–59.</param>
    /// <returns>A stamp whose <see cref="HasDate"/> is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If a field is outside the ProDOS range.</exception>
    public static ProdosTimestamp Encode(int yearField, int month, int day, int hour, int minute)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(yearField);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(yearField, 99);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 31);
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfNegative(minute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);

        var dateWord = (ushort)((day & 0x1F) | ((month & 0x0F) << 5) | ((yearField & 0x7F) << 9));
        var timeWord = (ushort)((minute & 0x3F) | ((hour & 0x1F) << 8));
        return Decode(dateWord, timeWord);
    }

    /// <summary>
    /// Formats catalog date and time fields. The year is the full year.
    /// </summary>
    /// <param name="year">Full year. The catalog prints <c>year % 100</c>.</param>
    /// <param name="month">Month. Values outside 1–12 print as <c>???</c>.</param>
    /// <param name="day">Day of the month.</param>
    /// <param name="hour">Hour, printed space-padded to two columns.</param>
    /// <param name="minute">Minute, printed zero-padded.</param>
    /// <returns>A 15-character catalog stamp such as <c>13-FEB-18  9:09</c>.</returns>
    public static string FormatCatalog(int year, int month, int day, int hour, int minute)
    {
        var monthName = month switch
        {
            1 => "JAN",
            2 => "FEB",
            3 => "MAR",
            4 => "APR",
            5 => "MAY",
            6 => "JUN",
            7 => "JUL",
            8 => "AUG",
            9 => "SEP",
            10 => "OCT",
            11 => "NOV",
            12 => "DEC",
            _ => "???",
        };
        var displayedYear = year % 100;
        if (displayedYear < 0)
        {
            displayedYear = 0;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}-{1}-{2:00} {3,2}:{4:00}",
            day,
            monthName,
            displayedYear,
            hour,
            minute);
    }

    /// <summary>
    /// Formats a stamp the way ProDOS 2.4.3 <c>CATALOG</c> does.
    /// </summary>
    /// <returns><c>dd-MMM-yy</c> and a space-padded hour, or <c>&lt;NO DATE&gt;</c>.</returns>
    public string ToCatalogText() =>
        this.HasDate ? FormatCatalog(this.Year, this.Month, this.Day, this.Hour, this.Minute) : "<NO DATE>";
}