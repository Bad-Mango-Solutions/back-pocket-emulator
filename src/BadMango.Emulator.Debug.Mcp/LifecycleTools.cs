// <copyright file="LifecycleTools.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;

using ModelContextProtocol.Server;

using Serilog;

/// <summary>
/// MCP tools for profile load, reset, step, run, and pause.
/// </summary>
[McpServerToolType]
public sealed class LifecycleTools
{
    private const int MaxStepCount = 10000;

    private readonly IDebugSession session;
    private readonly IDebugContext context;
    private readonly DebugMcpProfileLoader profiles;
    private readonly DebugMcpRunTracker runs;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LifecycleTools"/> class.
    /// </summary>
    /// <param name="session">The shared debug session.</param>
    /// <param name="context">The debug context used to read machine state.</param>
    /// <param name="profiles">The profile loader.</param>
    /// <param name="runs">The background-run tracker.</param>
    /// <param name="logger">The host logger.</param>
    public LifecycleTools(
        IDebugSession session,
        IDebugContext context,
        DebugMcpProfileLoader profiles,
        DebugMcpRunTracker runs,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(logger);
        this.session = session;
        this.context = context;
        this.profiles = profiles;
        this.runs = runs;
        this.logger = logger.ForContext<LifecycleTools>();
    }

    /// <summary>
    /// Loads a machine profile and rebuilds the machine.
    /// </summary>
    /// <param name="name">Profile name, or an absolute profile path under a sandbox root.</param>
    /// <returns>An emudbg envelope whose payload is the loaded machine.</returns>
    [McpServerTool(Name = "load_profile", UseStructuredContent = true)]
    [Description("Load a profile by name and rebuild the machine.")]
    public DebugEnvelope<MachineInfo> LoadProfile(
        [Description("Profile name, or an absolute path under a configured sandbox root.")] string name)
    {
        try
        {
            this.StopActiveRun();
            MachineInfo info = this.profiles.Load(name);
            this.logger.Information("Loaded profile {ProfileName}.", info.Name);
            return DebugEnvelope<MachineInfo>.Create(info);
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException
            or TimeoutException)
        {
            this.logger.Warning(ex, "load_profile failed for {ProfileName}.", name);
            throw new ModelContextProtocol.McpException(ex.Message);
        }
    }

