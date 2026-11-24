// <copyright file="BootSequenceMonitor.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using System.Globalization;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Serilog;

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
/// Reset returns the stage to <see cref="GuestOsBootStage.None"/> until firmware is entered again.
/// When no milestone was seen, <see cref="PublishFromDetector"/> asks <see cref="IGuestOsDetector"/>
/// and sets <see cref="GuestOsBootStage.Ready"/> if an operating system is already in memory.
/// </para>
/// </remarks>
public sealed class BootSequenceMonitor : IBootSequenceMonitor, IResetReceiver
{
    private const int MilestoneLimit = 256;
    private const ushort BootSectorBase = 0x0800;
    private const ushort BootSectorAddress = 0x0801;
    private const int BootSectorBytes = 16;
    private const int ShownSectorBytes = 8;

    private readonly ILogger logger;
    private readonly IGuestOsDetector detector;
    private readonly ProfilePlan[] plans;
    private readonly List<BootMilestone> milestones = [];
    private readonly Dictionary<ushort, int> nextStep = [];
    private readonly HashSet<ushort> ramTraps = [];
    private readonly List<ushort> firmwareAddresses = [];

    private IMachine? machine;
    private ITrapRegistry? registry;
    private IGuestMemoryReader? reader;
    private ProfilePlan? active;
    private GuestOsBootStage stage;
    private bool bootSectorClassified;
    private bool systemCallRecorded;

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
    /// Reset still returns the stage to <see cref="GuestOsBootStage.None"/> until firmware is entered again.
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

    /// <inheritdoc />
    public void OnResetAsserted(Cycle cycle)
    {
        this.logger.Debug("Guest OS boot watch reset at cycle {Cycle}.", cycle.Value);
        this.DisarmRam();
        this.DisarmFirmware();
        this.ClearBoot();
        this.ArmFirmware();
    }

    /// <inheritdoc />
    public void OnResetReleased(Cycle cycle)
    {
        this.logger.Debug("Guest OS boot watch saw RESET release at cycle {Cycle}.", cycle.Value);
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
                (cpu, _, context) => this.OnFirmware(capturedSlot, cardName, cpu, context),
                "Observe-only boot firmware entry.");
            this.firmwareAddresses.Add(address);
        }
    }

    private void DisarmFirmware()
    {
        if (this.registry is not null)
        {
            foreach (var address in this.firmwareAddresses)
            {
                this.registry.Unregister(address);
            }
        }

        this.firmwareAddresses.Clear();
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

    private TrapResult OnFirmware(int slot, string cardName, ICpu cpu, IEventContext context)
    {
        this.Record(
            context.Now,
            BootMilestoneKind.BootFirmwareEntered,
            cpu.GetPC(),
            $"slot {slot}: {cardName}",
            null);
        if (this.stage == GuestOsBootStage.None)
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