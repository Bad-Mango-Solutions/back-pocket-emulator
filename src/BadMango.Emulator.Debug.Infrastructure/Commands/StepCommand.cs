// <copyright file="StepCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Executes one or more CPU instructions in single-step mode.
/// </summary>
/// <remarks>
/// When invoked without arguments, executes a single instruction.
/// When invoked with a count, executes that many instructions.
/// Displays disassembly and register state after each step.
/// </remarks>
public sealed class StepCommand : CommandHandlerBase, ICommandHelp
{
    private const int DefaultStepCount = 1;
    private const int MaxStepCount = 10000;

    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StepCommand"/> class.
    /// </summary>
    public StepCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StepCommand"/> class that steps the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public StepCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("step", "Execute one or more CPU instructions")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["s", "si"];

    /// <inheritdoc/>
    public override string Usage => "step [count]";

    /// <inheritdoc/>
    public string Synopsis => "step [count]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Executes one or more CPU instructions in single-step mode. Without arguments, " +
        "executes a single instruction. With a count, executes that many instructions. " +
        "Displays the disassembled instruction before execution and reports the total " +
        "cycles consumed. Stops early if CPU halts or stop is requested.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "step                    Execute one instruction",
        "step 10                 Execute 10 instructions",
        "s                       Alias for step",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Modifies CPU registers (PC, A, X, Y, SP, P) and potentially memory based on " +
        "the executed instructions. May trigger I/O side effects.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["run", "stop", "regs", "pc"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (debugContext.Machine?.State == MachineState.Off)
        {
            return CommandResult.Error("Machine is off. Use 'power --on' (or 'boot') first.");
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

        int stepCount = DefaultStepCount;
        if (args.Length > 0)
        {
            if (!TryParseNumber(args[0], out stepCount))
            {
                return CommandResult.Error($"Invalid step count: '{args[0]}'. Expected a positive integer.");
            }

            if (stepCount < 1)
            {
                return CommandResult.Error("Step count must be at least 1.");
            }

            if (stepCount > MaxStepCount)
            {
                return CommandResult.Error($"Step count exceeds maximum ({MaxStepCount}). Use 'run' for larger counts.");
            }
        }

        try
        {
            ulong executed = DebugSessions.Require(this.session, debugContext).Step(stepCount);
            this.renderer.WriteStep(
                debugContext.Output,
                new StepReceipt(executed, debugContext.Cpu.GetPC()));
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return CommandResult.UsageError(ex.Message);
        }
    }

    private static bool TryParseNumber(string value, out int result)
    {
        // Try hex format first
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("$", StringComparison.Ordinal))
        {
            var hexValue = value.StartsWith("$", StringComparison.Ordinal) ? value[1..] : value[2..];
            return int.TryParse(hexValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}