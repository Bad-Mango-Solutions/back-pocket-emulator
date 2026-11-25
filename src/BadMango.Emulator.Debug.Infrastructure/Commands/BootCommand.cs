// <copyright file="BootCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Diagnostics.CodeAnalysis;

using BadMango.Emulator.Core.Configuration;
using Serilog;

/// <summary>
/// Boots the machine by performing a reset and starting execution.
/// </summary>
/// <remarks>
/// <para>
/// This command combines a reset operation with starting execution.
/// The machine is reset to its initial state, then begins running
/// in the background, allowing the debugger to remain responsive.
/// </para>
/// <para>
/// A brief delay (1 second by default) is applied before booting to give
/// the user time to hold down any modifier keys (such as Open Apple or
/// Closed Apple) that affect the boot process.
/// </para>
/// <para>
/// After boot, use 'pause' to suspend execution, 'resume' to continue,
/// or 'halt' to force a complete stop.
/// </para>
/// <para>
/// If the machine profile has <c>autoVideoWindowOpen</c> set to <see langword="true"/>, the video window will be opened automatically when the machine boots.
/// </para>
/// <para>
/// <c>--log</c> installs the guest OS detector and boot monitor before power-on, so the boot log
/// includes this boot from reset. Read the log with <c>os boot</c>. <c>--log</c> with <c>--off</c>
/// is a usage error. A monitor that is already installed is kept, and its previous milestone log is cleared.
/// </para>
/// </remarks>
public sealed class BootCommand : CommandHandlerBase, ICommandHelp
{
    /// <summary>
    /// Default delay in milliseconds before booting to allow modifier keys to be pressed.
    /// </summary>
    private const int DefaultBootDelayMs = 1000;

    private readonly MachineProfile? profile;
    private readonly IDebugWindowManager? windowManager;
    private readonly ILogger logger;
    private readonly int bootDelayMs;
    private readonly Func<BadMango.Emulator.Bus.Interfaces.IMachine, CancellationToken, Task> startPowerOn;

