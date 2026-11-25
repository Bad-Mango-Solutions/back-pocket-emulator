// <copyright file="PocketWatchCardTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Core.Configuration;
using BadMango.Unit.Components;

using Serilog;
using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Unit tests for <see cref="PocketWatchCard"/>.
/// </summary>
[TestFixture]
public class PocketWatchCardTests
{
    /// <summary>
    /// Tests that default constructor creates a valid card.
    /// </summary>
    [Test]
    public void Constructor_Default_CreatesValidCard()
    {
        // Act
        var card = new PocketWatchCard();

        // Assert
        Assert.That(card.Name, Is.EqualTo("PocketWatch"));
        Assert.That(card.DeviceType, Is.EqualTo("PocketWatch"));
        Assert.That(card.Kind, Is.EqualTo(PeripheralKind.SlotCard));
        Assert.That(card.IOHandlers, Is.Not.Null);
        Assert.That(card.ROMRegion, Is.Not.Null);
        Assert.That(card.ExpansionROMRegion, Is.Null);
    }

    /// <summary>
    /// Tests that card uses host time by default.
    /// </summary>
    [Test]
    public void UseHostTime_Default_ReturnsTrue()
    {
        // Arrange
        var card = new PocketWatchCard();

        // Act & Assert
        Assert.That(card.UseHostTime, Is.True);
    }

    /// <summary>
    /// Tests that the system clock default returns host local time.
    /// </summary>
    [Test]
    public void CurrentTime_UseHostTime_ReturnsHostLocalTime()
    {
        // Arrange
        var card = new PocketWatchCard();
        var before = DateTime.Now;

        // Act
        var time = card.CurrentTime;
        var after = DateTime.Now;

        // Assert (allow some tolerance for test execution time)
        Assert.That(time, Is.GreaterThanOrEqualTo(before.AddSeconds(-1)));
        Assert.That(time, Is.LessThanOrEqualTo(after.AddSeconds(1)));
    }

    /// <summary>
    /// Verifies that a missing offset follows the provider's local zone, including daylight saving time.
    /// </summary>
    [Test]
    public void CurrentTime_NoOffset_UsesProviderLocalTimeIncludingDaylightSaving()
    {
        TimeZoneInfo zone = FixedTimeProvider.CreatePacificLikeZone();
        var summerUtc = new DateTimeOffset(2026, 7, 15, 17, 30, 0, TimeSpan.Zero);
        var winterUtc = new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero);
        var summerProvider = new FixedTimeProvider(summerUtc, zone);
        var winterProvider = new FixedTimeProvider(winterUtc, zone);
        var summer = new PocketWatchCard(new PocketWatchConfig(), summerProvider);
        var winter = new PocketWatchCard(new PocketWatchConfig(), winterProvider);