    /// <summary>
    /// Resets the machine.
    /// </summary>
    /// <param name="kind">Warm, Hard, or PowerOn. Defaults to Warm.</param>
    /// <returns>An emudbg envelope whose payload is the reset receipt.</returns>
    [McpServerTool(Name = "reset", UseStructuredContent = true)]
    [Description("Reset the CPU (soft or hard).")]
    public DebugEnvelope<ResetReceipt> Reset(
        [Description("Warm, Hard, or PowerOn. Defaults to Warm.")] string kind = "Warm")
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out ResetKind resetKind))
        {
            throw new ModelContextProtocol.McpException($"Unknown reset kind '{kind}'. Expected Warm, Hard, or PowerOn.");
        }

        try
        {
            this.session.Reset(resetKind);
            RegisterSnapshot registers = this.session.GetRegisters();
            return DebugEnvelope<ResetReceipt>.Create(new ResetReceipt(resetKind, checked((uint)registers.ProgramCounter)));
        }
        catch (InvalidOperationException ex)
        {
            this.logger.Warning(ex, "reset failed.");
            throw new ModelContextProtocol.McpException(ex.Message);
        }
    }

    /// <summary>
    /// Steps the CPU for a number of instructions, bounded by a cycle budget.
    /// </summary>
    /// <param name="count">The number of instructions. Defaults to 1.</param>
    /// <param name="maxCycles">The cycle budget. Defaults to 1000000.</param>
    /// <returns>An emudbg envelope whose payload is the step receipt.</returns>
    [McpServerTool(Name = "step", UseStructuredContent = true)]
    [Description("Execute one or more CPU instructions.")]
    public DebugEnvelope<StepReceipt> Step(
        [Description("Number of instructions to execute. Defaults to 1.")] int count = 1,
        [Description("Cycle budget. Defaults to 1000000. Stepping stops when the budget is exhausted.")] ulong maxCycles = DebugMcpBudgets.DefaultMaxCycles)
    {
        if (count < 1)
        {
            throw new ModelContextProtocol.McpException("Step count must be at least 1.");
        }

        if (count > MaxStepCount)
        {
            throw new ModelContextProtocol.McpException($"Step count exceeds maximum ({MaxStepCount}). Use run_until for longer execution.");
        }

        try
        {
            ulong executed = this.session.Step(count, CancellationToken.None, maxCycles);
            RegisterSnapshot registers = this.session.GetRegisters();
            return DebugEnvelope<StepReceipt>.Create(new StepReceipt(executed, checked((uint)registers.ProgramCounter)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            this.logger.Warning(ex, "step failed.");
            throw new ModelContextProtocol.McpException(ex.Message);
        }
    }

    /// <summary>
    /// Runs until a condition or the cycle budget is met. Large budgets use start, poll, and stop.
    /// </summary>
    /// <param name="condition">An address, bp, watch, mem, or cycles.</param>
    /// <param name="maxCycles">The cycle budget. Defaults to 1000000.</param>
    /// <param name="action">run, start, poll, or stop.</param>
    /// <param name="runId">The background run identifier for poll and stop.</param>
    /// <param name="address">The address for a mem condition.</param>
    /// <param name="value">The expected byte for a mem condition.</param>
    /// <param name="untilCycles">The cycle target when <paramref name="condition"/> is cycles.</param>
    /// <returns>
    /// An emudbg envelope. A synchronous run carries a run result. Start, poll, stop, and a budget
    /// above the synchronous ceiling carry a background-run receipt.
    /// </returns>
    [McpServerTool(Name = "run_until", UseStructuredContent = true)]
    [Description("Run until a stop condition is reached. maxCycles is the cycle budget (default 1000000). Long runs use action start, poll, and stop.")]
    public object RunUntil(
        [Description("Stop condition: an address ($C002, 0xC002, or decimal), bp, watch, mem, or cycles.")] string condition = "",
        [Description("Cycle budget. Defaults to 1000000. Budgets above 5000000 start in the background when action is run.")] ulong maxCycles = DebugMcpBudgets.DefaultMaxCycles,
        [Description("run, start, poll, or stop. Defaults to run.")] string action = "run",
        [Description("Background run id for poll and stop. Defaults to the run this host started.")] string? runId = null,
        [Description("Address for a mem condition. Accepts $hex, 0xhex, or decimal.")] string? address = null,
        [Description("Expected byte for a mem condition. Accepts $hex, 0xhex, or decimal.")] string? value = null,
        [Description("Cycle target when condition is cycles.")] ulong? untilCycles = null)
    {
        try
        {
            string normalized = NormalizeAction(action);
            if (normalized is "poll")
            {
                return this.Poll(this.RequireRunId(runId));
            }

            if (normalized is "stop")
            {
                return this.Stop(this.RequireRunId(runId));
            }

            if (string.IsNullOrWhiteSpace(condition))
            {
                throw new ModelContextProtocol.McpException("run_until requires a condition. Use an address, bp, watch, mem, or cycles.");
            }

            RunCondition runCondition = ParseCondition(condition, address, value, untilCycles);
            if (normalized is "start" || maxCycles > DebugMcpBudgets.SynchronousMaxCycles)
            {
                return this.Start(runCondition, maxCycles);
            }

            RunResult result = this.session.RunUntil(runCondition, new RunBudget(maxCycles));
            return DebugEnvelope<RunResult>.Create(result);
        }
        catch (ModelContextProtocol.McpException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimeoutException)
        {
            this.logger.Warning(ex, "run_until failed.");
            throw new ModelContextProtocol.McpException(ex.Message);
        }
    }

    /// <summary>
    /// Pauses the machine and stops a background run when one is active.
    /// </summary>
    /// <returns>An emudbg envelope whose payload is the pause receipt.</returns>
    [McpServerTool(Name = "pause", UseStructuredContent = true)]
    [Description("Suspend machine execution.")]
    public DebugEnvelope<PauseReceipt> Pause()
    {
        try
        {
            this.StopActiveRun();
            this.session.Pause();
            string state = this.context.Machine?.State.ToString() ?? "None";
            uint programCounter = checked((uint)this.session.GetRegisters().ProgramCounter);
            return DebugEnvelope<PauseReceipt>.Create(new PauseReceipt(state, programCounter));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            this.logger.Warning(ex, "pause failed.");
            throw new ModelContextProtocol.McpException(ex.Message);
        }
    }

    private static string NormalizeAction(string action)
    {
        if (action.Equals("run", StringComparison.OrdinalIgnoreCase)
            || action.Equals("start", StringComparison.OrdinalIgnoreCase)
            || action.Equals("poll", StringComparison.OrdinalIgnoreCase)
            || action.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return action.ToLowerInvariant();
        }

        throw new ModelContextProtocol.McpException($"Unknown run action '{action}'. Expected run, start, poll, or stop.");
    }

    private static RunCondition ParseCondition(string condition, string? address, string? value, ulong? untilCycles)
    {
        if (condition.Equals("bp", StringComparison.OrdinalIgnoreCase))
        {
            return new RunCondition(RunConditionKind.Breakpoint);
        }

        if (condition.Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            return new RunCondition(RunConditionKind.Watchpoint);
        }

        if (condition.Equals("mem", StringComparison.OrdinalIgnoreCase))
        {
            return new RunCondition(
                RunConditionKind.MemoryEquals,
                MemoryAddress: ParseAddress(address),
                MemoryValue: [ParseByte(value)]);
        }

        if (condition.Equals("cycles", StringComparison.OrdinalIgnoreCase))
        {
            if (untilCycles is null)
            {
                throw new ModelContextProtocol.McpException("cycles condition requires untilCycles.");
            }

            return new RunCondition(RunConditionKind.CycleCount, CycleLimit: untilCycles);
        }

        return new RunCondition(RunConditionKind.ProgramCounter, Address: ParseAddress(condition));
    }

    private static uint ParseAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ModelContextProtocol.McpException("An address is required.");
        }

        if (TryParseUInt(text, out uint parsed))
        {
            return parsed;
        }

        throw new ModelContextProtocol.McpException($"Invalid address '{text}'.");
    }

    private static byte ParseByte(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ModelContextProtocol.McpException("A byte value is required.");
        }

        if (!TryParseUInt(text, out uint parsed) || parsed > byte.MaxValue)
        {
            throw new ModelContextProtocol.McpException($"Invalid byte value '{text}'.");
        }

        return (byte)parsed;
    }

    private static bool TryParseUInt(string text, out uint parsed)
    {
        string value = text.Trim();
        if (value.StartsWith('$'))
        {
            return uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private DebugEnvelope<BackgroundRunReceipt> Start(RunCondition condition, ulong maxCycles)
    {
        Guid id = this.session.StartRun(condition, new RunBudget(maxCycles));
        this.runs.Remember(id);
        this.logger.Information("Started background run {RunId} with budget {MaxCycles}.", id, maxCycles);
        return DebugEnvelope<BackgroundRunReceipt>.Create(
            new BackgroundRunReceipt("start", true, id.ToString("D"), null),
            id.ToString("D"));
    }

    private DebugEnvelope<BackgroundRunReceipt> Poll(Guid id)
    {
        RunResult? result = this.session.GetRunStatus(id);
        if (result is null)
        {
            if (this.runs.Current != id)
            {
                throw new ModelContextProtocol.McpException($"Run '{id:D}' was not found.");
            }

            return DebugEnvelope<BackgroundRunReceipt>.Create(
                new BackgroundRunReceipt("poll", true, id.ToString("D"), null),
                id.ToString("D"));
        }

        return DebugEnvelope<BackgroundRunReceipt>.Create(
            new BackgroundRunReceipt("poll", false, id.ToString("D"), result),
            id.ToString("D"));
    }

    private DebugEnvelope<BackgroundRunReceipt> Stop(Guid id)
    {
        if (!this.session.StopRun(id))
        {
            throw new ModelContextProtocol.McpException($"Run '{id:D}' was not found.");
        }

        RunResult? result = this.session.GetRunStatus(id);
        this.runs.Clear(id);
        this.logger.Information("Stopped background run {RunId}.", id);
        return DebugEnvelope<BackgroundRunReceipt>.Create(
            new BackgroundRunReceipt("stop", false, id.ToString("D"), result),
            id.ToString("D"));
    }

    private Guid RequireRunId(string? runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
        {
            if (!Guid.TryParse(runId, out Guid parsed))
            {
                throw new ModelContextProtocol.McpException($"Invalid run id '{runId}'.");
            }

            return parsed;
        }

        if (this.runs.Current is Guid active)
        {
            return active;
        }

        throw new ModelContextProtocol.McpException("No background run is active. Pass runId or start a run first.");
    }

    private void StopActiveRun()
    {
        if (this.runs.Current is not Guid runId)
        {
            return;
        }

        this.session.StopRun(runId);
        this.runs.Clear(runId);
    }
}