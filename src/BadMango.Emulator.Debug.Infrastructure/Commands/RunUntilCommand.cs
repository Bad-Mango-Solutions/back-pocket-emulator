// <copyright file="RunUntilCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Runs the emulator until a condition is met.
/// </summary>
public sealed class RunUntilCommand : CommandHandlerBase, ICommandHelp
{
    private const ulong DefaultMaxCycles = 10_000_000;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunUntilCommand"/> class.
    /// </summary>
    public RunUntilCommand()
        : base("run-until", "Run until a stop condition is reached")
    {
    }

    /// <inheritdoc/>
    public override string Usage => "run-until <address|bp|watch|mem <addr> <byte>|--until-cycles=<n>> [--max-cycles=<n>]";

    /// <inheritdoc/>
    public string Synopsis => "run-until <condition> [options]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Runs the machine until the supplied condition is true. Supports PC address, breakpoint, " +
        "watchpoint, memory match, or cycle-target conditions. Use --max-cycles to bound execution.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--until-cycles", null, "int", "Stop once this many cycles have elapsed", null),
        new("--max-cycles", null, "int", "Execution safety budget", DefaultMaxCycles.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "run-until $0801",
        "run-until bp",
        "run-until watch",
        "run-until mem $C030 01",
        "run-until --until-cycles=50000 --max-cycles=100000",
    ];

    /// <inheritdoc/>
    public string? SideEffects => "Advances machine execution and mutates CPU/memory/device state.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["run", "step", "bp", "watch"];

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

        if (args.Length == 0)
        {
            return CommandResult.Error($"Usage: {this.Usage}");
        }

        RunCondition? condition = null;
        ulong maxCycles = DefaultMaxCycles;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.StartsWith("--max-cycles=", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(arg["--max-cycles=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedMaxCycles))
                {
                    maxCycles = parsedMaxCycles;
                }

                continue;
            }

            if (arg.Equals("--max-cycles", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (ulong.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedMaxCycles))
                {
                    maxCycles = parsedMaxCycles;
                }

                continue;
            }

            if (arg.StartsWith("--until-cycles=", StringComparison.OrdinalIgnoreCase))
            {
                if (!ulong.TryParse(arg["--until-cycles=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong cycleLimit))
                {
                    return CommandResult.Error($"Invalid cycle value: '{arg}'.");
                }

                condition = new(RunConditionKind.CycleCount, CycleLimit: cycleLimit);
                continue;
            }

            if (arg.Equals("bp", StringComparison.OrdinalIgnoreCase))
            {
                condition = new(RunConditionKind.Breakpoint);
                continue;
            }

            if (arg.Equals("watch", StringComparison.OrdinalIgnoreCase))
            {
                condition = new(RunConditionKind.Watchpoint);
                continue;
            }

            if (arg.Equals("mem", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 >= args.Length)
                {
                    return CommandResult.Error("Usage: run-until mem <address> <byte>");
                }

                if (!AddressParser.TryParse(args[++i], debugContext.Machine, out uint memoryAddress))
                {
                    return CommandResult.Error($"Invalid memory address: '{args[i]}'.");
                }

                if (!TryParseByte(args[++i], out byte expectedByte))
                {
                    return CommandResult.Error($"Invalid byte value: '{args[i]}'.");
                }

                condition = new(RunConditionKind.MemoryEquals, MemoryAddress: memoryAddress, MemoryValue: [expectedByte]);
                continue;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                if (!AddressParser.TryParse(arg, debugContext.Machine, out uint programCounter))
                {
                    return CommandResult.Error($"Invalid run-until condition: '{arg}'.");
                }

                condition = new(RunConditionKind.ProgramCounter, Address: programCounter);
            }
        }

        if (condition is null)
        {
            return CommandResult.Error("Run condition required.");
        }

        var session = new MachineDebugSession(debugContext);
        RunResult result;
        try
        {
            result = session.RunUntil(condition, new RunBudget(MaxCycles: maxCycles));
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }

        debugContext.Output.WriteLine($"Stopped: {result.StopReason}");
        debugContext.Output.WriteLine($"  Instructions executed: {result.InstructionCount:N0}");
        debugContext.Output.WriteLine($"  Cycles consumed: {result.CycleCount:N0}");
        debugContext.Output.WriteLine($"  Final PC = ${result.ProgramCounter:X4}");

        return CommandResult.Ok();
    }

    private static bool TryParseByte(string value, out byte parsed)
    {
        parsed = 0;

        if (value.StartsWith("$", StringComparison.Ordinal))
        {
            return byte.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return byte.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
}