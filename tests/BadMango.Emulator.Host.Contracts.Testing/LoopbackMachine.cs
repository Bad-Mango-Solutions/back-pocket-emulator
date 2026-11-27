// <copyright file="LoopbackMachine.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts.Testing;

using BadMango.Emulator.Host.Contracts;

/// <summary>
/// In-memory machine held by the loopback. The session locks <see cref="Gate"/> around every mutation.
/// This type is not a CPU and it is not on the bus.
/// </summary>
internal sealed class LoopbackMachine
{
    /// <summary>
    /// The stub memory size, in bytes.
    /// </summary>
    internal const int MemorySize = 65536;

    /// <summary>
    /// How many audio blocks one subscription keeps. Older blocks are dropped.
    /// </summary>
    internal const int MaxAudioQueued = 8;

    /// <summary>
    /// How many input events are kept. Older events are dropped.
    /// </summary>
    internal const int MaxInputLog = 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackMachine"/> class.
    /// </summary>
    /// <param name="id">The machine id.</param>
    /// <param name="spec">The instance specification.</param>
    internal LoopbackMachine(MachineId id, HostInstanceSpec spec)
    {
        Id = id;
        Name = spec.Name;
        ProfileId = spec.ProfileId;
        StartPaused = spec.StartPaused;
        Power = spec.PowerCapabilities;
        NominalClockHz = spec.NominalClockHz;
        OutputKind = spec.OutputKind;
        CpuSummary = spec.CpuSummary;
        SchedulerSummary = spec.SchedulerSummary;
        Video = new HostVideoState(spec.OutputKind, Text: true, Mixed: false, Page2: false, HiRes: false, EightyColumns: false, AltCharSet: false);
    }

    /// <summary>
    /// Gets the lock for this machine. Callers take it once per host call.
    /// </summary>
    internal Lock Gate { get; } = new();

    /// <summary>
    /// Gets the machine id.
    /// </summary>
    internal MachineId Id { get; }

    /// <summary>
    /// Gets the instance name.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    /// Gets the profile id.
    /// </summary>
    internal string ProfileId { get; }

    /// <summary>
    /// Gets a value indicating whether Power On leaves the machine paused.
    /// </summary>
    internal bool StartPaused { get; }

    /// <summary>
    /// Gets the power record, or <see langword="null"/> for the conservative set.
    /// </summary>
    internal PowerCapabilities? Power { get; }

    /// <summary>
    /// Gets the nominal clock in Hz.
    /// </summary>
    internal long NominalClockHz { get; }

    /// <summary>
    /// Gets the video output kind.
    /// </summary>
    internal HostVideoKind OutputKind { get; }

    /// <summary>
    /// Gets the CPU summary.
    /// </summary>
    internal string? CpuSummary { get; }

    /// <summary>
    /// Gets the scheduler summary.
    /// </summary>
    internal string? SchedulerSummary { get; }

    /// <summary>
    /// Gets or sets the run state.
    /// </summary>
    internal HostRunState State { get; set; } = HostRunState.Off;

    /// <summary>
    /// Gets or sets the throttle.
    /// </summary>
    internal HostThrottle Throttle { get; set; } = HostThrottle.Normal;

    /// <summary>
    /// Gets or sets the measured speed ratio.
    /// </summary>
    internal double? MeasuredSpeed { get; set; }

