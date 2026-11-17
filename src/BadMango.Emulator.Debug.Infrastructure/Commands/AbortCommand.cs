// <copyright file="AbortCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Core.Cpu;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Shows or sets how the 65C02 treats <c>SignalLine.Abort</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>abort</c> prints the current reaction. <c>abort noop</c> selects
/// <see cref="AbortReaction.NoOp"/>. <c>abort hostbreak</c> selects
/// <see cref="AbortReaction.HostBreak"/>. With JSON output the result is one
/// <c>emudbg/v1</c> envelope.
/// </para>
/// <para>
/// The command only changes the reaction. It does not assert or deassert the line.
/// A machine reset does not clear the setting.
/// </para>
/// </remarks>
public sealed class AbortCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AbortCommand"/> class.
    /// </summary>
    public AbortCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AbortCommand"/> class.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public AbortCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("abort", "Show or set the 65C02 abort reaction")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override string Usage => "abort [noop|hostbreak]";

    /// <inheritdoc/>
    public string Synopsis => "abort [noop|hostbreak]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Shows or sets how a 65C02 treats SignalLine.Abort. With no argument, prints the current " +
        "reaction. 'abort noop' ignores the line (the default: an unmapped fetch still stops with " +
        "STP). 'abort hostbreak' requests a host stop at the next instruction boundary and does not " +
        "set STP. The command does not pulse the line. A machine reset leaves the setting alone. " +
        "HostBreak is rejected when the attached CPU is not a 65C02.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "abort                    Print the current reaction",
        "abort noop               Ignore ABORT (default)",
        "abort hostbreak          Stop the host at the next instruction boundary",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Changes how the 65C02 treats an abort line the bus has already asserted. Does not pulse the line.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["fault", "stop", "reset"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (args.Length > 1)
        {
            return CommandResult.Error("Usage: abort [noop|hostbreak]");
        }

        IDebugSession active = DebugSessions.Require(this.session, debugContext);
        try
        {
            if (args.Length == 1)
            {
                if (!TryParse(args[0], out AbortReaction reaction))
                {
                    return CommandResult.Error($"Unknown abort reaction '{args[0]}'. Use noop or hostbreak.");
                }

                active.AbortReaction = reaction;
            }

            AbortReaction current = active.AbortReaction;
            this.renderer.WriteAbort(
                debugContext.Output,
                new AbortReceipt(current),
                $"Abort reaction: {NameOf(current)}");
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
    }

    private static bool TryParse(string text, out AbortReaction reaction)
    {
        if (text.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            reaction = AbortReaction.NoOp;
            return true;
        }

        if (text.Equals("hostbreak", StringComparison.OrdinalIgnoreCase))
        {
            reaction = AbortReaction.HostBreak;
            return true;
        }

        reaction = default;
        return false;
    }

    private static string NameOf(AbortReaction reaction) => reaction switch
    {
        AbortReaction.NoOp => "NoOp",
        AbortReaction.HostBreak => "HostBreak",
        _ => reaction.ToString(),
    };
}