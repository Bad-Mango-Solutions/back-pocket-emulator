// <copyright file="BreakpointTools.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.ComponentModel;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Serilog;

/// <summary>
/// MCP tools for execution breakpoints and memory watchpoints.
/// </summary>
[McpServerToolType]
public sealed class BreakpointTools
{
    private readonly IDebugSession session;
    private readonly IDebugContext context;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BreakpointTools"/> class.
    /// </summary>
    /// <param name="session">The shared debug session.</param>
    /// <param name="context">The debug context used to resolve addresses.</param>
    /// <param name="logger">The host logger.</param>
    public BreakpointTools(IDebugSession session, IDebugContext context, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);
        this.session = session;
        this.context = context;
        this.logger = logger.ForContext<BreakpointTools>();

        // Watchpoint hit lines follow the debug context output, which is stdout on a console context.
        // The MCP protocol owns stdout, so hits log to stderr instead.
        this.context.Watchpoints.SetLogOutput(Console.Error);
    }

    /// <summary>
    /// Sets an execution breakpoint.
    /// </summary>
    /// <param name="address">The breakpoint address. Accepts $hex, 0xhex, decimal, or a soft-switch name.</param>
    /// <param name="label">An optional label.</param>
    /// <returns>An emudbg envelope whose payload is the breakpoint, or an error envelope.</returns>
    [McpServerTool(Name = "set_breakpoint", UseStructuredContent = true)]
    [Description("Set an execution breakpoint. The CPU stops before the instruction at the address executes. Address accepts $hex, 0xhex, decimal, or a soft-switch name.")]
    public object SetBreakpoint(
        [Description("Breakpoint address. Accepts $hex, 0xhex, decimal, or a soft-switch name.")] string address,
        [Description("Optional label.")] string? label = null)
    {
        if (!DebugMcpArguments.TryParseAddress(address, this.context.Machine, out uint parsed, out string? addressError))
        {
            return this.Fail("set_breakpoint", addressError!);
        }

        if (!DebugMcpArguments.TryNormalizeLabel(label, out string? normalized, out string? labelError))
        {
            return this.Fail("set_breakpoint", labelError!);
        }

        try
        {
            if (this.session.GetBreakpoints().Any(item => item.Address == parsed))
            {
                return this.Fail("set_breakpoint", $"Breakpoint at ${parsed:X4} already exists.");
            }

            this.session.AddBreakpoint(parsed, normalized);
            Breakpoint added = this.session.GetBreakpoints().First(item => item.Address == parsed);
            this.logger.Information("Breakpoint set at {Address}.", parsed);
            return DebugEnvelope<Breakpoint>.Create(added);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("set_breakpoint", ex.Message, ex);
        }
    }

    /// <summary>
    /// Sets a memory watchpoint.
    /// </summary>
    /// <param name="address">The watch address. Accepts $hex, 0xhex, decimal, or a soft-switch name.</param>
    /// <param name="access">Read, Write, or ReadWrite. Also accepts r, w, and rw. Defaults to ReadWrite.</param>
    /// <param name="stopOnHit">Whether a hit requests a CPU stop. Defaults to false.</param>
    /// <param name="label">An optional label.</param>
    /// <returns>An emudbg envelope whose payload is the watchpoint, or an error envelope.</returns>
    [McpServerTool(Name = "set_watchpoint", UseStructuredContent = true)]
    [Description("Set a memory watchpoint. Access is Read, Write, or ReadWrite (r, w, and rw are accepted). Pass stopOnHit true to halt on a hit.")]
    public object SetWatchpoint(
        [Description("Watch address. Accepts $hex, 0xhex, decimal, or a soft-switch name.")] string address,
        [Description("Read, Write, or ReadWrite. Also accepts r, w, and rw. Defaults to ReadWrite.")] string access = "ReadWrite",
        [Description("When true, request a CPU stop on a hit. Defaults to false.")] bool stopOnHit = false,
        [Description("Optional label.")] string? label = null)
    {
        if (!DebugMcpArguments.TryParseAddress(address, this.context.Machine, out uint parsed, out string? addressError))
        {
            return this.Fail("set_watchpoint", addressError!);
        }

        if (!DebugMcpArguments.TryParseWatchAccess(access, out WatchAccess watchAccess, out string? accessError))
        {
            return this.Fail("set_watchpoint", accessError!);
        }

        if (!DebugMcpArguments.TryNormalizeLabel(label, out string? normalized, out string? labelError))
        {
            return this.Fail("set_watchpoint", labelError!);
        }

        try
        {
            if (this.session.GetWatchpoints().Any(item => item.Address == parsed))
            {
                return this.Fail("set_watchpoint", $"Watchpoint at ${parsed:X4} already exists.");
            }

            this.session.AddWatchpoint(parsed, watchAccess, stopOnHit, normalized);
            Watchpoint added = this.session.GetWatchpoints().First(item => item.Address == parsed);
            this.logger.Information("Watchpoint set at {Address} for {Access}.", parsed, watchAccess);
            return DebugEnvelope<Watchpoint>.Create(added);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("set_watchpoint", ex.Message, ex);
        }
    }

    private CallToolResult Fail(string tool, string message, Exception? exception = null)
    {
        if (exception is null)
        {
            this.logger.Warning("{Tool} failed: {Message}", tool, message);
        }
        else
        {
            this.logger.Warning(exception, "{Tool} failed: {Message}", tool, message);
        }

        return DebugMcpResults.Error(message);
    }
}