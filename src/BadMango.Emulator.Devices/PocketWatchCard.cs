// <copyright file="PocketWatchCard.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Interfaces;

using Serilog;

/// <summary>
/// PocketWatch slot card - a Thunderclock-compatible real-time clock.
/// </summary>
/// <remarks>
/// <para>
/// The PocketWatch card provides ProDOS-compatible time services to the emulated system.
/// It is compatible with the Thunderclock Plus protocol, allowing existing software
/// to access the current date and time.
/// </para>
/// <para>
/// The card supports multiple time sources:
/// <list type="bullet">
/// <item><description>"host" - Uses the host system's clock with timezone offset</description></item>
/// <item><description>"frozen" - Uses a static datetime from configuration</description></item>
/// <item><description>"ntp" - Uses NTP server polling (not yet implemented)</description></item>
/// </list>
/// </para>
/// <para>
/// ProDOS calls <c>JSR $Cn0B</c> (WRITE) to latch the time and <c>JSR $Cn08</c> (READ)
/// to deposit <c>mo,da,dt,hr,mn</c> at <c>$0200</c>: month, weekday (Sunday is 00),
/// day, hour, and minute. Digits are high-bit ASCII <c>$B0</c>-<c>$B9</c> and commas
/// are <c>$AC</c>, the form ProDOS 8 <c>TCLOCK.S</c> decodes (<c>AND #$07</c>, then
/// <c>SBC #$B0</c>). Those entries are slot-dependent call traps, so the trap registry
/// suppresses them while INTCXROM maps internal ROM over the slot. The slot image stays
/// the generated stub.
/// </para>
/// </remarks>
[DeviceType("pocketwatch")]
public sealed class PocketWatchCard : IClockDevice, IDisposable
{
    /// <summary>
    /// Offset of the ProDOS READ entry within the slot ROM page ($Cn08).
    /// </summary>
    private const uint ReadEntryOffset = 0x08;

    /// <summary>
    /// Offset of the ProDOS WRITE entry within the slot ROM page ($Cn0B).
    /// </summary>
    private const uint WriteEntryOffset = 0x0B;

    /// <summary>
    /// ProDOS clock-driver output buffer.
    /// </summary>
    private const Addr ProDosTimeBuffer = 0x0200;

    /// <summary>
    /// Cycles charged for a handled clock entry, before the CPU's RTS.
    /// </summary>
    private const ulong ClockTrapCycles = 6;

    /// <summary>
    /// Bit 7 of Apple II high-bit ASCII. ProDOS <c>TCLOCK.S</c> expects digits <c>$B0</c>-<c>$B9</c>
    /// and commas <c>$AC</c>.
    /// </summary>
    private const byte HighAsciiBit = 0x80;

