// <copyright file="OsCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Bus.Interfaces;

using Serilog;

/// <summary>
/// Shows the detected guest OS and the boot milestone log, and can turn the monitor on.
/// </summary>
public sealed class OsCommand : CommandHandlerBase, ICommandHelp
{
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OsCommand"/> class.
    /// </summary>
    public OsCommand()
        : this(Log.Logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OsCommand"/> class.
    /// </summary>
    /// <param name="logger">Logger used when <c>os on</c> installs the monitor.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public OsCommand(ILogger logger)
        : base("os", "Show the guest OS, boot log, and file loads")
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <inheritdoc/>
    public string Synopsis => "os [boot|files|on]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Shows the guest operating system in memory and the boot stage. " +
        "os boot lists the milestone log. os files lists file-load progress, one line per file: " +
        "BASIC.SYSTEM 6656/10240 (65%), or BASIC.SYSTEM 6656 when the size is unknown. " +
        "os on installs the boot monitor when the profile left it off. " +
        "The monitor's traps return not-handled, so guest-visible behavior does not change.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "os                      Show the detected guest OS and boot stage",
        "os boot                 List boot milestones",
        "os files                List file-load progress",
        "os on                   Turn the boot monitor on",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "os on registers observe-only boot traps when the monitor was off.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["trapmon", "statmon"];

    /// <inheritdoc/>
    public override string Usage => "os [boot|files|on]";

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        if (context is not IDebugContext debugContext || debugContext.Machine is not IMachine machine)
        {
            return CommandResult.Error("No machine is attached.");
        }

        if (args.Length > 1)
        {
            return CommandResult.UsageError("Usage: os [boot|files|on]");
        }

        if (args.Length == 1)
        {
            if (string.Equals(args[0], "boot", StringComparison.OrdinalIgnoreCase))
            {
                context.Output.WriteLine(GuestOsInspection.FormatBootLog(GuestOsInspection.ReadBootLog(machine)));
                return CommandResult.Ok();
            }

            if (string.Equals(args[0], "files", StringComparison.OrdinalIgnoreCase))
            {
                context.Output.WriteLine(GuestOsInspection.FormatFileActivity(GuestOsInspection.ReadFileActivity(machine)));
                return CommandResult.Ok();
            }

            if (string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase))
            {
                Enable(machine);
                context.Output.WriteLine(GuestOsInspection.FormatGuestOs(GuestOsInspection.ReadGuestOs(machine)));
                return CommandResult.Ok("Guest OS boot monitor is on.");
            }

            return CommandResult.UsageError("Usage: os [boot|files|on]");
        }

        context.Output.WriteLine(GuestOsInspection.FormatGuestOs(GuestOsInspection.ReadGuestOs(machine)));
        return CommandResult.Ok();
    }

    private void Enable(IMachine machine)
    {
        GuestOsMonitorInstaller.Install(machine, this.logger, clearExistingLog: false);
    }
}