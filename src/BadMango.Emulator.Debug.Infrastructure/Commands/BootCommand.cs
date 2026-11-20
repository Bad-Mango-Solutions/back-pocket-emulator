// <copyright file="BootCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

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
/// </remarks>
public sealed class BootCommand : CommandHandlerBase, ICommandHelp
{
    /// <summary>
    /// Default delay in milliseconds before booting to allow modifier keys to be pressed.
    /// </summary>
    private const int DefaultBootDelayMs = 1000;

    private readonly MachineProfile? profile;
    private readonly IDebugWindowManager? windowManager;

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
        : base("boot", "Reset and start machine running")
    {
        this.profile = profile;
        this.windowManager = windowManager;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["startup", "power"];

    /// <inheritdoc/>
    public override string Usage => "boot [--on|--off]";

    /// <inheritdoc/>
    public string Synopsis => "boot [--on|--off]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Resets the machine to its initial state and immediately starts execution. " +
        "This is equivalent to pressing the power/reset button on a real computer. " +
        "The CPU loads its reset vector and begins executing from the reset handler. " +
        "Execution runs in the background, keeping the debugger responsive. " +
        "Profiles with boot.autoStart set to true run this same power-on path automatically after load. " +
        "A brief delay is applied before booting to allow time to hold modifier keys " +
        "(Open Apple, Closed Apple) that affect the boot process. " +
        "If the profile has autoVideoWindowOpen enabled, the video window opens automatically.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--on", null, "flag", "Power on the machine and begin execution. This is the default.", "on"),
        new("--off", null, "flag", "Power off the machine, flush disks, and reset devices.", null),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "boot                    Reset and start the machine",
        "startup                 Alias for boot",
        "power --off            Power off the machine",
        "power --on             Power on the machine",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Resets all CPU state (registers, flags, PC) and begins execution. " +
        "Memory and device state may be modified by executed code. " +
        "May open the video window if autoVideoWindowOpen is enabled in the profile. " +
        "Profiles with boot.autoStart enabled invoke this same power-on behavior at load time.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["reset", "pause", "resume", "halt", "video"];

    /// <summary>
    /// Gets a value indicating whether a profile should start automatically after load.
    /// </summary>
    /// <param name="profile">The profile to inspect.</param>
    /// <returns><see langword="true"/> when <c>boot.autoStart</c> is explicitly enabled.</returns>
    public static bool ShouldAutoStart(MachineProfile? profile) => profile?.Boot?.AutoStart == true;

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

        OpenVideoWindowIfConfigured(machine, profile, windowManager);
        await PowerOnMachineAsync(machine, cancellationToken).ConfigureAwait(false);
        logger?.Information("Profile {ProfileName} auto-started after load.", profile?.Name ?? "unknown");
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

        if (debugContext.Machine is null)
        {
            return CommandResult.Error("No machine attached to debug context.");
        }

        bool powerOn = true;
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
            else
            {
                return CommandResult.UsageError($"Unknown boot option '{arg}'.");
            }
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

        bool autoOpenVideo = OpenVideoWindowIfConfigured(debugContext.Machine, profile, windowManager);

        // Start boot with delay to allow modifier keys to be pressed
        _ = BootWithDelayAsync(debugContext, DefaultBootDelayMs);

        string message = autoOpenVideo && windowManager is not null
            ? $"Booting in {DefaultBootDelayMs / 1000.0:F1}s... Hold modifier keys now. Video window opened."
            : $"Booting in {DefaultBootDelayMs / 1000.0:F1}s... Hold modifier keys (Open Apple, Closed Apple) now.";

        return CommandResult.Ok(message);
    }

    private static async Task BootWithDelayAsync(
        IDebugContext debugContext,
        int delayMs)
    {
        try
        {
            // Wait to give user time to press modifier keys
            await Task.Delay(delayMs).ConfigureAwait(false);

            // Now boot the machine
            await PowerOnMachineAsync(debugContext.Machine!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            debugContext.Error.WriteLine($"Boot error: {ex.Message}");
        }
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
}