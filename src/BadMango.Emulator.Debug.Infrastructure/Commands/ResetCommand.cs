// <copyright file="ResetCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Devices;
using BadMango.Emulator.Devices.Interfaces;

using Serilog;

/// <summary>
/// Resets the machine or CPU to its initial state.
/// </summary>
/// <remarks>
/// <para>
/// Performs a reset, which:
/// - Sets PC to the reset vector ($FFFC-$FFFD).
/// - Initializes processor status flags.
/// - Clears the halt state.
/// </para>
/// <para>
/// When a machine is attached, a soft reset pulses the RESET line through the keyboard's
/// Reset key (as Ctrl-Reset would). A keyboard with <see cref="IKeyboardInput"/> receives
/// that pulse as <see cref="IKeyboardInput.KeyDown"/> and <see cref="IKeyboardInput.KeyUp"/>
/// of <see cref="KeyboardKey.Reset"/>. A keyboard without <see cref="IKeyboardInput"/> still
/// uses <see cref="IResetSource.PressResetKey"/>, <see cref="IResetSource.ReleaseResetKey"/>
/// and <see cref="IKeyboardDevice.SetModifiers"/>, so the Apple-key holds keep working. Without
/// a keyboard, the pulse goes through the machine's own RESET source. Devices respond to RESET,
/// RAM is preserved, and the run state is unchanged: a running machine continues into the ROM
/// reset handler, while a stopped or paused machine stops with PC at the reset vector. The pulse
/// is marshalled onto the machine thread, so the command is safe to use while the machine is
/// running. With the --hard flag, the machine also performs a power-on reset of the CPU and
/// devices and clears memory.
/// </para>
/// <para>
/// If no machine is attached, reset falls back to CPU-only behavior and uses
/// the attached bus for --hard memory clear when available.
/// </para>
/// <para>
/// <c>--log</c> installs the guest OS detector and boot monitor before a cold reset
/// (<c>--hard</c>, or <c>--open-apple</c> and its aliases <c>--command</c>, <c>--reboot</c>,
/// and <c>--cold</c>), so the reboot is logged from the start. Read the log with <c>os boot</c>.
/// <c>--log</c> without a cold-reset option is a usage error. A monitor that is already installed
/// is kept, and its previous milestone log is cleared.
/// </para>
/// </remarks>
public sealed class ResetCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetCommand"/> class.
    /// </summary>
    public ResetCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetCommand"/> class that resets the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public ResetCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : this(session, renderer, Log.Logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetCommand"/> class that resets the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    /// <param name="logger">Logger used when <c>--log</c> installs the boot monitor.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public ResetCommand(IDebugSession? session, IDebugOutputRenderer? renderer, ILogger logger)
        : base("reset", "Reset the CPU (soft or hard)")
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["rst"];

    /// <inheritdoc/>
    public override string Usage => "reset [--hard] [--open-apple|-o] [--closed-apple|-a] [--log]";

    /// <inheritdoc/>
    public string Synopsis => "reset [--hard] [--open-apple|--closed-apple] [--log]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Pulses the RESET line like Ctrl-Reset: Control is implied and devices respond to RESET " +
        "while the CPU runs its reset sequence through the reset vector ($FFFC-$FFFD), setting I " +
        "and clearing D. RAM is preserved, and a running machine keeps running into the ROM reset " +
        "handler; a stopped or paused machine stops at the reset vector. With --hard, also performs " +
        "a power-on reset of the CPU and devices and clears memory to zeros. With --open-apple / " +
        "--reboot / --cold, or --closed-apple / --diag, the corresponding Apple key is held through " +
        "the ROM reset sampling window for a short time after RESET releases, matching the hardware " +
        "behavior of the Apple IIe reset handler. With --log, the guest OS detector and boot monitor " +
        "are installed before a cold reset (--hard, or --open-apple and --command / --reboot / --cold), " +
        "so the reboot is logged from the start. The first milestone is Init (the reset vector and cold or warm). " +
        "Read the log with os boot. --log without a cold-reset " +
        "option is a usage error. A monitor that is already installed is kept, and its previous " +
        "milestone log is cleared so this boot starts fresh.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--hard", "-h", "flag", "Also clear memory to zeros", "off"),
        new("--open-apple", "-o", "flag", "Hold Open Apple (PB0) through the ROM reset sampling window; aliases: --command --reboot --cold", "off"),
        new("--closed-apple", "-a", "flag", "Hold Closed Apple (PB1) through the ROM reset sampling window; aliases: --option --diag", "off"),
        new("--log", null, "flag", "Install the guest OS boot monitor before a cold reset (--hard or --open-apple). Read the log with os boot.", "off"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "reset                        Soft reset (like Ctrl-Reset; RAM preserved)",
        "reset --hard                 Hard reset (CPU + memory clear)",
        "reset --hard --log           Hard reset and record the guest OS boot log",
        "reset --open-apple           Soft reset while holding Open Apple (cold reboot)",
        "reset --closed-apple         Soft reset while holding Closed Apple (self-test)",
        "reset --cold                 Alias for holding Open Apple through reset",
        "reset --cold --log           Open-Apple reset and record the guest OS boot log",
        "rst                          Alias for reset",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Resets devices and runs the CPU reset sequence. With --hard, also re-initializes the CPU " +
        "and devices to their power-on state and clears all memory. With --log, registers observe-only " +
        "boot traps before a cold reset and clears an existing monitor's milestone log.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["run", "step", "stop", "regs", "os", "boot"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        bool hardReset = args.Any(arg =>
            arg.Equals("--hard", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-h", StringComparison.OrdinalIgnoreCase));
        bool holdOpenApple = args.Any(arg =>
            arg.Equals("--open-apple", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--command", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--reboot", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--cold", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-o", StringComparison.OrdinalIgnoreCase));
        bool holdClosedApple = args.Any(arg =>
            arg.Equals("--closed-apple", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--option", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--diag", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-a", StringComparison.OrdinalIgnoreCase));
        bool logBoot = args.Any(arg => arg.Equals("--log", StringComparison.OrdinalIgnoreCase));
        if (logBoot && !hardReset && !holdOpenApple)
        {
            return CommandResult.UsageError("--log requires a cold reset (--hard or --open-apple).");
        }

        if (debugContext.Machine?.State == Bus.MachineState.Off)
        {
            return CommandResult.Error("Machine is off. Use 'power --on' (or 'boot') first.");
        }

        if (debugContext.Cpu is null)
        {
            return CommandResult.Error("No CPU attached to debug context.");
        }

        if (logBoot)
        {
            if (debugContext.Machine is not { } loggedMachine)
            {
                return CommandResult.Error("No machine attached to debug context.");
            }

            GuestOsMonitorInstaller.Install(loggedMachine, this.logger, clearExistingLog: true);
            debugContext.Output.WriteLine("Guest OS boot log is on. Read it with 'os boot'.");
        }

        if (debugContext.Machine is { } machine)
        {
            if (machine.GetComponent<IKeyboardDevice>() is { } keyboard)
            {
                if (machine.GetComponent<IKeyboardInput>() is { } keyboardInput)
                {
                    TriggerKeyboardReset(machine, keyboard, keyboardInput, holdOpenApple, holdClosedApple, hardReset);
                }
                else
                {
                    TriggerDeviceKeyboardReset(machine, keyboard, holdOpenApple, holdClosedApple, hardReset);
                }
            }
            else
            {
                // No keyboard to pulse. The shared session owns machine reset.
                DebugSessions.Require(this.session, debugContext).Reset(hardReset ? ResetKind.Hard : ResetKind.Warm);
            }

            if (machine.State == MachineState.Running)
            {
                // The reset is applied by the run loop; the CPU's registers belong to it now.
                debugContext.Output.WriteLine(hardReset ? "Hard reset signalled; machine is running." : "Soft reset signalled; machine is running.");
                return CommandResult.Ok();
            }
        }
        else
        {
            if (hardReset && debugContext.Bus is not null)
            {
                debugContext.Bus.Clear();
                debugContext.Output.WriteLine("Memory cleared.");
            }

            debugContext.Cpu.Reset();
            debugContext.Cpu.ClearStopRequest();
        }

        var pc = debugContext.Cpu.GetPC();
        string text = hardReset
            ? $"CPU reset. PC = ${pc:X4}{Environment.NewLine}Hard reset completed."
            : $"CPU reset. PC = ${pc:X4}{Environment.NewLine}Soft reset completed.";
        this.renderer.WriteReset(
            debugContext.Output,
            new ResetReceipt(hardReset ? ResetKind.Hard : ResetKind.Warm, pc),
            text);

        return CommandResult.Ok();
    }

    /// <summary>
    /// Pulses RESET through the keyboard's Reset key, holding Control for the duration of the pulse.
    /// </summary>
    /// <param name="machine">The machine to reset.</param>
    /// <param name="keyboard">The keyboard whose Reset key drives the line.</param>
    /// <param name="input">The keyboard input source used to send Reset and modifier snapshots.</param>
    /// <param name="holdOpenApple"><see langword="true"/> to hold Open Apple through the ROM reset sampling window.</param>
    /// <param name="holdClosedApple"><see langword="true"/> to hold Closed Apple through the ROM reset sampling window.</param>
    /// <param name="hardReset"><see langword="true"/> to treat the pulse as a power-on reset while the line is held.</param>
    /// <remarks>
    /// The Apple-key bits are intentionally left active after RESET deasserts so the ROM's reset
    /// handler can sample them before the temporary reset-only flags are cleared. The release callback
    /// releases Reset with a snapshot that keeps the reset-added Apple keys active, then those temporary
    /// Apple keys are released later on a short scheduler-based delay that preserves any user-held
    /// modifiers present in the meantime.
    /// </remarks>
    private static void TriggerKeyboardReset(IMachine machine, IKeyboardDevice keyboard, IKeyboardInput input, bool holdOpenApple, bool holdClosedApple, bool hardReset)
    {
        KeyboardModifiers originalModifiers = KeyboardModifiers.None;
        KeyboardModifiers resetOnlyModifiers = KeyboardModifiers.None;
        machine.PulseReset(
            () =>
            {
                originalModifiers = keyboard.Modifiers;
                var resetModifiers = originalModifiers | KeyboardModifiers.Control;
                if (holdOpenApple)
                {
                    resetModifiers |= KeyboardModifiers.OpenApple;
                    if (!originalModifiers.HasFlag(KeyboardModifiers.OpenApple))
                    {
                        resetOnlyModifiers |= KeyboardModifiers.OpenApple;
                    }
                }

                if (holdClosedApple)
                {
                    resetModifiers |= KeyboardModifiers.ClosedApple;
                    if (!originalModifiers.HasFlag(KeyboardModifiers.ClosedApple))
                    {
                        resetOnlyModifiers |= KeyboardModifiers.ClosedApple;
                    }
                }

                input.KeyDown(KeyboardKey.Reset, resetModifiers);

                // Must apply synchronously. ExecuteResetPulse reads Signals.IsAsserted
                // immediately after assertLine returns.
                machine.Scheduler.DispatchDue();
            },
            () =>
            {
                input.KeyUp(KeyboardKey.Reset, originalModifiers | resetOnlyModifiers);

                // Must apply synchronously. CompleteResetPulse reads Signals.IsAsserted
                // immediately after releaseLine returns.
                machine.Scheduler.DispatchDue();

                if (resetOnlyModifiers == KeyboardModifiers.None)
                {
                    return;
                }

                var releaseCycleDelay = GetResetAppleReleaseDelayCycles(machine);
                machine.Scheduler.ScheduleAt(
                    machine.Scheduler.Now + releaseCycleDelay,
                    ScheduledEventKind.DeferredWork,
                    priority: 0,
                    _ =>
                    {
                        var pendingModifiers = keyboard.Modifiers;
                        pendingModifiers &= ~resetOnlyModifiers;

                        // TODO(#388): §7.2 carries modifiers only as the snapshot on a key event,
                        // and §7.4 has no call that sets them alone. KeyboardInput restores a
                        // snapshot with OnHostKeyUp(KeyboardKey.None), but the spec does not define
                        // KeyUp/KeyDown(KeyboardKey.None) as that convention, and this public KeyUp
                        // journals a fake key-up. Clearing these temporary Apple keys needs an API
                        // epic #388 has not added.
                        input.KeyUp(KeyboardKey.None, pendingModifiers);

                        // No nested DispatchDue. This callback runs inside DispatchEventsUntil,
                        // which drains the 0-delay event KeyUp just queued.
                    },
                    tag: "reset-apple-release");
            },
            hardReset);
    }

    /// <summary>
    /// Pulses RESET through <see cref="IResetSource.PressResetKey"/> when the machine has a
    /// keyboard but no <see cref="IKeyboardInput"/>.
    /// </summary>
    /// <param name="machine">The machine to reset.</param>
    /// <param name="keyboard">The keyboard whose Reset key drives the line.</param>
    /// <param name="holdOpenApple"><see langword="true"/> to hold Open Apple through the ROM reset sampling window.</param>
    /// <param name="holdClosedApple"><see langword="true"/> to hold Closed Apple through the ROM reset sampling window.</param>
    /// <param name="hardReset"><see langword="true"/> to treat the pulse as a power-on reset while the line is held.</param>
    /// <remarks>
    /// This is the pre-<see cref="IKeyboardInput"/> path. Sub-issue 6 of epic #388 removes
    /// <see cref="IResetSource.PressResetKey"/>, <see cref="IResetSource.ReleaseResetKey"/>
    /// and <see cref="IKeyboardDevice.SetModifiers"/> once no caller is left. Until then, a keyboard
    /// without an input layer still has to honor <c>--open-apple</c> and <c>--closed-apple</c>.
    /// </remarks>
    private static void TriggerDeviceKeyboardReset(IMachine machine, IKeyboardDevice keyboard, bool holdOpenApple, bool holdClosedApple, bool hardReset)
    {
        KeyboardModifiers originalModifiers = KeyboardModifiers.None;
        KeyboardModifiers resetOnlyModifiers = KeyboardModifiers.None;
        machine.PulseReset(
            () =>
            {
                originalModifiers = keyboard.Modifiers;
                var resetModifiers = originalModifiers | KeyboardModifiers.Control;
                if (holdOpenApple)
                {
                    resetModifiers |= KeyboardModifiers.OpenApple;
                    if (!originalModifiers.HasFlag(KeyboardModifiers.OpenApple))
                    {
                        resetOnlyModifiers |= KeyboardModifiers.OpenApple;
                    }
                }

                if (holdClosedApple)
                {
                    resetModifiers |= KeyboardModifiers.ClosedApple;
                    if (!originalModifiers.HasFlag(KeyboardModifiers.ClosedApple))
                    {
                        resetOnlyModifiers |= KeyboardModifiers.ClosedApple;
                    }
                }

                keyboard.SetModifiers(resetModifiers);
                keyboard.PressResetKey();
            },
            () =>
            {
                keyboard.ReleaseResetKey();
                keyboard.SetModifiers(originalModifiers | resetOnlyModifiers);

                if (resetOnlyModifiers == KeyboardModifiers.None)
                {
                    return;
                }

                var releaseCycleDelay = GetResetAppleReleaseDelayCycles(machine);
                machine.Scheduler.ScheduleAt(
                    machine.Scheduler.Now + releaseCycleDelay,
                    ScheduledEventKind.DeferredWork,
                    priority: 0,
                    _ =>
                    {
                        var pendingModifiers = keyboard.Modifiers;
                        pendingModifiers &= ~resetOnlyModifiers;
                        keyboard.SetModifiers(pendingModifiers);
                    },
                    tag: "reset-apple-release");
            },
            hardReset);
    }

    private static ulong GetResetAppleReleaseDelayCycles(IMachine machine)
    {
        const double AppleResetSamplingWindowMs = 500.0;
        long clockSpeedHz = machine.ClockSpeedHz > 0 ? machine.ClockSpeedHz : 1_000_000;
        return (ulong)Math.Max(1, (AppleResetSamplingWindowMs * clockSpeedHz) / 1000.0);
    }
}