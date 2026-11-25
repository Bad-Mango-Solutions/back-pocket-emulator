// <copyright file="ConfiguredClock.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Globalization;

using BadMango.Emulator.Core.Configuration;

using Interfaces;

/// <summary>
/// Time source shared by <see cref="PocketWatchCard"/> and <see cref="ProDosClockDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PocketWatchConfig.TimezoneOffset"/> null uses <see cref="TimeProvider.GetLocalNow"/>,
/// including daylight saving time. A value, including zero, is UTC plus that many minutes.
/// Frozen time ignores the offset. <c>ntp</c> is not implemented and uses the host clock.
/// </para>
/// </remarks>
internal sealed class ConfiguredClock : IClockSource
{
    private static readonly DateTime DefaultFrozenTime = new(1986, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TimeProvider timeProvider;
    private readonly int? timezoneOffsetMinutes;
    private DateTime fixedTime;
    private bool useHostTime = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredClock"/> class.
    /// </summary>
    /// <param name="config">The clock configuration. The same fields as a PocketWatch card.</param>
    /// <param name="timeProvider">
    /// Clock used for host time. <see langword="null"/> uses <see cref="TimeProvider.System"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="config"/> is <see langword="null"/>.</exception>
    public ConfiguredClock(PocketWatchConfig config, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        timezoneOffsetMinutes = config.TimezoneOffset;
        ApplySource(config);
    }

    /// <inheritdoc />
    public DateTime CurrentTime
    {
        get
        {
            if (!useHostTime)
            {
                return fixedTime;
            }

            if (timezoneOffsetMinutes is int offsetMinutes)
            {
                return this.timeProvider.GetUtcNow().AddMinutes(offsetMinutes).DateTime;
            }

            return this.timeProvider.GetLocalNow().DateTime;
        }
    }

    /// <inheritdoc />
    public bool UseHostTime
    {
        get => useHostTime;
        set => useHostTime = value;
    }

    /// <inheritdoc />
    public void SetFixedTime(DateTime time)
    {
        fixedTime = time;
        useHostTime = false;
    }

    private void ApplySource(PocketWatchConfig config)
    {
        switch ((config.TimeSource ?? "host").ToLowerInvariant())
        {
            case "frozen":
                useHostTime = false;
                fixedTime = DefaultFrozenTime;
                if (!string.IsNullOrEmpty(config.FrozenTime) &&
                    DateTime.TryParse(
                        config.FrozenTime,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTime parsedTime))
                {
                    fixedTime = parsedTime;
                }

                break;

            case "host":
            case "ntp":
            default:
                useHostTime = true;
                break;
        }
    }
}