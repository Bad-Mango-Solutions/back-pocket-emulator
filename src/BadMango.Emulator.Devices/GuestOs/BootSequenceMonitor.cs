// <copyright file="BootSequenceMonitor.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using System.Globalization;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.Interfaces;

using Serilog;

using KeyboardModifiers = BadMango.Emulator.Devices.KeyboardModifiers;

/// <summary>
/// Records observe-only boot milestones and publishes the guest OS when a boot profile matches.
/// </summary>
/// <remarks>
/// <para>
/// Traps return <see cref="TrapResult.NotHandled"/> and use <see cref="TrapCategory.OperatingSystem"/>.
/// <c>$Cn00</c> is armed for every occupied slot whose card has firmware, on reset and power-on,
/// with <see cref="TrapScope.Machine"/>, and stays armed after the OS is ready so the next boot is visible.
/// <c>$0801</c> is armed after that firmware runs. Later addresses come from
/// <see cref="IGuestOsBootProfile"/>. Boot-progress traps are removed once the boot reaches a terminal stage.
/// A system-call address and <see cref="IGuestOsBootProfile.SessionTraps"/> use
/// <see cref="TrapScope.GuestOsSession"/> and stay until the session ends.
/// </para>
/// <para>
/// A cold start (power-on, hard reset, or Open Apple held when RESET releases) ends the guest OS session:
/// <see cref="GuestOsId.None"/>, <see cref="GuestOsBootStage.Init"/>, a cleared detector cache, a fresh boot log,
/// and every <see cref="TrapScope.GuestOsSession"/> trap unregistered. A user reboot
/// (<see cref="BootMilestoneKind.BootDeviceSelected"/> by <c>PR#6</c>, <c>IN#6</c>, <c>C600G</c>, or any jump to
/// <c>$Cn00</c> while a session is active) does the same, then the new boot runs. A warm reset unloads nothing.
/// The session's traps are attached again at <see cref="GuestOsBootStage.Loader"/>, or at
/// <see cref="GuestOsBootStage.Ready"/> when the detector publishes an OS with no milestone.
/// </para>
/// <para>
/// A boot sector that matches no profile ends at <see cref="GuestOsBootStage.CustomBoot"/>.
/// A recognized boot whose probe does not confirm the profile ends at <see cref="GuestOsBootStage.Unknown"/>.
/// A warm reset logs <see cref="BootMilestoneKind.RomInitEntered"/> and leaves the stage where it is, until slot firmware runs.
/// When no milestone was seen, <see cref="PublishFromDetector"/> asks <see cref="IGuestOsDetector"/>
/// and sets <see cref="GuestOsBootStage.Ready"/> if an operating system is already in memory.
/// </para>
/// </remarks>
public sealed class BootSequenceMonitor : IBootSequenceMonitor, IResetReceiver, IPowerOnReset
{
    private const int MilestoneLimit = 256;
    private const ushort BootSectorBase = 0x0800;
    private const ushort BootSectorAddress = 0x0801;
    private const int BootSectorBytes = 16;
    private const int ShownSectorBytes = 8;
    private const ushort ResetVector = 0xFFFC;
    private const ushort KeyboardData = 0xC000;
    private const int KeyboardPollsBeforeNoDevice = 2;

    private readonly ILogger logger;
    private readonly IGuestOsDetector detector;
    private readonly ProfilePlan[] plans;
    private readonly List<BootMilestone> milestones = [];
    private readonly Dictionary<ushort, int> nextStep = [];
    private readonly Dictionary<ushort, TrapScope> ramTraps = [];
    private readonly List<FirmwareTrap> firmwareTraps = [];

    private IMachine? machine;
    private ITrapRegistry? registry;
    private IGuestMemoryReader? reader;
    private ProfilePlan? active;
    private GuestOsBootStage stage;
    private bool bootSectorClassified;
    private bool systemCallRecorded;
    private bool pendingColdStart;
    private bool openAppleSelection;
    private bool warmMayBoot;
    private bool deviceSelected;
    private bool keyboardTrapArmed;
    private int romKeyboardPolls;
    private ushort resetVector;
    private bool sessionActive;
    private GuestOsId sessionOs;

