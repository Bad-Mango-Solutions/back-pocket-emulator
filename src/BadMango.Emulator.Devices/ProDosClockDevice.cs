// <copyright file="ProDosClockDevice.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

using System.Text.Json;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Configuration;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.GuestOs;

using Interfaces;

using Serilog;

/// <summary>
/// Slotless ProDOS 8 clock. It answers <c>DATETIME</c> at <c>$BF06</c> when no clock card won.
/// </summary>
/// <remarks>
/// <para>
/// The device is off unless the profile lists <c>prodos-clock</c>. It installs its call trap
/// only after guest-OS detection reports a ProDOS 8 session (<c>guestOs.detect</c> or
/// <c>guestOs.bootLog</c>). ProDOS stores <c>RTS</c> (<c>$60</c>) at <c>$BF06</c> when it finds
/// no clock card, and <c>JMP</c> (<c>$4C</c>) when a card won. The card wins: a <c>JMP</c> is not
/// trapped, so the installed driver runs. A stored <c>RTS</c> is handled on the next session
/// sync, which is detection or the boot monitor's MLI call. The CPU stores that byte on the RAM
/// fast path, which does not raise a write trap.
/// </para>
/// <para>
/// The handled call writes <c>DATE</c>/<c>TIME</c> at <c>$BF90</c>–<c>$BF93</c> (<c>year % 100</c>)
/// and sets <c>MACHID</c> bit 0. It does not write <c>$0200</c>. Auxiliary <c>RAMRD</c> returns
/// not-handled so the aux byte at <c>$BF06</c> executes.
/// </para>
/// </remarks>
[DeviceType("prodos-clock")]
public sealed class ProDosClockDevice : IMotherboardDevice, IClockSource, IProfileConfiguredDevice, IMachineBuiltCallback, IResetReceiver, IDisposable
{
    /// <summary>
    /// Cycles charged when <c>DATETIME</c> is handled, before the CPU performs <c>RTS</c>.
    /// </summary>
    /// <remarks>
    /// The CPU then adds the six cycles of <c>RTS</c>. The step that executes a handled
    /// <c>$BF06</c> therefore costs <see cref="DateTimeTrapCycles"/> plus 6. PocketWatch clock
    /// entries use the same split.
    /// </remarks>
    public const ulong DateTimeTrapCycles = 6;

    private const string DateTimeTrapName = "ProDOS DATETIME";

    private const string MliObserverName = "ProDOS MLI";

    private readonly ILogger logger;
    private readonly TimeProvider timeProvider;
    private ConfiguredClock clock;
    private IMachine? host;
    private ITrapRegistry? registry;
    private IGuestMemoryReader? reader;
    private IGuestOsDetector? detector;
    private BootSequenceMonitor? monitor;
    private Extended80ColumnDevice? extended80;
    private AuxiliaryMemoryController? auxMemory;
    private bool dateTimeTrapRegistered;
    private bool mliObserverRegistered;
    private bool machIdStamped;
    private bool deferralLogged;
    private bool clockCardWarned;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProDosClockDevice"/> class with host time.
    /// </summary>
    /// <param name="logger">
    /// Logger for the card-present warning and the one deferral line.
    /// <see langword="null"/> discards log events.
    /// </param>
    public ProDosClockDevice(ILogger? logger = null)
        : this(new PocketWatchConfig(), null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProDosClockDevice"/> class.
    /// </summary>
    /// <param name="config">Time source. The same fields as a PocketWatch card.</param>
    /// <param name="timeProvider">
    /// Clock used for host time. <see langword="null"/> uses <see cref="TimeProvider.System"/>.
    /// </param>
    /// <param name="logger">
    /// Logger for the card-present warning and the one deferral line.
    /// <see langword="null"/> discards log events.
    /// </param>
    /// <exception cref="ArgumentNullException">If <paramref name="config"/> is <see langword="null"/>.</exception>
    public ProDosClockDevice(PocketWatchConfig config, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.logger = (logger ?? Serilog.Core.Logger.None).ForContext<ProDosClockDevice>();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        clock = new ConfiguredClock(config, this.timeProvider);
    }

    /// <inheritdoc />
    public string Name => "ProDOS Clock";

    /// <inheritdoc />
    public string DeviceType => "ProDosClock";

    /// <inheritdoc />
    public PeripheralKind Kind => PeripheralKind.Motherboard;

    /// <inheritdoc />
    public DateTime CurrentTime => clock.CurrentTime;

    /// <inheritdoc />
    public bool UseHostTime
    {
        get => clock.UseHostTime;
        set => clock.UseHostTime = value;
    }

    /// <summary>
    /// Reports whether <paramref name="memory"/> is a ProDOS 8 global page whose <c>DATETIME</c>
    /// opcode is <c>RTS</c>, the no-card case this device handles.
    /// </summary>
    /// <param name="memory">Guest memory. The read has no soft-switch side effects.</param>
    /// <returns>
    /// <see langword="true"/> when the page is ProDOS 8 through 2.4.3 and <c>$BF06</c> is <c>$60</c>.
    /// A <c>JMP</c> at <c>$BF06</c> is a known page whose driver belongs to a clock card.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    public static bool IsSlotlessDateTimePage(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ProDosGlobalPage.Inspection page = ProDosGlobalPage.Inspect(memory);
        return page.IsKnownProDos8 && page.DateTimeOpcode == ProDosGlobalPage.Rts;
    }