    /// <summary>
    /// Initializes a new instance of the <see cref="BootCommand"/> class.
    /// </summary>
    public BootCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BootCommand"/> class with profile and window manager.
    /// </summary>
    /// <param name="profile">The machine profile containing boot configuration.</param>
    /// <param name="windowManager">The debug window manager for opening the video window.</param>
    public BootCommand(MachineProfile? profile, IDebugWindowManager? windowManager)
        : this(profile, windowManager, Log.Logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BootCommand"/> class with profile, window manager, and logger.
    /// </summary>
    /// <param name="profile">The machine profile containing boot configuration.</param>
    /// <param name="windowManager">The debug window manager for opening the video window.</param>
    /// <param name="logger">Logger used when <c>--log</c> installs the boot monitor.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public BootCommand(MachineProfile? profile, IDebugWindowManager? windowManager, ILogger logger)
        : this(profile, windowManager, DefaultBootDelayMs, PowerOnMachineAsync, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BootCommand"/> class with a test power-on path.
    /// </summary>
    /// <param name="profile">The machine profile containing boot configuration.</param>
    /// <param name="windowManager">The debug window manager for opening the video window.</param>
    /// <param name="bootDelayMs">Delay before power-on. Production uses <see cref="DefaultBootDelayMs"/>.</param>
    /// <param name="startPowerOn">
    /// Starts power-on. Production passes <see cref="PowerOnMachineAsync"/>, which also starts execution.
    /// </param>
    /// <param name="logger">Logger used when <c>--log</c> installs the boot monitor.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="startPowerOn"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    internal BootCommand(
        MachineProfile? profile,
        IDebugWindowManager? windowManager,
        int bootDelayMs,
        Func<BadMango.Emulator.Bus.Interfaces.IMachine, CancellationToken, Task> startPowerOn,
        ILogger logger)
        : base("boot", "Reset and start machine running")
    {
        ArgumentNullException.ThrowIfNull(startPowerOn);
        ArgumentNullException.ThrowIfNull(logger);
        this.profile = profile;
        this.windowManager = windowManager;
        this.logger = logger;
        this.bootDelayMs = bootDelayMs;
        this.startPowerOn = startPowerOn;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["startup", "power"];

    /// <inheritdoc/>
    public override string Usage => "boot [--on|--off] [--log]";

    /// <inheritdoc/>
    public string Synopsis => "boot [--on|--off] [--log]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Resets the machine to its initial state and immediately starts execution. " +
        "This is equivalent to pressing the power/reset button on a real computer. " +
        "The CPU loads its reset vector and begins executing from the reset handler. " +
        "Execution runs in the background, keeping the debugger responsive. " +
        "Profiles with boot.autoStart set to true run this same power-on path automatically after load. " +
        "A brief delay is applied before booting to allow time to hold modifier keys " +
        "(Open Apple, Closed Apple) that affect the boot process. " +
        "If the profile has autoVideoWindowOpen enabled, the video window opens automatically. " +
        "With --log, the guest OS detector and boot monitor are installed before power-on, " +
        "so milestones are captured from reset. Read the log with os boot. " +
        "--log cannot be combined with --off. A monitor that is already installed is kept, " +
        "and its previous milestone log is cleared so this boot starts fresh.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--on", null, "flag", "Power on the machine and begin execution. This is the default.", "on"),
        new("--off", null, "flag", "Power off the machine, flush disks, and reset devices.", null),
        new("--log", null, "flag", "Install the guest OS boot monitor before power-on. Read the log with os boot. Not valid with --off.", "off"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "boot                    Reset and start the machine",
        "boot --log              Power on and record the guest OS boot log",
        "startup                 Alias for boot",
        "power --off             Power off the machine",
        "power --on              Power on the machine",
        "power --on --log        Power on and record the guest OS boot log",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Resets all CPU state (registers, flags, PC) and begins execution. " +
        "Memory and device state may be modified by executed code. " +
        "May open the video window if autoVideoWindowOpen is enabled in the profile. " +
        "Profiles with boot.autoStart enabled invoke this same power-on behavior at load time. " +
        "With --log, registers observe-only boot traps before power-on. " +
        "A monitor that is already installed is kept and its previous milestone log is cleared.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["reset", "pause", "resume", "halt", "video", "os"];

    /// <summary>
    /// Gets the power-on task started by the last successful boot. Tests await it after <c>--log</c>.
    /// </summary>
    /// <value>The in-flight boot, or a completed task when boot has not been started.</value>
    internal Task BootTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Gets a value indicating whether a profile should start automatically after load.
    /// </summary>
    /// <param name="profile">The profile to inspect.</param>
    /// <returns><see langword="true"/> when <c>boot.autoStart</c> is explicitly enabled.</returns>
    public static bool ShouldAutoStart([NotNullWhen(true)] MachineProfile? profile) => profile?.Boot?.AutoStart == true;

    /// <summary>
    /// Performs the shared machine power-on flow used by both profile auto-start and the boot command.
    /// </summary>
    /// <param name="machine">The target machine.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes after the machine power-on flow starts execution.</returns>
    public static async Task PowerOnMachineAsync(
        BadMango.Emulator.Bus.Interfaces.IMachine machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        await machine.BootAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the machine automatically after profile load when <c>boot.autoStart</c> is enabled.
    /// </summary>
    /// <param name="machine">The machine instance.</param>
    /// <param name="profile">The loaded profile.</param>
    /// <param name="windowManager">The optional debug window manager.</param>
    /// <param name="logger">The optional logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the profile requested auto-start and boot was invoked;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static async Task<bool> AutoStartProfileAsync(
        BadMango.Emulator.Bus.Interfaces.IMachine machine,
        MachineProfile? profile,
        IDebugWindowManager? windowManager,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (!ShouldAutoStart(profile))
        {
            return false;
        }

        // Auto-start only powers on a machine that is Off; it must never reset one that is already on.
        if (machine.State != BadMango.Emulator.Bus.MachineState.Off)
        {
            logger?.Information(
                "Profile {ProfileName} auto-start skipped because the machine is {State}, not Off.",
                profile.Name,
                machine.State);
            return false;
        }

        // This runs fire-and-forget from startup, so failures are logged here instead of going unobserved.
        try
        {
            OpenVideoWindowIfConfigured(machine, profile, windowManager);
            await PowerOnMachineAsync(machine, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger?.Information("Profile {ProfileName} auto-start was cancelled.", profile.Name);
            return false;
        }

        // Intentional generic catch, approved by Josh Pactor (PR #619): auto-start runs fire-and-forget at
        // startup, and power-on can throw any exception type from any attached device. Narrowing this would
        // let unexpected failures go unobserved; every exception is logged, nothing is swallowed silently.
        // codeql[cs/catch-of-all-exceptions]
        catch (Exception ex)
        {
            logger?.Error(ex, "Profile {ProfileName} auto-start failed.", profile.Name);
            return false;
        }

        logger?.Information("Profile {ProfileName} auto-started after load.", profile.Name);
        return true;
    }

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        bool powerOn = true;
        bool logBoot = false;
        foreach (string arg in args)
        {
            if (arg.Equals("--off", StringComparison.OrdinalIgnoreCase))
            {
                powerOn = false;
            }
            else if (arg.Equals("--on", StringComparison.OrdinalIgnoreCase))
            {
                powerOn = true;
            }
            else if (arg.Equals("--log", StringComparison.OrdinalIgnoreCase))
            {
                logBoot = true;
            }
            else
            {
                return CommandResult.UsageError($"Unknown boot option '{arg}'.");
            }
        }

        if (!powerOn && logBoot)
        {
            return CommandResult.UsageError("--log cannot be combined with --off.");
        }

        if (debugContext.Machine is null)
        {
            return CommandResult.Error("No machine attached to debug context.");
        }

        if (!powerOn)
        {
            try
            {
                debugContext.Machine.PowerOffAsync().GetAwaiter().GetResult();
                return CommandResult.Ok("Machine powered off.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                return CommandResult.Error($"Power-off failed: {ex.Message}");
            }
        }

        if (logBoot)
        {
            GuestOsMonitorInstaller.Install(debugContext.Machine, this.logger, clearExistingLog: true);
        }

        bool autoOpenVideo = OpenVideoWindowIfConfigured(debugContext.Machine, profile, windowManager);

        // Start boot with delay to allow modifier keys to be pressed.
        // The monitor, when requested, is already installed so this power-on is logged from reset.
        this.BootTask = this.BootWithDelayAsync(debugContext);

        string message = autoOpenVideo && windowManager is not null
            ? $"Booting in {this.bootDelayMs / 1000.0:F1}s... Hold modifier keys now. Video window opened."
            : $"Booting in {this.bootDelayMs / 1000.0:F1}s... Hold modifier keys (Open Apple, Closed Apple) now.";
        if (logBoot)
        {
            message += " Guest OS boot log is on. Read it with 'os boot'.";
        }

        return CommandResult.Ok(message);
    }

    private static bool ShouldAutoOpenVideoWindow(MachineProfile? profile, IDebugWindowManager? windowManager) =>
        profile?.Boot?.AutoVideoWindowOpen == true && windowManager is not null;

    private static bool OpenVideoWindowIfConfigured(
        BadMango.Emulator.Bus.Interfaces.IMachine machine,
        MachineProfile? profile,
        IDebugWindowManager? windowManager)
    {
        if (!ShouldAutoOpenVideoWindow(profile, windowManager))
        {
            return false;
        }

        _ = windowManager!.ShowWindowAsync("Video", machine);
        return true;
    }

    private async Task BootWithDelayAsync(IDebugContext debugContext)
    {
        try
        {
            // Wait to give user time to press modifier keys
            await Task.Delay(this.bootDelayMs).ConfigureAwait(false);

            // Now boot the machine
            await this.startPowerOn(debugContext.Machine!, CancellationToken.None).ConfigureAwait(false);
        }

        // The command returns before this task finishes. Power-on can throw any exception type from any
        // attached device, and that fault would otherwise go unobserved. The debugger shows the message
        // on the error stream. Same rationale as profile auto-start (PR #619).
        // codeql[cs/catch-of-all-exceptions]
        catch (Exception ex)
        {
            debugContext.Error.WriteLine($"Boot error: {ex}");
        }
    }
}