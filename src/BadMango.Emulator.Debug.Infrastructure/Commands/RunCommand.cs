// <copyright file="RunCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Runs the CPU until it halts or reaches a limit.
/// </summary>
/// <remarks>
/// <para>
/// Executes instructions continuously until:
/// - The CPU halts (STP, or WAI when no interrupt will be delivered).
/// - The instruction limit is reached.
/// - The cycle limit is reached.
/// - A stop is requested (via StopCommand or externally).
/// </para>
/// <para>
/// Guards against infinite loops by enforcing configurable limits.
/// Optional logging can trace execution for debugging purposes.
/// </para>
/// </remarks>
public sealed class RunCommand : ExecutionCommandBase
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommand"/> class.
    /// </summary>
    public RunCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommand"/> class that runs the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public RunCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("run", "Run CPU until halt or limit reached")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["g", "go"];

    /// <inheritdoc/>
    public override string Usage => "run [instruction_limit] [--cycles=<limit>] [--trace] [--trace-file=<path>] [--trace-buffer]";

    /// <inheritdoc/>
    public override string Synopsis => "run [limit] [options]";

    /// <inheritdoc/>
    public override string DetailedDescription =>
        "Executes CPU instructions continuously until the processor halts (STP, or WAI " +
        "when no attached machine can deliver an interrupt), " +
        "a breakpoint is hit, or a stop is requested. By default no instruction, cycle, " +
        "or timeout limit is applied — execution continues until externally stopped. " +
        "Use --instructions=<n>, --cycles=<n>, or --timeout=<ms> to enforce explicit limits. " +
        "Optional tracing captures execution history for debugging.";

    /// <inheritdoc/>
    public override IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--trace", "-t", "flag", "Enable instruction tracing", "off"),
        new("--trace-buffer", "-tb", "flag", "Buffer trace output instead of streaming", "off"),
        new("--trace-file", null, "path", "Write trace output to specified file", null),
        new("--trace-last", null, "int", "Show only last N trace records", "100"),
        new("--trace-buffer-size", null, "int", "Maximum buffered trace records", "10000"),
        new("--cycles", null, "int", "Maximum cycles to execute", "unlimited"),
        new("--instructions", null, "int", "Maximum instructions to execute", "unlimited"),
        new("--timeout", null, "int", "Maximum wall-clock milliseconds to execute", "unlimited"),
    ];

    /// <inheritdoc/>
    public override IReadOnlyList<string> Examples { get; } =
    [
        "run                          Execute until halt, breakpoint, or stop",
        "run 1000                     Execute up to 1000 instructions",
        "run --cycles=50000           Execute up to 50,000 cycles",
        "run --trace                  Execute with instruction tracing",
        "run --trace-buffer --trace-last=50   Buffer and show last 50 instructions",
    ];

    /// <inheritdoc/>
    public override string? SideEffects =>
        "Modifies CPU state (PC, registers, flags). May modify memory and trigger " +
        "I/O device state changes depending on executed code.";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SeeAlso { get; } = ["step", "stop", "call", "reset"];

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
            return CommandResult.Error("No CPU attached to debug context.");
        }

        if (debugContext.Cpu.Halted)
        {
            return CommandResult.Error("CPU is halted. Use 'reset' to restart.");
        }

        // The run loop owns the CPU while the machine is running; stepping it from the REPL
        // thread at the same time would execute one CPU on two threads.
        if (debugContext.Machine?.State == MachineState.Running)
        {
            return CommandResult.Error("Machine is running. Use 'pause' or 'stop' first.");
        }

        // Parse options
        var options = ParseRunOptions(args);

        if (debugContext.Machine is not null)
        {
            return this.ExecuteOnSession(debugContext, options);
        }

        debugContext.Output.WriteLine($"Running from PC=${debugContext.Cpu.GetPC():X4}...");

        // CPU-only contexts have no machine for IDebugSession.RunUntil. Keep the local loop.
        var result = ExecuteInstructionLoop(
            debugContext,
            options,
            (_, _) => false); // Never terminate early - rely on halt detection

        debugContext.Output.WriteLine();
        debugContext.Output.WriteLine($"Stopped: {result.StopReason}");
        debugContext.Output.WriteLine($"  Instructions executed: {result.InstructionCount:N0}");
        debugContext.Output.WriteLine($"  Cycles consumed: {result.CycleCount:N0}");
        debugContext.Output.WriteLine($"  Final PC = ${debugContext.Cpu.GetPC():X4}");

        // Output buffered trace if requested
        if (options.EnableTrace && options.BufferTrace && debugContext.TracingListener is not null)
        {
            OutputBufferedTrace(debugContext, debugContext.TracingListener, options.TraceLastN);
        }

        return CommandResult.Ok();
    }

    private static ExecutionOptions ParseRunOptions(string[] args)
    {
        // RunCommand should default to unlimited execution. Start from the unlimited
        // baseline and let ParseCommonOptions populate any explicit user limits.
        var options = new ExecutionOptions
        {
            InstructionLimit = int.MaxValue,
            CycleLimit = long.MaxValue,
            TimeoutMs = 0,
        };

        ApplyCommonOptions(args, options);

        // Parse positional argument as instruction limit
        foreach (var arg in args)
        {
            if (!arg.StartsWith("-", StringComparison.Ordinal) &&
                TryParseNumber(arg, out long limit) &&
                limit <= int.MaxValue)
            {
                options.InstructionLimit = (int)limit;
                break;
            }
        }

        return options;
    }

    private CommandResult ExecuteOnSession(IDebugContext debugContext, ExecutionOptions options)
    {
        IDebugSession active = DebugSessions.Require(this.session, debugContext);
        ulong budget = this.renderer.DefaultMaxCycles
            ?? (options.CycleLimit > 0 && options.CycleLimit != long.MaxValue
                ? (ulong)options.CycleLimit
                : ulong.MaxValue);

        if (options.EnableTrace && debugContext.TracingListener is not null)
        {
            ConfigureTracing(debugContext, debugContext.TracingListener, options);
        }

        try
        {
            if (options.InstructionLimit != int.MaxValue)
            {
                ulong executed = active.Step(options.InstructionLimit);
                var cpu = debugContext.Cpu!;
                if (!this.renderer.JsonOutput)
                {
                    debugContext.Output.WriteLine(cpu.Halted
                        ? $"CPU halted ({cpu.HaltReason})"
                        : "instruction limit reached");
                }

                this.renderer.WriteStep(debugContext.Output, new StepReceipt(executed, cpu.GetPC()));
                return CommandResult.Ok();
            }

            TimeSpan? wall = options.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(options.TimeoutMs) : null;
            RunResult run = active.RunUntil(
                new RunCondition(RunConditionKind.CycleCount, CycleLimit: ulong.MaxValue),
                new RunBudget(budget, wall));
            this.renderer.WriteRun(debugContext.Output, run);
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CommandResult.UsageError(ex.Message);
        }
    }
}