        Assert.Multiple(() =>
        {
            Assert.That(summerProvider.GetLocalNow().Offset, Is.EqualTo(TimeSpan.FromHours(-7)));
            Assert.That(winterProvider.GetLocalNow().Offset, Is.EqualTo(TimeSpan.FromHours(-8)));
            Assert.That(summer.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 10, 30, 0)));
            Assert.That(winter.CurrentTime, Is.EqualTo(new DateTime(2026, 1, 15, 10, 30, 0)));
        });
    }

    /// <summary>
    /// Tests that SetFixedTime sets a specific time.
    /// </summary>
    [Test]
    public void SetFixedTime_ValidTime_ReturnsFixedTime()
    {
        // Arrange
        var card = new PocketWatchCard();
        var fixedTime = new DateTime(1986, 1, 1, 12, 30, 0, DateTimeKind.Utc);

        // Act
        card.SetFixedTime(fixedTime);

        // Assert
        Assert.That(card.UseHostTime, Is.False);
        Assert.That(card.CurrentTime, Is.EqualTo(fixedTime));
    }

    /// <summary>
    /// Tests that frozen time source configuration works.
    /// </summary>
    [Test]
    public void Constructor_FrozenTimeConfig_UsesConfiguredTime()
    {
        // Arrange
        var config = new PocketWatchConfig
        {
            TimeSource = "frozen",
            FrozenTime = "1986-06-15T10:30:00",
        };

        // Act
        var card = new PocketWatchCard(config);

        // Assert
        Assert.That(card.UseHostTime, Is.False);
        Assert.That(card.CurrentTime.Year, Is.EqualTo(1986));
        Assert.That(card.CurrentTime.Month, Is.EqualTo(6));
        Assert.That(card.CurrentTime.Day, Is.EqualTo(15));
        Assert.That(card.CurrentTime.Hour, Is.EqualTo(10));
        Assert.That(card.CurrentTime.Minute, Is.EqualTo(30));
    }

    /// <summary>
    /// Tests that invalid frozen time falls back to default.
    /// </summary>
    [Test]
    public void Constructor_InvalidFrozenTime_UsesDefaultFrozenTime()
    {
        // Arrange
        var config = new PocketWatchConfig
        {
            TimeSource = "frozen",
            FrozenTime = "not-a-date",
        };

        // Act
        var card = new PocketWatchCard(config);

        // Assert
        Assert.That(card.UseHostTime, Is.False);
        Assert.That(card.CurrentTime.Year, Is.EqualTo(1986)); // Default fallback year
    }

    /// <summary>
    /// Tests that an explicit timezone offset, including zero, is applied to UTC and ignores the local zone.
    /// </summary>
    [Test]
    public void CurrentTime_WithTimezoneOffset_AppliesOffset()
    {
        var provider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 15, 17, 30, 0, TimeSpan.Zero),
            FixedTimeProvider.CreatePacificLikeZone());
        var plusOneHour = new PocketWatchCard(
            new PocketWatchConfig
            {
                TimeSource = "host",
                TimezoneOffset = 60,
            },
            provider);
        var utc = new PocketWatchCard(
            new PocketWatchConfig
            {
                TimeSource = "host",
                TimezoneOffset = 0,
            },
            provider);

        Assert.Multiple(() =>
        {
            Assert.That(plusOneHour.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 18, 30, 0)));
            Assert.That(utc.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 17, 30, 0)));
        });
    }

    /// <summary>
    /// Verifies that frozen time ignores the injected clock and any timezone offset.
    /// </summary>
    [Test]
    public void CurrentTime_Frozen_IgnoresTimeProviderAndOffset()
    {
        var provider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 15, 17, 30, 0, TimeSpan.Zero),
            FixedTimeProvider.CreatePacificLikeZone());
        var card = new PocketWatchCard(
            new PocketWatchConfig
            {
                TimeSource = "frozen",
                FrozenTime = "1986-06-15T10:30:00",
                TimezoneOffset = -300,
            },
            provider);

        Assert.Multiple(() =>
        {
            Assert.That(card.UseHostTime, Is.False);
            Assert.That(card.CurrentTime.Year, Is.EqualTo(1986));
            Assert.That(card.CurrentTime.Month, Is.EqualTo(6));
            Assert.That(card.CurrentTime.Day, Is.EqualTo(15));
            Assert.That(card.CurrentTime.Hour, Is.EqualTo(10));
            Assert.That(card.CurrentTime.Minute, Is.EqualTo(30));
        });
    }

    /// <summary>
    /// Tests that NTP time source falls back to host time.
    /// </summary>
    [Test]
    public void Constructor_NtpTimeSource_FallsBackToHost()
    {
        // Arrange
        var config = new PocketWatchConfig
        {
            TimeSource = "ntp",
            NtpServer = "pool.ntp.org",
        };

        // Act
        var card = new PocketWatchCard(config);

        // Assert (NTP not implemented, falls back to host)
        Assert.That(card.UseHostTime, Is.True);
    }

    /// <summary>
    /// Tests that Reset clears internal state.
    /// </summary>
    [Test]
    public void Reset_ClearsInternalState()
    {
        // Arrange
        var card = new PocketWatchCard();

        // Act - should not throw
        card.Reset();

        // Assert - card still functional
        Assert.That(card.CurrentTime, Is.Not.EqualTo(DateTime.MinValue));
    }

    /// <summary>
    /// Tests that slot number can be set.
    /// </summary>
    [Test]
    public void SlotNumber_CanBeSetAndRetrieved()
    {
        // Arrange
        var card = new PocketWatchCard();

        // Act
        card.SlotNumber = 4;

        // Assert
        Assert.That(card.SlotNumber, Is.EqualTo(4));
    }

    /// <summary>
    /// Tests that ROM region returns correct data.
    /// </summary>
    [Test]
    public void ROMRegion_ReadsIdentificationBytes()
    {
        // Arrange
        var card = new PocketWatchCard();
        var rom = card.ROMRegion!;
        var ctx = CreateTestContext();

        // Act - read identification bytes and the generated stub entries
        var byte0 = rom.Read8(0x00, ctx);
        var byte2 = rom.Read8(0x02, ctx);
        var byte4 = rom.Read8(0x04, ctx);
        var byte6 = rom.Read8(0x06, ctx);
        var readEntry = rom.Read8(0x08, ctx);
        var writeEntry = rom.Read8(0x0B, ctx);
        var slideEnd = rom.Read8(0xFB, ctx);

        // Assert - Thunderclock identification bytes, NOP entries, RTS at $CnFB
        Assert.Multiple(() =>
        {
            Assert.That(byte0, Is.EqualTo(0x08));
            Assert.That(byte2, Is.EqualTo(0x28));
            Assert.That(byte4, Is.EqualTo(0x58));
            Assert.That(byte6, Is.EqualTo(0x70));
            Assert.That(readEntry, Is.EqualTo(0xEA));
            Assert.That(writeEntry, Is.EqualTo(0xEA));
            Assert.That(slideEnd, Is.EqualTo(0x60));
        });
    }

    /// <summary>
    /// Verifies that the discovered pocketwatch factory honors a frozen profile config.
    /// </summary>
    [Test]
    public void Factory_FrozenConfig_UsesConfiguredTime()
    {
        DeviceFactoryRegistry.EnsureInitialized();
        using var document = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00","timezoneOffset":-300}""");

        var factory = DeviceFactoryRegistry.SlotCardFactories["pocketwatch"];
        var card = (PocketWatchCard)factory(null!, document.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(card.UseHostTime, Is.False);
            Assert.That(card.CurrentTime.Year, Is.EqualTo(1986));
            Assert.That(card.CurrentTime.Month, Is.EqualTo(6));
            Assert.That(card.CurrentTime.Day, Is.EqualTo(15));
            Assert.That(card.CurrentTime.Hour, Is.EqualTo(10));
            Assert.That(card.CurrentTime.Minute, Is.EqualTo(30));
        });
    }

    /// <summary>
    /// Verifies that a null profile config keeps the host-time default.
    /// </summary>
    [Test]
    public void Factory_NullConfig_KeepsHostDefault()
    {
        DeviceFactoryRegistry.EnsureInitialized();

        var factory = DeviceFactoryRegistry.SlotCardFactories["pocketwatch"];
        var card = (PocketWatchCard)factory(null!, null);

        Assert.That(card.UseHostTime, Is.True);
    }

    /// <summary>
    /// Verifies that <see cref="PocketWatchCard.FromProfileConfig"/> applies a host timezone offset.
    /// </summary>
    [Test]
    public void FromProfileConfig_TimezoneOffset_AppliesOffset()
    {
        var provider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 15, 17, 30, 0, TimeSpan.Zero),
            FixedTimeProvider.CreatePacificLikeZone());
        using var plusOneHour = JsonDocument.Parse(
            """{"timeSource":"host","timezoneOffset":60}""");
        using var explicitUtc = JsonDocument.Parse(
            """{"timeSource":"host","timezoneOffset":0}""");

        var offsetCard = PocketWatchCard.FromProfileConfig(plusOneHour.RootElement, logger: null, timeProvider: provider);
        var utcCard = PocketWatchCard.FromProfileConfig(explicitUtc.RootElement, logger: null, timeProvider: provider);

        Assert.Multiple(() =>
        {
            Assert.That(offsetCard.UseHostTime, Is.True);
            Assert.That(offsetCard.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 18, 30, 0)));
            Assert.That(utcCard.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 17, 30, 0)));
        });
    }

    /// <summary>
    /// Verifies that a profile which omits <c>timezoneOffset</c>, or sets it to null, uses local time.
    /// </summary>
    [Test]
    public void FromProfileConfig_OmittedOffset_UsesLocalTime()
    {
        var provider = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 16, 6, 30, 0, TimeSpan.Zero),
            FixedTimeProvider.CreatePacificLikeZone());
        using var omitted = JsonDocument.Parse("""{"timeSource":"host"}""");
        using var explicitNull = JsonDocument.Parse("""{"timeSource":"host","timezoneOffset":null}""");

        var omittedCard = PocketWatchCard.FromProfileConfig(omitted.RootElement, logger: null, timeProvider: provider);
        var nullCard = PocketWatchCard.FromProfileConfig(explicitNull.RootElement, logger: null, timeProvider: provider);

        Assert.Multiple(() =>
        {
            Assert.That(omittedCard.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 23, 30, 0)));
            Assert.That(nullCard.CurrentTime, Is.EqualTo(new DateTime(2026, 7, 15, 23, 30, 0)));
        });
    }

    /// <summary>
    /// Verifies that a missing or non-object profile config keeps the defaults.
    /// </summary>
    [Test]
    public void FromProfileConfig_MissingConfig_KeepsHostDefault()
    {
        using var document = JsonDocument.Parse("\"frozen\"");

        var missing = PocketWatchCard.FromProfileConfig(null);
        var notAnObject = PocketWatchCard.FromProfileConfig(document.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(missing.UseHostTime, Is.True);
            Assert.That(notAnObject.UseHostTime, Is.True);
        });
    }

    /// <summary>
    /// Verifies that a config object with the wrong property types keeps the defaults.
    /// </summary>
    [Test]
    public void FromProfileConfig_WrongTypedConfig_UsesDefaults()
    {
        using var offsetDocument = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":"1986-06-15T10:30:00","timezoneOffset":"five"}""");
        using var frozenDocument = JsonDocument.Parse(
            """{"timeSource":"frozen","frozenTime":15}""");

        var offsetCard = PocketWatchCard.FromProfileConfig(offsetDocument.RootElement);
        var frozenCard = PocketWatchCard.FromProfileConfig(frozenDocument.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(offsetCard.UseHostTime, Is.True);
            Assert.That(frozenCard.UseHostTime, Is.True);
        });
    }

    /// <summary>
    /// Verifies that the discovered factory logs a warning and keeps the defaults for an unreadable config.
    /// </summary>
    [Test]
    public void Factory_WrongTypedConfig_LogsWarningAndUsesDefaults()
    {
        DeviceFactoryRegistry.EnsureInitialized();
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger();
        var previous = DeviceFactoryRegistry.LoggerFactory;
        DeviceFactoryRegistry.LoggerFactory = _ => logger;
        try
        {
            using var document = JsonDocument.Parse(
                """{"timeSource":"frozen","timezoneOffset":"five"}""");
            var factory = DeviceFactoryRegistry.SlotCardFactories["pocketwatch"];
            var card = (PocketWatchCard)factory(null!, document.RootElement);

            Assert.Multiple(() =>
            {
                Assert.That(card.UseHostTime, Is.True);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].Level, Is.EqualTo(LogEventLevel.Warning));
                Assert.That(events[0].Exception, Is.InstanceOf<JsonException>());
            });
        }
        finally
        {
            DeviceFactoryRegistry.LoggerFactory = previous;
        }
    }

    /// <summary>
    /// Tests that I/O handlers are configured.
    /// </summary>
    [Test]
    public void IOHandlers_AreConfigured()
    {
        // Arrange
        var card = new PocketWatchCard();

        // Assert
        Assert.That(card.IOHandlers, Is.Not.Null);
    }

    /// <summary>
    /// Tests that null config throws ArgumentNullException.
    /// </summary>
    [Test]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.That(
            () => new PocketWatchCard(null!),
            Throws.ArgumentNullException);
    }

    /// <summary>
    /// Helper method to create a test bus access context.
    /// </summary>
    private static BusAccess CreateTestContext()
    {
        return new(
            Address: 0xC400,
            Value: 0,
            WidthBits: 8,
            Mode: BusAccessMode.Decomposed,
            EmulationFlag: true,
            Intent: AccessIntent.DataRead,
            SourceId: 0,
            Cycle: 0,
            Flags: AccessFlags.None);
    }

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}