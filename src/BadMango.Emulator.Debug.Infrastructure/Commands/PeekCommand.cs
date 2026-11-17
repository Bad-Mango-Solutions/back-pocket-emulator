// <copyright file="PeekCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Reads memory without side effects (debug/peek access).
/// </summary>
/// <remarks>
/// <para>
/// Performs a side-effect-free read from memory using DebugRead intent. This will
/// not trigger soft switches or I/O device behavior. Output is in hex format only.
/// </para>
/// <para>
/// Use <c>peek</c> when you want to inspect memory without affecting emulation state.
/// For side-effectful reads (like actual hardware behavior), use <c>read</c> instead.
/// </para>
/// <para>
/// <strong>No Side Effects:</strong> This command does not modify any emulation state.
/// </para>
/// </remarks>
public sealed class PeekCommand : CommandHandlerBase, ICommandHelp
{
    private const int DefaultByteCount = 1;
    private const int MaxByteCount = 256;

    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeekCommand"/> class.
    /// </summary>
    public PeekCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PeekCommand"/> class that reads the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public PeekCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("peek", "Read memory without side effects (hex output)")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<string> Aliases { get; } = ["p"];

    /// <inheritdoc/>
    public override string Usage => "peek <address> [count]";

    /// <inheritdoc/>
    public string Synopsis => "peek <address> [count]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Performs a side-effect-free read from memory using DebugRead intent. This " +
        "bypasses I/O handlers and soft switches, returning the raw memory value. " +
        "Output is in hex format only. Use this for safe memory inspection without " +
        "affecting emulation state. For side-effectful reads, use 'read' instead. " +
        "Addresses can be specified as hex ($C000, 0xC000), decimal, or soft switch " +
        "names registered by the current machine (e.g., SPEAKER, KBD, KBDSTRB).";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "peek $C000               Read keyboard data without triggering strobe",
        "peek KBD                  Same as above using soft switch name",
        "peek $300 16             Read 16 bytes starting at $0300",
        "peek 0x6000              Read a single byte from $6000",
        "peek SPEAKER              Peek at speaker address (no click)",
    ];

    /// <inheritdoc/>
    public string? SideEffects => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["read", "poke", "mem", "switches"];

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
            return CommandResult.Error("Address required. Usage: peek <address> [count]");
        }

        if (!AddressParser.TryParse(args[0], debugContext.Machine, out uint address))
        {
            return CommandResult.Error($"Invalid address: '{args[0]}'. Use {AddressParser.GetFormatDescription()}.");
        }

        int count = DefaultByteCount;
        if (args.Length > 1 && !AddressParser.TryParseCount(args[1], out count))
        {
            return CommandResult.Error($"Invalid count: '{args[1]}'. Expected a positive integer.");
        }

        count = Math.Clamp(count, 1, MaxByteCount);

        if (debugContext.Bus is { } bus)
        {
            uint memorySize = (uint)bus.PageCount << bus.PageShift;
            if (address >= memorySize)
            {
                return CommandResult.Error($"Address ${address:X4} is out of range (memory size: ${memorySize:X4}).");
            }

            if (address + (uint)count > memorySize)
            {
                count = (int)(memorySize - address);
            }
        }

        try
        {
            MemoryBlock block = DebugSessions.Require(this.session, debugContext).ReadMemory(address, count);
            this.renderer.WriteMemory(debugContext.Output, block);
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
    }
}