// <copyright file="ProDosClockTests.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Devices.GuestOs;
using BadMango.Emulator.Devices.Tests.GuestOs;
using BadMango.Unit.Components;

/// <summary>
/// DATE/TIME packing and the synthetic global-page checks for the slotless ProDOS clock.
/// </summary>
[TestFixture]
public class ProDosClockTests
{
    /// <summary>
    /// Verifies the DATE/TIME bit layout at the year boundaries and on 29 February.
    /// </summary>
    /// <param name="year">Civil year. The stored year is <c>year % 100</c>.</param>
    /// <param name="month">Month, 1–12.</param>
    /// <param name="day">Day of the month.</param>
    /// <param name="hour">Hour, 0–23.</param>
    /// <param name="minute">Minute, 0–59.</param>
    [TestCase(1999, 12, 31, 23, 59)]
    [TestCase(2000, 1, 1, 0, 0)]
    [TestCase(2039, 12, 31, 23, 59)]
    [TestCase(2024, 2, 29, 12, 0)]
    public void Pack_WritesDateTimeLayout(int year, int month, int day, int hour, int minute)
    {
        var time = new DateTime(year, month, day, hour, minute, 0);
        ProDosDateTime packed = ProDosDateTime.Pack(time);
        int date = ((year % 100) << 9) | (month << 5) | day;

        Assert.Multiple(() =>
        {
            Assert.That(packed.DateLo, Is.EqualTo((byte)date));
            Assert.That(packed.DateHi, Is.EqualTo((byte)(date >> 8)));
            Assert.That(packed.Minute, Is.EqualTo((byte)minute));
            Assert.That(packed.Hour, Is.EqualTo((byte)hour));
            Assert.That(packed.Minute & 0xC0, Is.EqualTo(0), "Minute bits 7–6 stay clear.");
            Assert.That(packed.Hour & 0xE0, Is.EqualTo(0), "Hour bits 7–5 stay clear.");
        });
    }

    /// <summary>
    /// Verifies that a null offset uses the provider's local time and a value is UTC plus minutes.
    /// </summary>
    [Test]
    public void HostTime_NullOffsetUsesLocal_ValueIsUtcPlusMinutes()
    {
        TimeZoneInfo zone = FixedTimeProvider.CreatePacificLikeZone();
        var utc = new DateTimeOffset(2026, 7, 16, 6, 30, 0, TimeSpan.Zero);
        DateTime local = TimeZoneInfo.ConvertTime(utc, zone).DateTime;
        var provider = new FixedTimeProvider(utc, zone);

        var localClock = new ProDosClockDevice(new PocketWatchConfig { TimeSource = "host" }, provider);
        var utcClock = new ProDosClockDevice(
            new PocketWatchConfig { TimeSource = "host", TimezoneOffset = 0 },
            provider);
        var offsetClock = new ProDosClockDevice(
            new PocketWatchConfig { TimeSource = "host", TimezoneOffset = -300 },
            provider);
        var frozen = new ProDosClockDevice(new PocketWatchConfig
        {
            TimeSource = "frozen",
            FrozenTime = "1986-06-15T10:30:00",
            TimezoneOffset = -300,
        });

        Assert.Multiple(() =>
        {
            Assert.That(local.Day, Is.EqualTo(15));
            Assert.That(local.Hour, Is.EqualTo(23));
            Assert.That(localClock.CurrentTime, Is.EqualTo(local));
            Assert.That(utcClock.CurrentTime, Is.EqualTo(utc.DateTime));
            Assert.That(offsetClock.CurrentTime, Is.EqualTo(utc.AddMinutes(-300).DateTime));
            Assert.That(frozen.UseHostTime, Is.False);
            Assert.That(frozen.CurrentTime, Is.EqualTo(new DateTime(1986, 6, 15, 10, 30, 0)));
        });
    }

    /// <summary>
    /// Verifies that each global-page check fails on its own, and that JMP is not the slotless case.
    /// </summary>
    [Test]
    public void IsSlotlessDateTimePage_EachCheckFailsAlone_JmpDefers()
    {
        var missingJmp = new GuestMemoryImage();
        missingJmp.PlantProDos(0x24);
        missingJmp.Poke(0xBF00, 0x00);

        var badOpcode = new GuestMemoryImage();
        badOpcode.PlantProDos(0x24, dateTimeOpcode: 0xEA);

        var lowDriver = new GuestMemoryImage();
        lowDriver.PlantProDos(0x24, driver: 0xCFFF);

        var unknownVersion = new GuestMemoryImage();
        unknownVersion.PlantProDos(0x25);

        var cardDriver = new GuestMemoryImage();
        cardDriver.PlantProDos(0x24, dateTimeOpcode: 0x4C);

        var noCard = new GuestMemoryImage();
        noCard.PlantProDos(0x24, dateTimeOpcode: 0x60);

        Assert.Multiple(() =>
        {
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(missingJmp), Is.False);
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(badOpcode), Is.False);
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(lowDriver), Is.False);
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(unknownVersion), Is.False);
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(cardDriver), Is.False);
            Assert.That(ProDosClockDevice.IsSlotlessDateTimePage(noCard), Is.True);
            Assert.That(ProDosGlobalPage.Inspect(cardDriver).IsKnownProDos8, Is.True);
        });
    }

    /// <summary>
    /// Verifies that the discovered motherboard factory includes <c>prodos-clock</c>.
    /// </summary>
    [Test]
    public void Factory_DiscoversProDosClock()
    {
        DeviceFactoryRegistry.EnsureInitialized();

        Assert.That(DeviceFactoryRegistry.MotherboardDeviceFactories, Does.ContainKey("prodos-clock"));
        Assert.That(
            DeviceFactoryRegistry.SkippedDeviceTypes.Values,
            Does.Not.Contain("prodos-clock"));
    }
}