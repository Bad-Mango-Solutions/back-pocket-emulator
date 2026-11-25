// <copyright file="FixedTimeProvider.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// <see cref="TimeProvider"/> that returns one UTC instant in a caller-supplied local zone.
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset utcNow;
    private readonly TimeZoneInfo localZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedTimeProvider"/> class.
    /// </summary>
    /// <param name="utcNow">The UTC instant returned by <see cref="GetUtcNow"/>.</param>
    /// <param name="localZone">The zone <see cref="TimeProvider.GetLocalNow"/> converts that instant into.</param>
    public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localZone)
    {
        this.utcNow = utcNow;
        this.localZone = localZone;
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => localZone;

    /// <summary>
    /// Builds a Pacific-like zone: UTC-8 standard time, with a one-hour daylight saving
    /// advance from the second Sunday in March until the first Sunday in November.
    /// </summary>
    /// <returns>A zone whose summer offset is UTC-7 and whose winter offset is UTC-8.</returns>
    public static TimeZoneInfo CreatePacificLikeZone()
    {
        TimeZoneInfo.TransitionTime dstStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime dstEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2039, 12, 31),
            TimeSpan.FromHours(1),
            dstStart,
            dstEnd);
        return TimeZoneInfo.CreateCustomTimeZone(
            "TestPacific",
            TimeSpan.FromHours(-8),
            "Test Pacific",
            "Test Pacific Standard Time",
            "Test Pacific Daylight Time",
            new[] { rule });
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => utcNow;
}