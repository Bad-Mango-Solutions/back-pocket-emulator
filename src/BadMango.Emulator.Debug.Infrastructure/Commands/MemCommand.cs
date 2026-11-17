// <copyright file="MemCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Displays a hex dump of memory contents.
/// </summary>
/// <remarks>
/// Shows memory contents in a traditional hex dump format with:
/// - Address column on the left
/// - Hex bytes in the middle (16 bytes per line)
/// - ASCII representation on the right.
/// </remarks>
public sealed class MemCommand : CommandHandlerBase, ICommandHelp
{
    private const int DefaultByteCount = 256;
    private const int MaxByteCount = 65536;

    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemCommand"/> class.
    /// </summary>
    public MemCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MemCommand"/> class that reads the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public MemCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("mem", "Display memory contents as hex dump")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["m", "dump", "hexdump"];

    /// <inheritdoc/>
    public override string Usage => "mem <address> [length]";

    /// <inheritdoc/>
    public string Synopsis => "mem <address> [length]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Displays memory contents in traditional hex dump format with address column, " +
        "hex bytes (16 per line), and ASCII representation. Uses DebugRead intent for " +
        "side-effect-free access. Defaults to 256 bytes if length not specified. " +
        "Maximum displayable length is 65536 bytes.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "mem $300                 Display 256 bytes starting at $0300",
        "mem $800 64              Display 64 bytes starting at $0800",
        "mem 0x6000 $100          Display 256 bytes starting at $6000",
    ];

    /// <inheritdoc/>
    public string? SideEffects => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["peek", "poke", "read"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (args.Length == 0)
        {
            return CommandResult.Error("Address required. Usage: mem <address> [length]");
        }

        if (!TryParseAddress(args[0], out uint startAddress))
        {
            return CommandResult.Error($"Invalid address: '{args[0]}'. Use hex format ($1234 or 0x1234) or decimal.");
        }

        int byteCount = DefaultByteCount;
        if (args.Length > 1 && !TryParseLength(args[1], out byteCount))
        {
            return CommandResult.Error($"Invalid length: '{args[1]}'. Expected a positive integer.");
        }

        if (byteCount < 1)
        {
            byteCount = 1;
        }

        if (byteCount > MaxByteCount)
        {
            byteCount = MaxByteCount;
        }

        if (debugContext.Bus is { } bus)
        {
            uint memorySize = (uint)bus.PageCount << bus.PageShift;
            if (startAddress >= memorySize)
            {
                return CommandResult.Error($"Address ${startAddress:X4} is out of range (memory size: ${memorySize:X4}).");
            }

            if (startAddress + (uint)byteCount > memorySize)
            {
                byteCount = (int)(memorySize - startAddress);
            }
        }

        try
        {
            MemoryBlock block = DebugSessions.Require(this.session, debugContext).ReadMemory(startAddress, byteCount);
            this.renderer.WriteMemory(debugContext.Output, block);
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
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

        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseLength(string value, out int result)
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
}