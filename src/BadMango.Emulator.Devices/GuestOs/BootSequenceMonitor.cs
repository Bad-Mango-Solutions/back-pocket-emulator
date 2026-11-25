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
/// <c>$Cn00</c> is armed for every occupied slot whose card has firmware, on reset and power-on.
/// <c>$0801</c> is armed after that firmware runs. Later addresses come from
/// <see cref="IGuestOsBootProfile"/> and are removed once the boot reaches a terminal stage.
/// A profile that lists a system-call address keeps that one trap so subscribers can see the calls.
/// </para>
/// <para>
/// A boot sector that matches no profile ends at <see cref="GuestOsBootStage.CustomBoot"/>.
/// A recognized boot whose probe does not confirm the profile ends at <see cref="GuestOsBootStage.Unknown"/>.
/// A cold reset (power-on, hard reset, or Open Apple held when RESET releases) returns the stage to
/// <see cref="GuestOsBootStage.Init"/> and logs <see cref="BootMilestoneKind.RomInitEntered"/> first.
/// A warm reset logs that milestone and leaves the stage where it is, until slot firmware runs.
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
    private readonly HashSet<ushort> ramTraps = [];
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

    /// <summary>Raised on every entry to a profile's system-call address after that trap is armed.</summary>
    public event EventHandler<GuestOsSystemCallEventArgs>? SystemCallObserved;

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
            this.SetStage(GuestOsBootStage.Ready);
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
            this.PrepareForBoot();
            this.openAppleSelection = openApple;
            this.Record(cycle, BootMilestoneKind.RomInitEntered, vector, this.DescribeInit(vector, cold: true), null);
            this.SetStage(GuestOsBootStage.Init);
            this.ArmKeyboardPoll();
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

    private static ushort ReadReturnTarget(ICpu cpu)
    {
        uint stackPointer = cpu.Registers.SP.GetByte();
        uint lowAddress = 0x0100u + ((stackPointer + 1u) & 0xFFu);
        uint highAddress = 0x0100u + ((stackPointer + 2u) & 0xFFu);
        uint stacked = cpu.Peek8(lowAddress) | ((uint)cpu.Peek8(highAddress) << 8);
        return (ushort)(stacked + 1u);
    }

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
                "Observe-only boot firmware entry.");
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

    private void ArmRam(ushort address)
    {
        if (this.registry is null || !this.ramTraps.Add(address))
        {
            return;
        }

        var captured = address;
        this.registry.Register(
            captured,
            $"Boot milestone ${captured:X4}",
            TrapCategory.OperatingSystem,
            (cpu, _, context) => this.Dispatch(captured, cpu, context),
            "Observe-only boot milestone.");
    }

    private void DisarmRam()
    {
        if (this.registry is not null)
        {
            foreach (var address in this.ramTraps)
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

    private TrapResult OnFirmware(int slot, string cardName, ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        var pc = cpu.GetPC();
        if (!this.deviceSelected && (this.stage == GuestOsBootStage.Init || this.warmMayBoot))
        {
            if (this.warmMayBoot && this.stage != GuestOsBootStage.Init)
            {
                this.RestartBootWatch();
            }

            string method = this.SelectMethod(cpu, bus, pc);
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

        this.ArmRam(BootSectorAddress);
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
            this.ArmRam(address);
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
            this.ArmRam(address);
        }
    }

    private void Finish(GuestOsBootStage terminal)
    {
        this.SetStage(terminal);
        this.DisarmRam();
        this.DisarmFirmware();
        this.DisarmKeyboardPoll();
        if (terminal != GuestOsBootStage.Ready || this.active is null)
        {
            this.nextStep.Clear();
            return;
        }

        foreach (var address in this.active.Addresses)
        {
            if (!this.active.TryGetSteps(address, out var steps))
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
            this.ArmRam(address);
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

    private void RestartBootWatch()
    {
        this.DisarmRam();
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