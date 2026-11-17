// <copyright file="BreakCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;
using System.Text;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Manages execution breakpoints backed by the trap registry.
/// </summary>
/// <remarks>
/// Breakpoints register a <see cref="Bus.TrapOperation.Call"/> trap that stops
/// the CPU immediately when the program counter reaches the breakpoint address,
/// *before* the instruction at that address executes. Resuming with <c>step</c>
/// or <c>run</c> executes that instruction once and then continues normally.
/// Use this with <c>run</c> to diagnose execution that reaches a specific PC.
/// </remarks>
public sealed class BreakCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BreakCommand"/> class.
    /// </summary>
    public BreakCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BreakCommand"/> class that uses the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public BreakCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("bp", "Manage execution breakpoints")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["breakpoint"];

    /// <inheritdoc/>
    public override string Usage => "bp <add|remove|list|clear|enable|disable|save|load> [address] [label]";

    /// <inheritdoc/>
    public string Synopsis => "bp <add|remove|list|clear|enable|disable|save|load> [args]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Adds, removes, lists, enables, or disables execution breakpoints. " +
        "Breakpoints stop the CPU immediately when the program counter reaches " +
        "the given address, before the instruction at that address executes. " +
        "Resuming with 'step' or 'run' executes that instruction once and then " +
        "continues normally until the next breakpoint or stop condition.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "bp add $3A00            Break when PC reaches $3A00",
        "bp add $3D00 boot-end   Break with label",
        "bp list                 Show all breakpoints",
        "bp remove $3A00         Remove a single breakpoint",
        "bp disable $3A00        Keep entry but stop matching",
        "bp save config.json     Save breakpoints to file",
        "bp load config.json     Load breakpoints from file",
        "bp clear                Remove every breakpoint",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Registers / unregisters Call traps on the active trap registry.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["run", "stop", "watch", "trace"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (debugContext.Cpu is null)
        {
            return CommandResult.Error("No CPU attached. Boot a profile first.");
        }

        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        return sub switch
        {
            "add" or "set" => Add(debugContext, args),
            "remove" or "rm" or "del" => Remove(debugContext, args),
            "list" or "ls" or "" => List(debugContext),
            "clear" => Clear(debugContext),
            "enable" => SetEnabled(debugContext, args, enabled: true),
            "disable" => SetEnabled(debugContext, args, enabled: false),
            "save" => Save(debugContext, args),
            "load" => Load(debugContext, args),
            _ => CommandResult.Error($"Unknown subcommand: '{args[0]}'. Use add/remove/list/clear/enable/disable/save/load."),
        };
    }

    private CommandResult Add(IDebugContext context, string[] args)
    {
        if (args.Length < 2)
        {
            return CommandResult.UsageError("Usage: bp add <address> [label]");
        }

        if (!AddressParser.TryParse(args[1], context.Machine, out uint address))
        {
            return CommandResult.UsageError($"Invalid address: '{args[1]}'.");
        }

        string? label = args.Length > 2 ? string.Join(' ', args, 2, args.Length - 2) : null;
        IDebugSession active = DebugSessions.Require(this.session, context);
        if (active.GetBreakpoints().Any(item => item.Address == address))
        {
            return CommandResult.Error($"Breakpoint at ${address:X4} already exists.");
        }

        active.AddBreakpoint(address, label);
        Breakpoint added = active.GetBreakpoints().First(item => item.Address == address);
        string text = $"Breakpoint set at ${address:X4}{(label is null ? string.Empty : $"  ; {label}")}.";
        this.renderer.WriteBreakpoint(context.Output, added, text);
        return CommandResult.Ok();
    }

    private CommandResult Remove(IDebugContext context, string[] args)
    {
        if (args.Length < 2)
        {
            return CommandResult.UsageError("Usage: bp remove <address>");
        }

        if (!AddressParser.TryParse(args[1], context.Machine, out uint address))
        {
            return CommandResult.UsageError($"Invalid address: '{args[1]}'.");
        }

        if (!DebugSessions.Require(this.session, context).RemoveBreakpoint(address))
        {
            return CommandResult.Error($"No breakpoint at ${address:X4}.");
        }

        context.Output.WriteLine($"Removed breakpoint at ${address:X4}.");
        return CommandResult.Ok();
    }

    private CommandResult Clear(IDebugContext context)
    {
        IDebugSession active = DebugSessions.Require(this.session, context);
        int count = active.GetBreakpoints().Count;
        active.ClearBreakpoints();
        context.Output.WriteLine($"Cleared {count} breakpoint(s).");
        return CommandResult.Ok();
    }

    private CommandResult SetEnabled(IDebugContext context, string[] args, bool enabled)
    {
        if (args.Length < 2)
        {
            return CommandResult.Error($"Usage: bp {(enabled ? "enable" : "disable")} <address>");
        }

        if (!AddressParser.TryParse(args[1], context.Machine, out uint address))
        {
            return CommandResult.Error($"Invalid address: '{args[1]}'.");
        }

        if (!context.Breakpoints.SetEnabled(address, enabled))
        {
            return CommandResult.Error($"No breakpoint at ${address:X4}.");
        }

        context.Output.WriteLine($"Breakpoint at ${address:X4} {(enabled ? "enabled" : "disabled")}.");
        return CommandResult.Ok();
    }

    private CommandResult List(IDebugContext context)
    {
        IDebugSession active = DebugSessions.Require(this.session, context);
        IReadOnlyList<Breakpoint> all = active.GetBreakpoints();
        var text = new StringBuilder();
        if (all.Count == 0)
        {
            text.Append("No breakpoints set.");
            uint? last = context.Breakpoints.LastHitAddress;
            if (last is not null)
            {
                text.AppendLine();
                text.Append(string.Format(CultureInfo.InvariantCulture, "Last hit (before clear): ${0:X4}", last.Value));
            }
        }
        else
        {
            text.Append("ADDR   EN  HITS       LABEL");
            foreach (Breakpoint bp in all)
            {
                text.AppendLine();
                text.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    "${0:X4}  {1}   {2,-9}  {3}",
                    bp.Address,
                    bp.Enabled ? "Y" : "n",
                    bp.HitCount,
                    bp.Label ?? string.Empty));
            }

            uint? lastHit = context.Breakpoints.LastHitAddress;
            if (lastHit is not null)
            {
                text.AppendLine();
                text.AppendLine();
                text.Append(string.Format(CultureInfo.InvariantCulture, "Last hit: ${0:X4}", lastHit.Value));
            }
        }

        this.renderer.WriteBreakpoints(context.Output, all, text.ToString());
        return CommandResult.Ok();
    }

    private CommandResult Save(IDebugContext context, string[] args)
    {
        if (args.Length < 2)
        {
            return CommandResult.Error("Usage: bp save <filepath>");
        }

        string rawPath = args[1];
        string resolvedPath = rawPath;
        if (context.PathResolver is not null)
        {
            if (!context.PathResolver.TryResolve(rawPath, out string? resolved))
            {
                return CommandResult.Error($"Cannot resolve path: '{rawPath}'.");
            }

            resolvedPath = resolved!;
        }

        try
        {
            context.Breakpoints.SaveToFile(resolvedPath);
            string displayPath = resolvedPath != rawPath
                ? $"{rawPath} (resolved to '{resolvedPath}')"
                : resolvedPath;
            context.Output.WriteLine($"Saved {context.Breakpoints.Count} breakpoint(s) to {displayPath}");
            return CommandResult.Ok();
        }
        catch (Exception ex)
        {
            return CommandResult.Error($"Failed to save breakpoints: {ex.Message}");
        }
    }

    private CommandResult Load(IDebugContext context, string[] args)
    {
        if (args.Length < 2)
        {
            return CommandResult.Error("Usage: bp load <filepath>");
        }

        string rawPath = args[1];
        string resolvedPath = rawPath;
        if (context.PathResolver is not null)
        {
            if (!context.PathResolver.TryResolve(rawPath, out string? resolved))
            {
                return CommandResult.Error($"Cannot resolve path: '{rawPath}'.");
            }

            resolvedPath = resolved!;
        }

        if (!File.Exists(resolvedPath))
        {
            string errorMessage = resolvedPath != rawPath
                ? $"File not found: '{rawPath}' (resolved to '{resolvedPath}')"
                : $"File not found: '{rawPath}'";
            return CommandResult.Error(errorMessage);
        }

        try
        {
            int countBefore = context.Breakpoints.Count;
            context.Breakpoints.LoadFromFile(resolvedPath);
            int countAfter = context.Breakpoints.Count;
            string displayPath = resolvedPath != rawPath
                ? $"{rawPath} (resolved to '{resolvedPath}')"
                : resolvedPath;
            context.Output.WriteLine($"Loaded breakpoints from {displayPath} ({countAfter - countBefore} new breakpoint(s), {countAfter} total)");
            return CommandResult.Ok();
        }
        catch (Exception ex)
        {
            return CommandResult.Error($"Failed to load breakpoints: {ex.Message}");
        }
    }
}