    /// <inheritdoc />
    public void ApplyProfileConfig(JsonElement? config)
    {
        clock = new ConfiguredClock(ParseConfig(config), timeProvider);
    }

    /// <inheritdoc />
    public void Initialize(IEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        registry = context.GetComponent<ITrapRegistry>();
        if (context is IMachine machine)
        {
            Bind(machine);
        }
    }

    /// <inheritdoc />
    public void RegisterHandlers(IOPageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
    }

    /// <inheritdoc />
    public void Reset()
    {
        machIdStamped = false;
        deferralLogged = false;
        SyncGuestOsSession();
    }

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
        machIdStamped = false;
        SyncGuestOsSession();
    }

    /// <summary>
    /// Subscribes to guest-OS detection and the boot monitor, then syncs the session trap.
    /// </summary>
    /// <param name="machine">The machine whose detector, monitor, and trap registry are used.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Safe to call again. A detector or monitor that appears later, such as <c>os on</c>, is picked up.
    /// </remarks>
    public void Bind(IMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        if (disposed)
        {
            return;
        }

        host = machine;
        registry ??= machine.GetComponent<ITrapRegistry>();
        extended80 ??= machine.GetComponent<Extended80ColumnDevice>();
        auxMemory ??= machine.GetComponent<AuxiliaryMemoryController>();
        reader ??= new CpuGuestMemoryReader(machine.Cpu);
        AttachDetector(machine);
        AttachMonitor(machine);
        WarnIfClockCard(machine);
        SyncGuestOsSession();
    }

    /// <inheritdoc />
    public void OnMachineBuilt(IMachine machine) => Bind(machine);

    /// <inheritdoc />
    public void SetFixedTime(DateTime time) => clock.SetFixedTime(time);

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        UpdateGuestOsSessionTrap(installDateTime: false);
        if (detector is not null)
        {
            detector.Changed -= OnGuestOsChanged;
        }

        if (monitor is not null)
        {
            monitor.StageChanged -= OnStageChanged;
            monitor.SystemCallObserved -= OnSystemCall;
        }
    }

    /// <summary>
    /// Installs or drops the guest-OS-session traps for the current ProDOS session.
    /// </summary>
    /// <param name="installDateTime">
    /// <see langword="true"/> to register the <c>$BF06</c> DATETIME trap.
    /// <see langword="false"/> to drop it. The MLI observer is decided separately.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the only method that registers or unregisters the slotless clock. Both traps are
    /// guest-OS-session traps and fixed-address call traps, so removing a slot card does not touch
    /// them. <c>$BF06</c> is installed when ProDOS 8 is the current session and <c>DATETIME</c> is
    /// <c>RTS</c>. <c>$BF00</c> is an observe-only MLI entry, installed only when the boot monitor
    /// is absent (<c>guestOs.detect</c> without <c>bootLog</c>). The monitor owns <c>$BF00</c> when
    /// it is present; a second registration at that key throws.
    /// </para>
    /// <para>
    /// #702 adds <c>TrapScope.GuestOsSession</c> and unloads that scope on a cold start or a
    /// user reboot. After that merge, pass <c>TrapScope.GuestOsSession</c> on both register calls
    /// below. The unregister calls can go away once the scope unload covers them. Do not register
    /// these addresses anywhere else.
    /// </para>
    /// </remarks>
    private void UpdateGuestOsSessionTrap(bool installDateTime)
    {
        if (registry is null)
        {
            return;
        }

        if (installDateTime)
        {
            if (!dateTimeTrapRegistered)
            {
                registry.Register(
                    ProDosGlobalPage.DateTime,
                    DateTimeTrapName,
                    TrapCategory.OperatingSystem,
                    HandleDateTime,
                    "Slotless ProDOS DATETIME. Writes DATE/TIME at $BF90-$BF93 and returns. A clock card's JMP is not registered.");
                dateTimeTrapRegistered = true;
            }
        }
        else if (dateTimeTrapRegistered)
        {
            registry.Unregister(ProDosGlobalPage.DateTime);
            dateTimeTrapRegistered = false;
        }

        // The boot monitor owns $BF00 when boot logging is on. Detect-only sessions need this
        // observer so the first MLI call can stamp MACHID and notice a fast-path RTS store.
        bool observeMli = !disposed && IsProDosSession() && monitor is null;
        if (observeMli)
        {
            if (!mliObserverRegistered)
            {
                registry.Register(
                    ProDosGlobalPage.MliEntry,
                    MliObserverName,
                    TrapCategory.OperatingSystem,
                    ObserveMli,
                    "Observe-only ProDOS MLI entry. The call is not handled.");
                mliObserverRegistered = true;
            }
        }
        else if (mliObserverRegistered)
        {
            registry.Unregister(ProDosGlobalPage.MliEntry);
            mliObserverRegistered = false;
        }
    }

    private void SyncGuestOsSession()
    {
        if (disposed || reader is null)
        {
            return;
        }

        bool install = IsProDosSession() && IsSlotlessDateTimePage(reader);
        UpdateGuestOsSessionTrap(install);
        if (!install && IsProDosSession() && DeferringToClockCard(reader))
        {
            LogDeferralOnce();
        }
    }

    private void OnGuestOsChanged(object? sender, GuestOsChangedEventArgs args)
    {
        SyncGuestOsSession();
    }

    private void OnStageChanged(object? sender, GuestOsBootStageChangedEventArgs args)
    {
        SyncGuestOsSession();
    }

    private void OnSystemCall(object? sender, GuestOsSystemCallEventArgs args)
    {
        if (disposed || args.Os != GuestOsId.ProDos8)
        {
            return;
        }

        // ProDOS may store RTS at $BF06 after the session is published and before this MLI
        // call reaches DATETIME. Re-read the opcode here; the store itself is a fast-path write.
        SyncGuestOsSession();
        if (host is not null)
        {
            StampMachIdOnMli(host.Cpu);
        }
    }

    private TrapResult ObserveMli(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        SyncGuestOsSession();
        StampMachIdOnMli(cpu);
        return TrapResult.NotHandled;
    }

    private void StampMachIdOnMli(ICpu cpu)
    {
        if (machIdStamped || !dateTimeTrapRegistered)
        {
            return;
        }

        machIdStamped = true;
        SetMachIdClockBit(cpu);
    }

    private bool IsProDosSession()
    {
        if (detector is null || detector.Current.Id != GuestOsId.ProDos8)
        {
            return false;
        }

        if (monitor is null)
        {
            return true;
        }

        return monitor.Stage == GuestOsBootStage.Ready;
    }

    private void AttachDetector(IMachine machine)
    {
        if (detector is not null)
        {
            return;
        }

        if (machine.GetComponent<IGuestOsDetector>() is not IGuestOsDetector found)
        {
            return;
        }

        detector = found;
        detector.Changed += OnGuestOsChanged;
    }

    private void AttachMonitor(IMachine machine)
    {
        if (monitor is not null)
        {
            return;
        }

        if (machine.GetComponent<BootSequenceMonitor>() is not BootSequenceMonitor found)
        {
            return;
        }

        monitor = found;
        monitor.StageChanged += OnStageChanged;
        monitor.SystemCallObserved += OnSystemCall;
    }

    private void WarnIfClockCard(IMachine machine)
    {
        if (clockCardWarned)
        {
            return;
        }

        ISlotManager? slots = machine.GetComponent<ISlotManager>();
        if (slots is null)
        {
            return;
        }

        foreach (ISlotCard card in slots.Slots.Values)
        {
            if (card is not IClockDevice)
            {
                continue;
            }

            clockCardWarned = true;
            logger.Warning(
                "prodos-clock is configured alongside a clock card. The card answers DATETIME when ProDOS installs its driver.");
            return;
        }
    }

    private bool DeferringToClockCard(IGuestMemoryReader memory)
    {
        ProDosGlobalPage.Inspection page = ProDosGlobalPage.Inspect(memory);
        return page.IsKnownProDos8 && page.DateTimeOpcode == ProDosGlobalPage.Jmp;
    }

    private void LogDeferralOnce()
    {
        if (deferralLogged)
        {
            return;
        }

        deferralLogged = true;
        logger.Information("Slotless ProDOS clock deferring to slot clock card.");
    }

    private TrapResult HandleDateTime(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        if (ReadingAuxiliaryRam() || !SlotlessPage(cpu))
        {
            return TrapResult.NotHandled;
        }

        ProDosDateTime.Write(cpu, clock.CurrentTime);
        SetMachIdClockBit(cpu);
        return TrapResult.Success(new Cycle(DateTimeTrapCycles));
    }

    private bool ReadingAuxiliaryRam()
        => (extended80 is not null && extended80.IsRamRdEnabled)
            || (auxMemory is not null && auxMemory.IsRamRdEnabled);

    private bool SlotlessPage(ICpu cpu)
    {
        if (cpu.Peek8(ProDosGlobalPage.MliEntry) != ProDosGlobalPage.Jmp)
        {
            return false;
        }

        if (cpu.Peek8(ProDosGlobalPage.DateTime) != ProDosGlobalPage.Rts)
        {
            return false;
        }

        ushort driver = (ushort)(cpu.Peek8(ProDosGlobalPage.DateTimeVector)
            | (cpu.Peek8((Addr)(ProDosGlobalPage.DateTimeVector + 1u)) << 8));
        if (driver < ProDosGlobalPage.LanguageCardStart)
        {
            return false;
        }

        return ProDosGlobalPage.IsKnownVersion(cpu.Peek8(ProDosGlobalPage.KVersionAddress));
    }

    private void SetMachIdClockBit(ICpu cpu)
    {
        byte machId = cpu.Peek8(ProDosGlobalPage.MachIdAddress);
        cpu.Write8(ProDosGlobalPage.MachIdAddress, (byte)(machId | ProDosGlobalPage.ClockBit));
    }

    private PocketWatchConfig ParseConfig(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } element)
        {
            return new PocketWatchConfig();
        }

        try
        {
            return element.Deserialize<PocketWatchConfig>() ?? new PocketWatchConfig();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            logger.Warning(ex, "prodos-clock config could not be read. Using the default clock settings.");
            return new PocketWatchConfig();
        }
    }
}