    /// <summary>
    /// Initializes a new instance of the <see cref="BootSequenceMonitor"/> class.
    /// </summary>
    /// <param name="logger">Boot lines are written at Information with source context GuestOs.</param>
    /// <param name="detector">The probe runner used when a kernel milestone is reached, and for the no-milestone fallback.</param>
    /// <param name="profiles">Boot profiles in match order. The sequence is copied. An empty sequence makes every sector a custom boot.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/>, <paramref name="detector"/>, <paramref name="profiles"/>, or a profile is <see langword="null"/>.</exception>
    public BootSequenceMonitor(ILogger logger, IGuestOsDetector detector, IEnumerable<IGuestOsBootProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(profiles);
        this.logger = logger.ForContext("SourceContext", "GuestOs");
        this.detector = detector;
        var built = new List<ProfilePlan>();
        foreach (var profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            built.Add(new ProfilePlan(profile));
        }

        this.plans = built.ToArray();
    }

    /// <inheritdoc />
    public event EventHandler<GuestOsBootStageChangedEventArgs>? StageChanged;

    /// <inheritdoc />
    public event EventHandler<BootMilestone>? MilestoneReached;

    /// <inheritdoc />
    public event EventHandler<GuestOsSessionEventArgs>? GuestOsSessionStarted;

    /// <inheritdoc />
    public event EventHandler<GuestOsSessionEventArgs>? GuestOsSessionEnded;

    /// <summary>Raised on every entry to a profile's system-call address after that trap is armed.</summary>
    public event EventHandler<GuestOsSystemCallEventArgs>? SystemCallObserved;

    /// <summary>
    /// Raised when a <see cref="IGuestOsBootProfile.SessionTraps"/> address is entered.
    /// </summary>
    /// <remarks>
    /// The handler runs before the entry opcode. Nobody listening means the invoke is skipped,
    /// so a DOS file-manager or RWTS trap stays a null check when the OS call log is off.
    /// </remarks>
    public event GuestOsSessionTrapHandler? SessionTrapEntered;

    /// <inheritdoc />
    public GuestOsBootStage Stage => this.stage;

    /// <inheritdoc />
    public IReadOnlyList<BootMilestone> Milestones => this.milestones;

    /// <summary>Gets how many times <c>$0801</c> was entered after the sector was classified.</summary>
    /// <value>Zero until a classified boot sector is entered again. Reset clears it.</value>
    public int BootSectorReentries { get; private set; }

    /// <summary>Creates the DOS 3.3 and ProDOS 8 profiles in match order.</summary>
    /// <returns>ProDOS 8 first, then DOS 3.3.</returns>
    public static IReadOnlyList<IGuestOsBootProfile> CreateStandardProfiles() =>
        [new ProDos8GuestOsBootProfile(), new Dos33GuestOsBootProfile()];

