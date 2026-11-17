// <copyright file="RegsCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Displays the current state of all CPU registers.
/// </summary>
/// <remarks>
/// Shows the Accumulator (A), Index registers (X, Y), Stack Pointer (SP),
/// Program Counter (PC), and Processor Status (P) flags in a formatted display.
/// Register widths vary based on CPU mode:
/// - Native (E=0, CP=0): All registers are 32-bit
/// - 65816 Compat (E=0, CP=1): A width depends on M flag, X/Y width depends on X flag (0=16-bit, 1=8-bit)
/// - 65C02 Compat (E=1, CP=1): All registers are 8-bit, SP is 8-bit.
/// </remarks>
public sealed class RegsCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegsCommand"/> class.
    /// </summary>
    public RegsCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegsCommand"/> class that reads the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public RegsCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("regs", "Display CPU registers")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["r", "registers"];

    /// <inheritdoc/>
    public override string Usage => "regs";

    /// <inheritdoc/>
    public string Synopsis => "regs";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Displays all CPU registers in a formatted box display including PC, SP, A, X, Y, " +
        "and processor status flags. Register widths vary based on CPU mode: Native mode " +
        "shows 32-bit registers, 65816 compat shows 16-bit, and 65C02 compat shows 8-bit.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "regs                    Display all CPU registers",
        "r                       Alias for regs",
        "registers               Alias for regs",
    ];

    /// <inheritdoc/>
    public string? SideEffects => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["pc", "step", "run"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        try
        {
            RegisterSnapshot snapshot = DebugSessions.Require(this.session, debugContext).GetRegisters();
            this.renderer.WriteRegisters(debugContext.Output, snapshot);
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
    }
}