    /// <summary>
    /// Gets or sets the profile overlay JSON.
    /// </summary>
    internal string? OverlayJson { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the debugger is attached.
    /// </summary>
    internal bool DebuggerAttached { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the machine has been removed.
    /// </summary>
    internal bool Removed { get; set; }

    /// <summary>
    /// Gets the 64 KiB stub memory.
    /// </summary>
    internal byte[] Memory { get; } = new byte[MemorySize];

    /// <summary>
    /// Gets the breakpoints.
    /// </summary>
    internal List<HostBreakpoint> Breakpoints { get; } = [];

    /// <summary>
    /// Gets the watches.
    /// </summary>
    internal List<HostWatch> Watches { get; } = [];

    /// <summary>
    /// Gets the drives.
    /// </summary>
    internal List<LoopbackDrive> Drives { get; } =
    [
        new(new DriveId(6, 1), boot: true),
        new(new DriveId(6, 2), boot: false),
    ];

    /// <summary>
    /// Gets the keys that are down.
    /// </summary>
    internal List<HostKey> KeysDown { get; } = [];

    /// <summary>
    /// Gets or sets the last modifier snapshot.
    /// </summary>
    internal HostKeyModifiers Modifiers { get; set; }

    /// <summary>
    /// Gets joystick X for indexes 0 and 1.
    /// </summary>
    internal float[] JoystickX { get; } = new float[2];

    /// <summary>
    /// Gets joystick Y for indexes 0 and 1.
    /// </summary>
    internal float[] JoystickY { get; } = new float[2];

    /// <summary>
    /// Gets paddle positions 0 through 3.
    /// </summary>
    internal byte[] Paddles { get; } = new byte[4];

    /// <summary>
    /// Gets game-port buttons 0 through 3.
    /// </summary>
    internal bool[] Buttons { get; } = new bool[4];

    /// <summary>
    /// Gets or sets the last mouse motion kind.
    /// </summary>
    internal HostMouseMotionKind MouseKind { get; set; } = HostMouseMotionKind.Relative;

    /// <summary>
    /// Gets or sets the last mouse X.
    /// </summary>
    internal float MouseX { get; set; }

    /// <summary>
    /// Gets or sets the last mouse Y.
    /// </summary>
    internal float MouseY { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the left mouse button is down.
    /// </summary>
    internal bool MouseLeft { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the middle mouse button is down.
    /// </summary>
    internal bool MouseMiddle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the right mouse button is down.
    /// </summary>
    internal bool MouseRight { get; set; }

    /// <summary>
    /// Gets or sets the window that holds pointer capture.
    /// </summary>
    internal string? CaptureWindowId { get; set; }

    /// <summary>
    /// Gets or sets the last text-injection receipt.
    /// </summary>
    internal HostTextReceipt LastText { get; set; }

    /// <summary>
    /// Gets the input log.
    /// </summary>
    internal List<HostInputEntry> InputLog { get; } = [];

    /// <summary>
    /// Gets the live presentation subscriptions.
    /// </summary>
    internal List<LoopbackSubscription> Subscriptions { get; } = [];

    /// <summary>
    /// Gets or sets the latest presentation sample.
    /// </summary>
    internal HostPresentationSample? Latest { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the previous frame publish.
    /// </summary>
    internal long LastFrameTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the instantaneous frame rate.
    /// </summary>
    internal double? FramesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the latest frame sequence.
    /// </summary>
    internal ulong? LastSequence { get; set; }

    /// <summary>
    /// Gets or sets the latest frame cycle.
    /// </summary>
    internal ulong? LastCycle { get; set; }

    /// <summary>
    /// Gets or sets the line-out format.
    /// </summary>
    internal HostAudioFormat AudioFormat { get; set; } = HostAudioDefaults.LineOut;

    /// <summary>
    /// Gets or sets the text-screen snapshot.
    /// </summary>
    internal HostScreenText Screen { get; set; } = new(40, 24, string.Empty, false, false, false);

    /// <summary>
    /// Gets or sets the video soft-switch snapshot.
    /// </summary>
    internal HostVideoState Video { get; set; }

    /// <summary>
    /// Gets the boot drive label, when one drive is marked as the boot drive.
    /// </summary>
    internal string? BootDriveId => Drives.FirstOrDefault(drive => drive.IsBootDrive)?.Id.Label;

    /// <summary>
    /// Finds a drive in the topology.
    /// </summary>
    /// <param name="drive">The drive id.</param>
    /// <returns>The drive, or <see langword="null"/>.</returns>
    internal LoopbackDrive? FindDrive(DriveId drive)
    {
        return Drives.FirstOrDefault(candidate => candidate.Id == drive);
    }
}