    /// <summary>
    /// The default frozen time when no valid frozen time is specified.
    /// </summary>
    private static readonly DateTime DefaultFrozenTime = new(1986, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SlotIOHandlers handlers = new();
    private readonly IBusTarget romRegion;
    private readonly PocketWatchConfig config;
    private readonly byte[] timeData = new byte[8];
    private DateTime fixedTime;
    private bool useHostTime = true;
    private int timezoneOffsetMinutes;
    private int slotNumber;
    private int readIndex;
    private bool clockTrapsRegistered;
    private bool timeLatched;
    private IEventContext? eventContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="PocketWatchCard"/> class with default configuration.
    /// </summary>
    public PocketWatchCard()
        : this(new())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PocketWatchCard"/> class with specified configuration.
    /// </summary>
    /// <param name="config">The PocketWatch configuration.</param>
    public PocketWatchCard(PocketWatchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;

        // Configure time source
        ConfigureTimeSource();

        // Set up I/O handlers (Thunderclock protocol)
        handlers.Set(0x00, ReadClockData, WriteClockControl);
        handlers.Set(0x01, ReadClockStatus, null);

        // Create ROM with identification bytes
        romRegion = new PocketWatchRom();
    }

    // ─── IPeripheral ────────────────────────────────────────────────────

    /// <inheritdoc />
    public string Name => "PocketWatch";

    /// <inheritdoc />
    public string DeviceType => "PocketWatch";

    /// <inheritdoc />
    public PeripheralKind Kind => PeripheralKind.SlotCard;

    // ─── ISlotCard ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public int SlotNumber
    {
        get => slotNumber;
        set
        {
            slotNumber = value;
            RegisterClockTraps();
        }
    }

    /// <inheritdoc />
    public SlotIOHandlers? IOHandlers => handlers;

    /// <inheritdoc />
    public IBusTarget? ROMRegion => romRegion;

    /// <inheritdoc />
    public IBusTarget? ExpansionROMRegion => null;

    // ─── IClockDevice ───────────────────────────────────────────────────

    /// <inheritdoc />
    public DateTime CurrentTime
    {
        get
        {
            if (useHostTime)
            {
                return DateTimeOffset.UtcNow.AddMinutes(timezoneOffsetMinutes).DateTime;
            }

            return fixedTime;
        }
    }

    /// <inheritdoc />
    public bool UseHostTime
    {
        get => useHostTime;
        set => useHostTime = value;
    }

    /// <summary>
    /// Creates a PocketWatch card from a profile card <c>config</c> object.
    /// </summary>
    /// <param name="cardConfig">
    /// The <c>devices.slots.cards[].config</c> object, or <see langword="null"/> when the profile omits it.
    /// </param>
    /// <param name="logger">
    /// Optional logger for a config object that cannot be read. Null and non-object values are not logged.
    /// </param>
    /// <returns>
    /// A card configured from <paramref name="cardConfig"/>. A null, non-object, or unreadable value keeps the defaults.
    /// </returns>
    public static PocketWatchCard FromProfileConfig(JsonElement? cardConfig, ILogger? logger = null)
    {
        if (cardConfig is not { ValueKind: JsonValueKind.Object } element)
        {
            return new();
        }

        try
        {
            PocketWatchConfig parsed = element.Deserialize<PocketWatchConfig>() ?? new();
            return new(parsed);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            logger?.Warning(
                ex,
                "PocketWatch card config could not be read. Using the default clock settings.");
            return new();
        }
    }

    /// <inheritdoc />
    public void SetFixedTime(DateTime time)
    {
        fixedTime = time;
        useHostTime = false;
    }

    // ─── IScheduledDevice ───────────────────────────────────────────────

    /// <inheritdoc />
    public void Initialize(IEventContext context)
    {
        eventContext = context;
        RegisterClockTraps();
    }

    /// <inheritdoc />
    public void Reset()
    {
        readIndex = 0;
        timeLatched = false;
    }

    /// <inheritdoc />
    public void OnExpansionROMSelected()
    {
        // No expansion ROM
    }

    /// <inheritdoc />
    public void OnExpansionROMDeselected()
    {
        // No expansion ROM
    }

    /// <summary>
    /// Detaches the card from the machine that installed it.
    /// </summary>
    /// <remarks>
    /// <see cref="Machine.RemoveSlotCard"/> calls this after the slot's traps are unregistered.
    /// Clearing the registration flag lets the same instance register again on a later insert.
    /// </remarks>
    public void Dispose()
    {
        clockTrapsRegistered = false;
        eventContext = null;
        timeLatched = false;
        readIndex = 0;
    }

    private static byte ToHighAscii(byte plain)
        => (byte)(plain | HighAsciiBit);

    private static void WriteTwoDigits(ICpu cpu, Addr address, int value)
    {
        int clamped = Math.Clamp(value, 0, 99);
        cpu.Write8(address, ToHighAscii((byte)('0' + (clamped / 10))));
        cpu.Write8(address + 1, ToHighAscii((byte)('0' + (clamped % 10))));
    }

    private void ConfigureTimeSource()
    {
        timezoneOffsetMinutes = config.TimezoneOffset;

        switch (config.TimeSource.ToLowerInvariant())
        {
            case "host":
                useHostTime = true;
                break;

            case "frozen":
                useHostTime = false;
                fixedTime = !string.IsNullOrEmpty(config.FrozenTime) &&
                    DateTime.TryParse(config.FrozenTime, out var parsedTime)
                        ? parsedTime
                        : DefaultFrozenTime;
                break;

            case "ntp":
                // NTP support is a future enhancement
                // For now, fall back to host time
                useHostTime = true;
                break;

            default:
                useHostTime = true;
                break;
        }
    }

    private byte ReadClockData(byte offset, in BusAccess context)
    {
        // Latch current time on first read
        if (readIndex == 0)
        {
            LatchTime();
        }

        byte value = timeData[readIndex];
        readIndex = (readIndex + 1) % timeData.Length;
        return value;
    }

    private byte ReadClockStatus(byte offset, in BusAccess context)
    {
        // Status: bit 7 = data ready
        return 0x80;
    }

    private void WriteClockControl(byte offset, byte value, in BusAccess context)
    {
        // Reset read index on any write
        readIndex = 0;
    }

    private void LatchTime()
    {
        var time = CurrentTime;

        // Thunderclock format (ProDOS compatible):
        // Byte 0: Month (1-12)
        // Byte 1: Day of week (0=Sunday)
        // Byte 2: Day of month (1-31)
        // Byte 3: Hour (0-23)
        // Byte 4: Minute (0-59)
        // Byte 5: Second (0-59)
        // Byte 6: Year low byte (since 1900)
        // Byte 7: Year high byte
        int year = time.Year - 1900;
        timeData[0] = (byte)time.Month;
        timeData[1] = (byte)time.DayOfWeek;
        timeData[2] = (byte)time.Day;
        timeData[3] = (byte)time.Hour;
        timeData[4] = (byte)time.Minute;
        timeData[5] = (byte)time.Second;
        timeData[6] = (byte)(year & 0xFF);
        timeData[7] = (byte)((year >> 8) & 0xFF);
        timeLatched = true;
    }

    /// <summary>
    /// Registers the ProDOS READ and WRITE call traps once the slot number and machine context are both known.
    /// </summary>
    /// <remarks>
    /// Profile loading calls <see cref="Initialize"/> before the slot manager assigns
    /// <see cref="SlotNumber"/>. Runtime insertion assigns the slot first. Either order
    /// registers exactly once.
    /// </remarks>
    private void RegisterClockTraps()
    {
        if (clockTrapsRegistered || eventContext is null || slotNumber is < 1 or > 7)
        {
            return;
        }

        ITrapRegistry? registry = eventContext.GetComponent<ITrapRegistry>();
        if (registry is null)
        {
            return;
        }

        Addr slotBase = 0xC000u + ((uint)slotNumber << 8);
        registry.RegisterSlotDependent(
            slotBase + WriteEntryOffset,
            slotNumber,
            "PW_WRITE",
            TrapCategory.SlotFirmware,
            HandleWriteEntry,
            "PocketWatch WRITE entry: latch the time and return.");
        registry.RegisterSlotDependent(
            slotBase + ReadEntryOffset,
            slotNumber,
            "PW_READ",
            TrapCategory.SlotFirmware,
            HandleReadEntry,
            "PocketWatch READ entry: write high-bit mo,da,dt,hr,mn at $0200 and return.");
        clockTrapsRegistered = true;
    }

    private TrapResult HandleWriteEntry(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(context);
        LatchTime();
        return TrapResult.Success(new Cycle(ClockTrapCycles));
    }

    private TrapResult HandleReadEntry(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(context);
        if (!timeLatched)
        {
            LatchTime();
        }

        WriteProDosTimeString(cpu);
        return TrapResult.Success(new Cycle(ClockTrapCycles));
    }

    private void WriteProDosTimeString(ICpu cpu)
    {
        WriteTwoDigits(cpu, ProDosTimeBuffer, timeData[0]);
        cpu.Write8(ProDosTimeBuffer + 2, ToHighAscii((byte)','));
        WriteTwoDigits(cpu, ProDosTimeBuffer + 3, timeData[1]);
        cpu.Write8(ProDosTimeBuffer + 5, ToHighAscii((byte)','));
        WriteTwoDigits(cpu, ProDosTimeBuffer + 6, timeData[2]);
        cpu.Write8(ProDosTimeBuffer + 8, ToHighAscii((byte)','));
        WriteTwoDigits(cpu, ProDosTimeBuffer + 9, timeData[3]);
        cpu.Write8(ProDosTimeBuffer + 11, ToHighAscii((byte)','));
        WriteTwoDigits(cpu, ProDosTimeBuffer + 12, timeData[4]);
    }
}