// <copyright file="MapTraceCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;
using System.Text;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Shows or changes mapping trace.
/// </summary>
/// <remarks>
/// <para>
/// <c>maptrace</c> prints whether trace is on, the optional range, and the ring.
/// <c>maptrace on</c> enables remap and slow-path logging and leaves an existing range in place.
/// <c>maptrace off</c> clears the range, restores those pages, and then disables trace.
/// <c>maptrace range $start $end</c> turns trace on and forces every 4 KB page the range
/// intersects onto the slow path. <c>maptrace range clear</c> restores those pages and leaves
/// trace on.
/// </para>
/// <para>
/// With JSON output the result is one <c>emudbg/v1</c> envelope.
/// </para>
/// </remarks>
public sealed class MapTraceCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;
    private readonly IDebugOutputRenderer renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapTraceCommand"/> class.
    /// </summary>
    public MapTraceCommand()
        : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapTraceCommand"/> class.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    /// <param name="renderer">The output renderer. <see langword="null"/> writes text.</param>
    public MapTraceCommand(IDebugSession? session, IDebugOutputRenderer? renderer)
        : base("maptrace", "Show or set mapping trace and its slow-path range")
    {
        this.session = session;
        this.renderer = renderer ?? DebugOutputRenderer.PlainText;
    }

    /// <inheritdoc/>
    public override string Usage => "maptrace [on|off|range <start> <end>|range clear]";

    /// <inheritdoc/>
    public string Synopsis => "maptrace [on|off|range <start> <end>|range clear]";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Shows or sets mapping trace. With no argument, prints whether trace is on, the address " +
        "range, and the fixed ring. 'maptrace on' records remaps and slow-path accesses and does " +
        "not change a range. 'maptrace range $start $end' turns trace on and forces every 4 KB page " +
        "the range intersects onto the slow path. 'maptrace range clear' restores those pages and " +
        "leaves trace on. 'maptrace off' clears the range, restores those pages, and then disables " +
        "trace. Fast-path accesses are not recorded. With JSON output the result is one emudbg/v1 envelope.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "maptrace                 Print trace state, range, and the ring",
        "maptrace on              Record remaps and slow-path accesses",
        "maptrace range $0800 $08FF  Force the $0800 page onto the slow path",
        "maptrace range clear     Restore natural fast-path ops and keep trace on",
        "maptrace off             Clear the range and disable trace",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "maptrace on, off, and range change which pages stay on the fast path and whether the ring records.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["fault", "buslog", "switches"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        IDebugSession active = DebugSessions.Require(this.session, debugContext);
        try
        {
            if (!TryApply(active, debugContext, args, out string? error))
            {
                return CommandResult.Error(error ?? Usage);
            }

            MapTraceReceipt receipt = Describe(active, debugContext);
            this.renderer.WriteMapTrace(debugContext.Output, receipt, Format(receipt));
            return CommandResult.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return CommandResult.Error(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return CommandResult.Error(ex.Message);
        }
    }

    private static bool TryApply(IDebugSession session, IDebugContext context, string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0)
        {
            return true;
        }

        if (args.Length == 1)
        {
            if (args[0].Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                session.MappingTraceEnabled = true;
                return true;
            }

            if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                session.ClearMappingTraceRange();
                session.MappingTraceEnabled = false;
                return true;
            }

            error = "Usage: maptrace [on|off|range <start> <end>|range clear]";
            return false;
        }

        if (!args[0].Equals("range", StringComparison.OrdinalIgnoreCase))
        {
            error = "Usage: maptrace [on|off|range <start> <end>|range clear]";
            return false;
        }

        if (args.Length == 2 && args[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            session.ClearMappingTraceRange();
            return true;
        }

        if (args.Length == 3
            && AddressParser.TryParse(args[1], context.Machine, out uint start)
            && AddressParser.TryParse(args[2], context.Machine, out uint end))
        {
            session.SetMappingTraceRange(start, end);
            return true;
        }

        error = "Usage: maptrace range <start> <end> | maptrace range clear";
        return false;
    }

    private static MapTraceReceipt Describe(IDebugSession session, IDebugContext context)
    {
        uint? start = null;
        uint? end = null;
        if (session.TryGetMappingTraceRange(out uint rangeStart, out uint rangeEnd))
        {
            start = rangeStart;
            end = rangeEnd;
        }

        IReadOnlyList<MapTraceLine> lines = [];
        if (context.Bus?.MappingTrace is not null)
        {
            lines = Project(context.Bus.MappingTrace.Snapshot());
        }

        return new MapTraceReceipt(session.MappingTraceEnabled, start, end, lines);
    }

    private static MapTraceLine[] Project(MappingTraceRecord[] records)
    {
        var lines = new MapTraceLine[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            MappingTraceRecord record = records[i];
            IReadOnlyList<MapTraceSwitch>? switches = null;
            if (record.SoftSwitches is not null)
            {
                var projected = new MapTraceSwitch[record.SoftSwitches.Count];
                for (int s = 0; s < record.SoftSwitches.Count; s++)
                {
                    SoftSwitchState state = record.SoftSwitches[s];
                    projected[s] = new MapTraceSwitch(state.Name, state.Address, state.Value);
                }

                switches = projected;
            }

            lines[i] = new MapTraceLine(
                record.Kind == MappingTraceKind.Remap ? "remap" : "access",
                record.Cycle,
                record.Address,
                record.Kind == MappingTraceKind.Access ? record.Intent.ToString() : string.Empty,
                record.TargetName,
                record.Region.ToString(),
                record.PhysicalBase,
                record.DeviceId,
                record.PageStart,
                record.PageEnd,
                record.Kind == MappingTraceKind.Remap ? record.Perms.ToString() : string.Empty,
                record.Kind == MappingTraceKind.Remap ? record.NaturalFastOps.ToString() : string.Empty,
                switches);
        }

        return lines;
    }

    private static string Format(MapTraceReceipt receipt)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Mapping trace: {(receipt.Enabled ? "on" : "off")}");
        text.AppendLine();
        if (receipt.RangeStart is uint start && receipt.RangeEnd is uint end)
        {
            text.Append(CultureInfo.InvariantCulture, $"Range: {Hex(start)}-{Hex(end)}");
        }
        else
        {
            text.Append("Range: none");
        }

        text.AppendLine();
        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"  Records: {receipt.Records.Count}");
        text.AppendLine();
        foreach (MapTraceLine line in receipt.Records)
        {
            text.AppendLine();
            if (line.Kind == "remap")
            {
                text.Append(CultureInfo.InvariantCulture, $"  remap {Hex(PageAddress(line.PageStart))}-{Hex(PageAddress(line.PageEnd) + 0xFFF)}");
                text.Append(CultureInfo.InvariantCulture, $" target {line.TargetName ?? "(none)"}");
                text.Append(CultureInfo.InvariantCulture, $" base {Hex(line.PhysicalBase)}");
                text.Append(CultureInfo.InvariantCulture, $" perms {line.Perms} fast {line.NaturalFastOps}");
                if (line.SoftSwitches is not null)
                {
                    foreach (MapTraceSwitch state in line.SoftSwitches)
                    {
                        text.AppendLine();
                        text.Append(CultureInfo.InvariantCulture, $"    {state.Name} {Hex(state.Address)} = {(state.Value ? "1" : "0")}");
                    }
                }
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"  access {Hex(line.Address)} {line.Intent}");
                text.Append(CultureInfo.InvariantCulture, $" target {line.TargetName ?? "(none)"}");
                text.Append(CultureInfo.InvariantCulture, $" base {Hex(line.PhysicalBase)}");
                text.Append(CultureInfo.InvariantCulture, $" region {line.Region} device {line.DeviceId}");
                text.Append(CultureInfo.InvariantCulture, $" page {Hex(PageAddress(line.PageStart))} cycle {line.Cycle}");
            }
        }

        return text.ToString();
    }

    private static uint PageAddress(int pageIndex) => (uint)pageIndex << 12;

    private static string Hex(uint address) =>
        address <= 0xFFFF
            ? string.Create(CultureInfo.InvariantCulture, $"${address:X4}")
            : string.Create(CultureInfo.InvariantCulture, $"${address:X8}");
}