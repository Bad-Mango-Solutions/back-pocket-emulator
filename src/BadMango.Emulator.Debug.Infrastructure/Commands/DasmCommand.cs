// <copyright file="DasmCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Disassembles memory contents into assembly instructions.
/// </summary>
/// <remarks>
/// <para>
/// Supports multiple modes of operation:
/// - By byte count: dasm $1000 $20 (disassemble $20 bytes starting at $1000).
/// - By instruction count: dasm $1000 --instructions=10.
/// - By address range: dasm $1000 $1020 --range.
/// - From current PC: dasm (without address defaults to PC).
/// </para>
/// </remarks>
public sealed class DasmCommand : CommandHandlerBase, ICommandHelp
{
    private const int DefaultInstructionCount = 16;

    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DasmCommand"/> class.
    /// </summary>
    public DasmCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DasmCommand"/> class that reads the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public DasmCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("dasm", "Disassemble memory contents")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["d", "disasm", "u", "unassemble"];

    /// <inheritdoc/>
    public override string Usage => "dasm [address] [length|--instructions=N|--range end_address]";

    /// <inheritdoc/>
    public string Synopsis => "dasm [address] [length|--instructions=N|--range end_address]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Disassembles memory contents into assembly instructions. Supports multiple modes: " +
        "by instruction count (default), by byte count, or by address range. Without an " +
        "address argument, starts from the current PC. Shows address, bytes, and mnemonic " +
        "for each instruction.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } =
    [
        new("--instructions", null, "int", "Number of instructions to disassemble", "16"),
        new("--bytes", null, "int", "Number of bytes to disassemble", null),
        new("--range", null, "flag", "Treat second address as end address", "off"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "dasm                    Disassemble 16 instructions from PC",
        "dasm $1000              Disassemble from $1000",
        "dasm $1000 $1050 --range   Disassemble address range",
        "dasm $300 --instructions=5 Disassemble 5 instructions",
    ];

    /// <inheritdoc/>
    public string? SideEffects => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["mem", "pc", "step"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        var options = ParseOptions(debugContext, args);
        if (!options.Success)
        {
            return CommandResult.Error(options.ErrorMessage ?? "Invalid arguments.");
        }

        int count = options.UseInstructionCount
            ? options.InstructionCount
            : Math.Max(1, options.UseRange ? (int)(options.EndAddress - options.StartAddress) : options.ByteCount);

        try
        {
            IReadOnlyList<DisassemblyLine> lines = DebugSessions.Require(this.session, debugContext)
                .Disassemble(options.StartAddress, count);
            this.renderer.WriteDisassembly(debugContext.Output, lines);
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CommandResult.Error($"Disassembly error: {ex.Message}");
        }
    }

    private static DisasmOptions ParseOptions(IDebugContext debugContext, string[] args)
    {
        var options = new DisasmOptions();

        // Default to current PC if available, otherwise 0
        options.StartAddress = debugContext.Cpu?.GetPC() ?? 0;
        options.InstructionCount = DefaultInstructionCount;
        options.ByteCount = 32;
        options.UseInstructionCount = true; // Default mode
        options.Success = true;

        uint? firstAddress = null;
        uint? secondAddress = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--instructions=", StringComparison.OrdinalIgnoreCase))
            {
                var valueStr = arg["--instructions=".Length..];
                if (TryParseNumber(valueStr, out int count) && count > 0)
                {
                    options.InstructionCount = count;
                    options.UseInstructionCount = true;
                    options.UseRange = false;
                }
                else
                {
                    options.Success = false;
                    options.ErrorMessage = $"Invalid instruction count: '{valueStr}'";
                    return options;
                }
            }
            else if (arg.StartsWith("--bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var valueStr = arg["--bytes=".Length..];
                if (TryParseNumber(valueStr, out int count) && count > 0)
                {
                    options.ByteCount = count;
                    options.UseInstructionCount = false;
                    options.UseRange = false;
                }
                else
                {
                    options.Success = false;
                    options.ErrorMessage = $"Invalid byte count: '{valueStr}'";
                    return options;
                }
            }
            else if (arg.Equals("--range", StringComparison.OrdinalIgnoreCase))
            {
                options.UseRange = true;
                options.UseInstructionCount = false;
            }
            else if (TryParseAddress(arg, out uint address))
            {
                if (firstAddress is null)
                {
                    firstAddress = address;
                }
                else if (secondAddress is null)
                {
                    secondAddress = address;
                }
            }
            else
            {
                // Try as number (byte count or instruction count)
                if (TryParseNumber(arg, out int count) && count > 0)
                {
                    if (options.UseInstructionCount)
                    {
                        options.InstructionCount = count;
                    }
                    else
                    {
                        options.ByteCount = count;
                    }
                }
            }
        }

        // Process parsed addresses
        if (firstAddress is not null)
        {
            options.StartAddress = firstAddress.Value;
        }

        if (secondAddress is not null)
        {
            if (options.UseRange)
            {
                options.EndAddress = secondAddress.Value;
            }
            else
            {
                // Second positional could be byte count
                options.ByteCount = (int)secondAddress.Value;
                options.UseInstructionCount = false;
            }
        }

        // Validate range mode
        if (options.UseRange && options.EndAddress <= options.StartAddress)
        {
            options.EndAddress = options.StartAddress + 32; // Default range
        }

        return options;
    }

    private static bool TryParseAddress(string value, out uint result)
    {
        result = 0;

        if (value.StartsWith("$", StringComparison.Ordinal))
        {
            return uint.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        // Must have $ or 0x prefix to be treated as address to avoid ambiguity with counts
        return false;
    }

    private static bool TryParseNumber(string value, out int result)
    {
        result = 0;

        if (value.StartsWith("$", StringComparison.Ordinal))
        {
            return int.TryParse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private sealed class DisasmOptions
    {
        public uint StartAddress { get; set; }

        public uint EndAddress { get; set; }

        public int ByteCount { get; set; }

        public int InstructionCount { get; set; }

        public bool UseRange { get; set; }

        public bool UseInstructionCount { get; set; }

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }
    }
}