    /// <summary>Creates a detector that reads through <see cref="CpuGuestMemoryReader"/>.</summary>
    /// <param name="machine">The machine whose CPU is peeked.</param>
    /// <param name="logger">Logger passed to the detector.</param>
    /// <returns>A detector with the ProDOS 8 and DOS 3.3 probes.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public static GuestOsDetector CreateDetector(IMachine machine, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(logger);
        return new GuestOsDetector(
            logger,
            [new ProDos8GuestOsProbe(), new Dos33GuestOsProbe()],
            new CpuGuestMemoryReader(machine.Cpu));
    }

    /// <summary>
    /// Installs the monitor on <paramref name="host"/> and arms <c>$Cn00</c> for boot firmware that is present.
    /// </summary>
    /// <param name="host">The machine whose trap registry and slots are used.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="host"/> is <see langword="null"/>.</exception>
    /// <remarks>A second call is ignored. The machine must already be in the component list for RESET delivery.</remarks>
    public void Attach(IMachine host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (this.machine is not null)
        {
            return;
        }

        this.machine = host;
        this.registry = host.GetComponent<ITrapRegistry>();
        this.reader = new CpuGuestMemoryReader(host.Cpu);
        this.ArmFirmware();
    }

    /// <summary>
    /// Asks the detector what is in memory. When no milestone has been seen and an OS is present, the stage becomes <see cref="GuestOsBootStage.Ready"/>.
    /// </summary>
    /// <returns>The detector's current result.</returns>
    /// <remarks>
    /// This is the slotless, restored-state, and late-attach path. It does not register traps.
    /// A cold reset still returns the stage to <see cref="GuestOsBootStage.Init"/> until firmware is entered again.
    /// </remarks>
    public GuestOsInfo PublishFromDetector()
    {
        var info = this.detector.Detect();
        if (this.stage == GuestOsBootStage.None && this.milestones.Count == 0 && info.Id != GuestOsId.None)
        {
            this.active = this.FindPlan(info.Id);
            this.SetStage(GuestOsBootStage.Ready);
            this.EnsureSession(info.Id, GuestOsBootStage.Ready, armCalls: true);
        }

        return info;
    }

    /// <summary>
    /// Clears the milestone log and returns the boot stage machine to a fresh boot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drops every milestone, the active profile, and the re-entry count, and sets
    /// <see cref="Stage"/> to <see cref="GuestOsBootStage.None"/>. Firmware traps are armed again.
    /// A monitor that had reached a terminal stage had removed them, so the next <c>$Cn00</c> entry
    /// can be recorded. A cold reset then logs <see cref="BootMilestoneKind.RomInitEntered"/> and
    /// moves the stage to <see cref="GuestOsBootStage.Init"/>.
    /// </para>
    /// <para>
    /// RESET calls this. A logged boot or cold reset also calls it when the monitor is already
    /// installed, so the previous boot is dropped before the new one is recorded. The detector's
    /// last result is left as it is.
    /// </para>
    /// </remarks>
    public void PrepareForBoot()
    {
        this.DisarmRam();
        this.DisarmFirmware();
        this.DisarmKeyboardPoll();
        this.ClearBoot();
        this.ArmFirmware();
    }

    /// <inheritdoc />
    public void PowerOnReset()
    {
        this.pendingColdStart = true;
    }

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
        this.logger.Debug("Guest OS boot watch reset at cycle {Cycle}.", cycle.Value);
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
        this.logger.Debug("Guest OS boot watch saw RESET release at cycle {Cycle}.", cycle.Value);
        bool openApple = this.OpenAppleHeld();
        bool cold = this.pendingColdStart || openApple;
        this.pendingColdStart = false;
        ushort vector = this.ReadResetVector();
        this.resetVector = vector;
        if (cold)
        {
            GuestOsId? ended = this.TakeSession(endCachedDetection: true);
            this.PrepareForBoot();
            this.openAppleSelection = openApple;
            this.Record(cycle, BootMilestoneKind.RomInitEntered, vector, this.DescribeInit(vector, cold: true), null);
            this.SetStage(GuestOsBootStage.Init);
            this.ArmKeyboardPoll();
            this.RaiseSessionEnded(ended);
            return;
        }

        this.openAppleSelection = false;
        this.warmMayBoot = true;
        this.deviceSelected = false;
        this.romKeyboardPolls = 0;
        this.Record(cycle, BootMilestoneKind.RomInitEntered, vector, this.DescribeInit(vector, cold: false), null);
        this.ArmFirmware();
        if (this.stage != GuestOsBootStage.Init)
        {
            this.DisarmKeyboardPoll();
        }
    }

    private static byte[] ReadSector(ICpu cpu)
    {
        var sector = new byte[BootSectorBytes];
        for (var i = 0; i < sector.Length; i++)
        {
            sector[i] = cpu.Peek8((uint)(BootSectorBase + i));
        }

        return sector;
    }

    private static string FormatSector(ReadOnlySpan<byte> sector)
    {
        var count = Math.Min(sector.Length, ShownSectorBytes);
        var parts = new string[count];
        for (var i = 0; i < count; i++)
        {
            parts[i] = sector[i].ToString("X2", CultureInfo.InvariantCulture);
        }

        return string.Join(' ', parts);
    }

    private static ushort ReadReturnTarget(ICpu cpu) => (ushort)(ProDosMliCall.ReadStackedReturn(cpu) + 1u);

    private void ClearBoot()
    {
        this.SetStage(GuestOsBootStage.None);
        this.milestones.Clear();
        this.active = null;
        this.BootSectorReentries = 0;
        this.bootSectorClassified = false;
        this.systemCallRecorded = false;
        this.openAppleSelection = false;
        this.warmMayBoot = false;
        this.deviceSelected = false;
        this.romKeyboardPolls = 0;
        this.nextStep.Clear();
    }

    private void ArmFirmware()
    {
        this.DisarmFirmware();
        if (this.registry is null || this.machine?.GetComponent<ISlotManager>() is not ISlotManager slots)
        {
            return;
        }

        for (var slot = 1; slot <= 7; slot++)
        {
            var card = slots.GetCard(slot);
            if (card?.ROMRegion is null)
            {
                continue;
            }

            var address = (ushort)(0xC000 + (slot << 8));
            var capturedSlot = slot;
            var cardName = card.Name;
            this.registry.RegisterSlotDependent(
                address,
                capturedSlot,
                $"Boot firmware slot {capturedSlot}",
                TrapCategory.OperatingSystem,
                (cpu, bus, context) => this.OnFirmware(capturedSlot, cardName, cpu, bus, context),
                "Observe-only boot firmware entry.",
                TrapScope.Machine);
            this.firmwareTraps.Add(new FirmwareTrap(address, capturedSlot));
        }
    }

    private void DisarmFirmware()
    {
        if (this.registry is not null)
        {
            foreach (var trap in this.firmwareTraps)
            {
                this.registry.UnregisterSlotDependent(trap.Address, trap.Slot);
            }
        }

        this.firmwareTraps.Clear();
    }

    private void ArmRam(ushort address, TrapScope scope, string? name = null)
    {
        if (this.registry is null || this.ramTraps.ContainsKey(address))
        {
            return;
        }

        var captured = address;
        this.ramTraps.Add(captured, scope);
        this.registry.Register(
            captured,
            name ?? $"Boot milestone ${captured:X4}",
            TrapCategory.OperatingSystem,
            (cpu, _, context) => this.Dispatch(captured, cpu, context),
            scope == TrapScope.GuestOsSession ? "Guest OS session trap." : "Observe-only boot milestone.",
            scope);
    }

    private void DisarmRam()
    {
        if (this.registry is not null)
        {
            foreach (ushort address in this.ramTraps.Keys)
            {
                this.registry.Unregister(address);
            }
        }

        this.ramTraps.Clear();
    }

    private void DisarmRam(ushort address)
    {
        if (this.ramTraps.Remove(address))
        {
            this.registry?.Unregister(address);
        }
    }

    private void DisarmBootWatch()
    {
        var remove = new List<ushort>();
        foreach (var pair in this.ramTraps)
        {
            if (pair.Value != TrapScope.Machine)
            {
                continue;
            }

            this.registry?.Unregister(pair.Key);
            remove.Add(pair.Key);
        }

        foreach (ushort address in remove)
        {
            this.ramTraps.Remove(address);
        }
    }

    private void ForgetSessionAddresses()
    {
        var remove = new List<ushort>();
        foreach (var pair in this.ramTraps)
        {
            if (pair.Value == TrapScope.GuestOsSession)
            {
                remove.Add(pair.Key);
            }
        }

        foreach (ushort address in remove)
        {
            this.ramTraps.Remove(address);
        }
    }

    private TrapResult OnFirmware(int slot, string cardName, ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        var pc = cpu.GetPC();
        string method = this.SelectMethod(cpu, bus, pc);
        if (this.IsReboot(method))
        {
            this.BeginUserReboot();
        }

        if (!this.deviceSelected && (this.stage == GuestOsBootStage.Init || this.warmMayBoot))
        {
            if (this.warmMayBoot && this.stage != GuestOsBootStage.Init)
            {
                this.RestartBootWatch();
            }

            this.Record(
                context.Now,
                BootMilestoneKind.BootDeviceSelected,
                pc,
                $"slot {slot}: {cardName}, method {method}",
                null);
            this.SetStage(GuestOsBootStage.BootDeviceSelected);
            this.deviceSelected = true;
            this.warmMayBoot = false;
            this.openAppleSelection = false;
            this.DisarmKeyboardPoll();
        }

        if (this.stage is GuestOsBootStage.Ready
            or GuestOsBootStage.Unknown
            or GuestOsBootStage.CustomBoot
            or GuestOsBootStage.NoBootDevice)
        {
            return TrapResult.NotHandled;
        }

        this.Record(
            context.Now,
            BootMilestoneKind.BootFirmwareEntered,
            pc,
            $"slot {slot}: {cardName}",
            null);
        if (this.stage is GuestOsBootStage.None or GuestOsBootStage.Init or GuestOsBootStage.BootDeviceSelected)
        {
            this.SetStage(GuestOsBootStage.BootRom);
        }

        this.ArmRam(BootSectorAddress, TrapScope.Machine);
        return TrapResult.NotHandled;
    }

    private TrapResult Dispatch(ushort address, ICpu cpu, IEventContext context)
    {
        if (address == BootSectorAddress)
        {
            return this.OnBootSector(cpu, context);
        }

        return this.OnProfile(address, cpu, context);
    }

    private TrapResult OnBootSector(ICpu cpu, IEventContext context)
    {
        if (this.bootSectorClassified)
        {
            this.BootSectorReentries++;
            return TrapResult.NotHandled;
        }

        this.bootSectorClassified = true;
        var sector = ReadSector(cpu);
        var plan = this.Match(sector);
        var pc = cpu.GetPC();
        if (plan is null)
        {
            this.Record(
                context.Now,
                BootMilestoneKind.CustomBootLoaderEntered,
                pc,
                "boot sector " + FormatSector(sector),
                null);
            this.Finish(GuestOsBootStage.CustomBoot);
            return TrapResult.NotHandled;
        }

        this.active = plan;
        this.Record(
            context.Now,
            BootMilestoneKind.BootSectorLoaded,
            pc,
            plan.Profile.DescribeMilestone(BootMilestoneKind.BootSectorLoaded, BootSectorAddress),
            plan.Profile.Id);
        this.SetStage(GuestOsBootStage.Boot1);
        this.ArmLoader(plan);
        return TrapResult.NotHandled;
    }

    private TrapResult OnProfile(ushort address, ICpu cpu, IEventContext context)
    {
        if (this.active is null || !this.nextStep.TryGetValue(address, out var index))
        {
            this.NotifySessionTrap(address, cpu, context);
            return TrapResult.NotHandled;
        }

        if (!this.active.TryGetSteps(address, out var steps) || index >= steps.Length)
        {
            this.RaiseSystemCall(address, cpu, context);
            return TrapResult.NotHandled;
        }

        var step = steps[index];
        this.nextStep[address] = index + 1;
        var pc = cpu.GetPC();
        var cycle = context.Now;
        var profile = this.active.Profile;
        if (step.IsLoader)
        {
            if (this.stage == GuestOsBootStage.Boot1)
            {
                this.Record(cycle, BootMilestoneKind.LoaderEntered, pc, profile.DescribeMilestone(BootMilestoneKind.LoaderEntered, address), profile.Id);
                this.SetStage(GuestOsBootStage.Loader);
                this.ArmAfterLoader();
                this.EnsureSession(profile.Id, GuestOsBootStage.Loader, armCalls: false);
            }

            return TrapResult.NotHandled;
        }

        if (step.IsReady)
        {
            foreach (var kind in step.Kinds)
            {
                this.Record(cycle, kind, pc, profile.DescribeMilestone(kind, address), profile.Id);
            }

            var detected = this.detector.Detect();
            var terminal = detected.Id == profile.Id || detected.Id == GuestOsId.Unknown
                ? GuestOsBootStage.Ready
                : GuestOsBootStage.Unknown;
            this.Finish(terminal);
            this.RaiseSystemCall(address, cpu, context);
            return TrapResult.NotHandled;
        }

        if (step.IsSystemCall && !this.systemCallRecorded)
        {
            this.systemCallRecorded = true;
            var parameter = ReadReturnTarget(cpu);
            var detail = this.reader is null
                ? profile.DescribeMilestone(BootMilestoneKind.FirstSystemCall, parameter)
                : profile.DescribeSystemCall(this.reader, parameter);
            this.Record(cycle, BootMilestoneKind.FirstSystemCall, pc, detail, profile.Id);
        }

        this.RaiseSystemCall(address, cpu, context);
        return TrapResult.NotHandled;
    }

    private void ArmLoader(ProfilePlan plan)
    {
        foreach (var address in plan.Addresses)
        {
            if (!plan.TryGetSteps(address, out var steps) || steps.Length == 0 || !steps[0].IsLoader)
            {
                continue;
            }

            this.nextStep[address] = 0;
            this.ArmRam(address, TrapScope.Machine);
        }
    }

    private void ArmAfterLoader()
    {
        if (this.active is null)
        {
            return;
        }

        foreach (var address in this.active.Addresses)
        {
            if (!this.active.TryGetSteps(address, out var steps) || steps.Length == 0)
            {
                continue;
            }

            var index = this.nextStep.TryGetValue(address, out var current) ? current : 0;
            if (index >= steps.Length || steps[index].IsLoader)
            {
                this.DisarmRam(address);
                continue;
            }

            this.nextStep[address] = index;
            var scope = this.active.HasSystemCall(address) ? TrapScope.GuestOsSession : TrapScope.Machine;
            this.ArmRam(address, scope);
        }
    }

    private void Finish(GuestOsBootStage terminal)
    {
        this.SetStage(terminal);
        this.DisarmBootWatch();
        this.DisarmKeyboardPoll();
        if (terminal == GuestOsBootStage.Ready && this.active is not null)
        {
            this.ArmSystemCalls(this.active);
            this.EnsureSession(this.active.Profile.Id, GuestOsBootStage.Ready, armCalls: false);
            return;
        }

        if (this.sessionActive)
        {
            GuestOsId? ended = this.TakeSession(endCachedDetection: false);
            this.RaiseSessionEnded(ended);
        }

        this.nextStep.Clear();
    }

    private void NotifySessionTrap(ushort address, ICpu cpu, IEventContext context)
    {
        GuestOsSessionTrapHandler? entered = this.SessionTrapEntered;
        if (entered is null || this.active is null)
        {
            return;
        }

        IReadOnlyList<GuestOsSessionTrap> traps = this.active.Profile.SessionTraps;
        for (int index = 0; index < traps.Count; index++)
        {
            if (traps[index].Address != address)
            {
                continue;
            }

            entered(address, cpu, context.Now);
            return;
        }
    }

    private void RaiseSystemCall(ushort address, ICpu cpu, IEventContext context)
    {
        if (this.active is null || this.reader is null || !this.active.HasSystemCall(address))
        {
            return;
        }

        var parameter = ReadReturnTarget(cpu);
        var detail = this.active.Profile.DescribeSystemCall(this.reader, parameter);
        this.SystemCallObserved?.Invoke(
            this,
            new GuestOsSystemCallEventArgs(context.Now, cpu.GetPC(), this.active.Profile.Id, parameter, detail));
    }

    private ProfilePlan? Match(ReadOnlySpan<byte> sector)
    {
        foreach (var plan in this.plans)
        {
            if (plan.Profile.MatchesBootSector(sector))
            {
                return plan;
            }
        }

        return null;
    }

    private GuestOsId? TakeSession(bool endCachedDetection)
    {
        bool hadSession = this.sessionActive;
        GuestOsId ended = hadSession ? this.sessionOs : this.detector.Current.Id;
        this.registry?.UnregisterScope(TrapScope.GuestOsSession);
        this.ForgetSessionAddresses();
        this.detector.Clear();
        this.sessionActive = false;
        this.sessionOs = GuestOsId.None;
        if (!hadSession && (!endCachedDetection || ended == GuestOsId.None))
        {
            return null;
        }

        return ended;
    }

    private void BeginUserReboot()
    {
        GuestOsId? ended = this.TakeSession(endCachedDetection: true);
        this.DisarmBootWatch();
        this.active = null;
        this.BootSectorReentries = 0;
        this.bootSectorClassified = false;
        this.systemCallRecorded = false;
        this.warmMayBoot = false;
        this.deviceSelected = false;
        this.openAppleSelection = false;
        this.nextStep.Clear();
        this.milestones.Clear();
        this.DisarmKeyboardPoll();
        this.SetStage(GuestOsBootStage.Init);
        this.RaiseSessionEnded(ended);
    }

    private bool IsReboot(string method)
    {
        if (this.warmMayBoot || method is not ("user" or "autostart"))
        {
            return false;
        }

        if (this.stage is GuestOsBootStage.None
            or GuestOsBootStage.Init
            or GuestOsBootStage.BootDeviceSelected
            or GuestOsBootStage.BootRom)
        {
            return false;
        }

        if (this.sessionActive || this.detector.Current.Id != GuestOsId.None)
        {
            return true;
        }

        return this.stage is GuestOsBootStage.Ready
            or GuestOsBootStage.CustomBoot
            or GuestOsBootStage.Unknown
            or GuestOsBootStage.NoBootDevice;
    }

    private void EnsureSession(GuestOsId id, GuestOsBootStage atStage, bool armCalls)
    {
        if (this.sessionActive || id == GuestOsId.None)
        {
            return;
        }

        if (this.active is not null)
        {
            if (armCalls)
            {
                this.ArmSystemCalls(this.active);
            }

            this.ArmProfileSessionTraps();
        }

        this.sessionActive = true;
        this.sessionOs = id;
        this.logger.Information("Guest OS session started: {GuestOsId}", id);
        this.GuestOsSessionStarted?.Invoke(this, new GuestOsSessionEventArgs(id, atStage));
    }

    private void ArmSystemCalls(ProfilePlan plan)
    {
        foreach (ushort address in plan.Addresses)
        {
            if (!plan.TryGetSteps(address, out var steps))
            {
                continue;
            }

            var callIndex = -1;
            for (var i = 0; i < steps.Length; i++)
            {
                if (steps[i].IsSystemCall)
                {
                    callIndex = i;
                }
            }

            if (callIndex < 0)
            {
                continue;
            }

            this.nextStep[address] = callIndex;
            this.ArmRam(address, TrapScope.GuestOsSession);
        }
    }

    private void ArmProfileSessionTraps()
    {
        if (this.active is null)
        {
            return;
        }

        foreach (GuestOsSessionTrap trap in this.active.Profile.SessionTraps)
        {
            this.ArmRam(trap.Address, TrapScope.GuestOsSession, trap.Name);
        }
    }

    private ProfilePlan? FindPlan(GuestOsId id)
    {
        foreach (var plan in this.plans)
        {
            if (plan.Profile.Id == id)
            {
                return plan;
            }
        }

        return null;
    }

    private void RaiseSessionEnded(GuestOsId? ended)
    {
        if (ended is not GuestOsId endedId)
        {
            return;
        }

        this.logger.Information("Guest OS session ended: {GuestOsId}", endedId);
        this.GuestOsSessionEnded?.Invoke(this, new GuestOsSessionEventArgs(endedId, this.stage));
    }

    private void RestartBootWatch()
    {
        this.DisarmBootWatch();
        this.active = null;
        this.BootSectorReentries = 0;
        this.bootSectorClassified = false;
        this.systemCallRecorded = false;
        this.nextStep.Clear();
    }

    private void ArmKeyboardPoll()
    {
        if (this.registry is null || this.keyboardTrapArmed)
        {
            return;
        }

        this.registry.Register(
            KeyboardData,
            TrapOperation.Read,
            "Boot keyboard poll",
            TrapCategory.OperatingSystem,
            (cpu, bus, context) => this.OnKeyboardPoll(cpu, bus, context),
            "Observe-only ROM keyboard wait after reset.");
        this.keyboardTrapArmed = true;
        this.romKeyboardPolls = 0;
    }

    private void DisarmKeyboardPoll()
    {
        if (this.keyboardTrapArmed)
        {
            this.registry?.Unregister(KeyboardData, TrapOperation.Read);
            this.keyboardTrapArmed = false;
        }

        this.romKeyboardPolls = 0;
    }

    private TrapResult OnKeyboardPoll(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        if (this.stage != GuestOsBootStage.Init || this.deviceSelected || !this.IsRomKeyboardWait(cpu, bus))
        {
            this.romKeyboardPolls = 0;
            return TrapResult.NotHandled;
        }

        this.romKeyboardPolls++;
        if (this.romKeyboardPolls < KeyboardPollsBeforeNoDevice)
        {
            return TrapResult.NotHandled;
        }

        Addr poll = cpu.GetPC() >= 3 ? cpu.GetPC() - 3 : cpu.GetPC();
        this.Record(context.Now, BootMilestoneKind.NoBootDevice, poll, "ROM keyboard wait, no boot device", null);
        this.Finish(GuestOsBootStage.NoBootDevice);
        return TrapResult.NotHandled;
    }

    private string SelectMethod(ICpu cpu, IMemoryBus bus, Addr firmware)
    {
        if (this.openAppleSelection)
        {
            return "open-apple";
        }

        if (cpu.LastRetiredInstruction is Addr retired && IsRomMapping(bus.GetPageEntry(retired)))
        {
            return "autostart";
        }

        if (cpu.LastRetiredInstruction is null && firmware == this.resetVector)
        {
            return "autostart";
        }

        return "user";
    }

    private bool OpenAppleHeld()
    {
        return this.machine?.GetComponent<IKeyboardDevice>() is { } keyboard
            && keyboard.Modifiers.HasFlag(KeyboardModifiers.OpenApple);
    }

    private ushort ReadResetVector()
    {
        ICpu? cpu = this.machine?.Cpu;
        if (cpu is null)
        {
            return 0;
        }

        return (ushort)(cpu.Peek8(ResetVector) | (cpu.Peek8(ResetVector + 1u) << 8));
    }

    private string DescribeInit(ushort vector, bool cold)
    {
        string temperature = cold ? "cold" : "warm";
        if (cold && this.openAppleSelection)
        {
            return $"reset vector ${vector:X4}, {temperature}, open-apple";
        }

        return $"reset vector ${vector:X4}, {temperature}";
    }

    private bool IsRomKeyboardWait(ICpu cpu, IMemoryBus bus)
    {
        Addr pc = cpu.GetPC();
        if (pc < 3)
        {
            return false;
        }

        Addr instruction = pc - 3;
        byte opcode = cpu.Peek8(instruction);
        if (opcode is not (0xAD or 0x2C))
        {
            return false;
        }

        if (cpu.Peek8(instruction + 1) != 0x00 || cpu.Peek8(instruction + 2) != 0xC0)
        {
            return false;
        }

        return IsRomMapping(bus.GetPageEntry(instruction));
    }

    private bool IsRomMapping(PageEntry entry)
    {
        if (entry.RegionTag == RegionTag.Rom || entry.Target is RomTarget)
        {
            return true;
        }

        return !entry.CanWrite
            && entry.CanExecute
            && entry.RegionTag is not (RegionTag.Ram or RegionTag.ZeroPage or RegionTag.Stack or RegionTag.Video);
    }

    private void Record(Cycle at, BootMilestoneKind kind, Addr pc, string detail, GuestOsId? os)
    {
        var milestone = new BootMilestone(at, kind, pc, detail, os);
        if (this.milestones.Count == MilestoneLimit)
        {
            this.milestones.RemoveAt(0);
        }

        this.milestones.Add(milestone);
        this.logger.Information(
            "Boot {Milestone} at ${Pc:X4} (cycle {Cycle}): {Detail}",
            kind,
            pc,
            at.Value,
            detail);
        this.MilestoneReached?.Invoke(this, milestone);
    }

    private void SetStage(GuestOsBootStage next)
    {
        if (this.stage == next)
        {
            return;
        }

        var previous = this.stage;
        this.stage = next;
        this.StageChanged?.Invoke(this, new GuestOsBootStageChangedEventArgs(previous, next));
    }

    private readonly record struct FirmwareTrap(ushort Address, int Slot);

    private readonly record struct MilestoneStep(BootMilestoneKind[] Kinds)
    {
        public bool IsLoader => this.Kinds.Length == 1 && this.Kinds[0] == BootMilestoneKind.LoaderEntered;

        public bool IsReady => this.Kinds.Length > 0 && this.Kinds[0] == BootMilestoneKind.KernelReady;

        public bool IsSystemCall => this.Kinds.Length == 1 && this.Kinds[0] == BootMilestoneKind.FirstSystemCall;
    }

    private sealed class ProfilePlan
    {
        private readonly Dictionary<ushort, MilestoneStep[]> steps;

        public ProfilePlan(IGuestOsBootProfile profile)
        {
            this.Profile = profile;
            var order = new List<ushort>();
            var grouped = new Dictionary<ushort, List<BootMilestoneKind>>();
            foreach (var trap in profile.MilestoneTraps)
            {
                if (!grouped.TryGetValue(trap.Address, out var kinds))
                {
                    kinds = [];
                    grouped.Add(trap.Address, kinds);
                    order.Add(trap.Address);
                }

                kinds.Add(trap.Kind);
            }

            this.Addresses = order;
            this.steps = new Dictionary<ushort, MilestoneStep[]>(order.Count);
            foreach (var pair in grouped)
            {
                this.steps.Add(pair.Key, Group(pair.Value));
            }
        }

        public IGuestOsBootProfile Profile { get; }

        public IReadOnlyList<ushort> Addresses { get; }

        public bool TryGetSteps(ushort address, out MilestoneStep[] found)
        {
            if (this.steps.TryGetValue(address, out var located))
            {
                found = located;
                return true;
            }

            found = [];
            return false;
        }

        public bool HasSystemCall(ushort address)
        {
            if (!this.steps.TryGetValue(address, out var found))
            {
                return false;
            }

            foreach (var step in found)
            {
                if (step.IsSystemCall)
                {
                    return true;
                }
            }

            return false;
        }

        private static MilestoneStep[] Group(List<BootMilestoneKind> kinds)
        {
            var grouped = new List<MilestoneStep>();
            var ready = new List<BootMilestoneKind>();
            foreach (var kind in kinds)
            {
                if (kind is BootMilestoneKind.KernelReady or BootMilestoneKind.SystemProgramStarted)
                {
                    ready.Add(kind);
                    continue;
                }

                Flush();
                grouped.Add(new MilestoneStep([kind]));
            }

            Flush();
            return grouped.ToArray();

            void Flush()
            {
                if (ready.Count == 0)
                {
                    return;
                }

                grouped.Add(new MilestoneStep(ready.ToArray()));
                ready.Clear();
            }
        }
    }
}