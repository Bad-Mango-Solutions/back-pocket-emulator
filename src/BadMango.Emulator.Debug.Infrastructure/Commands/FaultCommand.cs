// <copyright file="FaultCommand.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure.Commands;

using System.Globalization;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Shows details about the most recent bus fault.
/// </summary>
/// <remarks>
/// <para>
/// Displays information about the last bus fault that occurred, including the
/// faulting address, fault kind, and any additional context available.
/// </para>
/// <para>
/// Bus fault recording is off by default; the first use of this command
/// (or <c>buslog</c>) enables it, so only faults from that point on are shown.
/// </para>
/// <para>
/// This command requires a bus to be attached to the debug context.
/// </para>
/// </remarks>
public sealed class FaultCommand : CommandHandlerBase, ICommandHelp
{
    private readonly IDebugSession? session;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultCommand"/> class.
    /// </summary>
    public FaultCommand()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FaultCommand"/> class that reads the shared session.
    /// </summary>
    /// <param name="session">The debug session. <see langword="null"/> uses the session bound to the context.</param>
    public FaultCommand(IDebugSession? session)
        : base("fault", "Show most recent bus fault details")
    {
        this.session = session;
    }

    /// <inheritdoc/>
    public override string Usage => "fault";

    /// <inheritdoc/>
    public string Synopsis => "fault";

    /// <inheritdoc/>
    public string DetailedDescription =>
        "Displays the latched fault status and fault address, then the most recent " +
        "bus fault recorded by the memory bus. Useful for debugging unmapped memory " +
        "access, permission violations, and silent floating-bus reads. Shows 'No fault' " +
        "if no fault has occurred since the ring was last cleared. Recording is off by " +
        "default; the first use of 'fault' or 'buslog' turns it on, so only faults from " +
        "that point on are captured. The registers stay latched even when recording is off.";

    /// <inheritdoc/>
    public IReadOnlyList<CommandOption> Options { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<string> Examples { get; } =
    [
        "fault                    Show last bus fault status",
    ];

    /// <inheritdoc/>
    public string? SideEffects =>
        "Enables bus fault recording if it is not already on.";

    /// <inheritdoc/>
    public IReadOnlyList<string> SeeAlso { get; } = ["buslog", "regions", "pages"];

    /// <inheritdoc/>
    public override CommandResult Execute(ICommandContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context is not IDebugContext debugContext)
        {
            return CommandResult.Error("Debug context required for this command.");
        }

        if (!debugContext.IsBusAttached || debugContext.Bus is null)
        {
            return CommandResult.Error("No bus attached. This command requires a bus-based system.");
        }

        IDebugSession active = DebugSessions.Require(this.session, debugContext);
        FaultKind status = (FaultKind)active.FaultStatus;
        uint faultAddress = active.FaultAddress;

        var ring = debugContext.Bus.FaultRing;
        if (ring is null)
        {
            debugContext.Output.WriteLine("Bus Fault Status:");
            debugContext.Output.WriteLine();
            WriteRegisters(debugContext, status, faultAddress);
            debugContext.Output.WriteLine();
            debugContext.Output.WriteLine("  No fault ring is configured for this bus.");
            return CommandResult.Ok();
        }

        BusLogCommand.EnsureRecordingEnabled(debugContext.Bus, debugContext.Output);

        debugContext.Output.WriteLine("Bus Fault Status:");
        debugContext.Output.WriteLine();
        WriteRegisters(debugContext, status, faultAddress);
        debugContext.Output.WriteLine();
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Ring capacity:    {ring.Capacity}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Faults in buffer: {ring.Count}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Total recorded:   {ring.TotalFaults}"));
        debugContext.Output.WriteLine();

        var last = ring.Last;
        if (last is null)
        {
            debugContext.Output.WriteLine("  No fault recorded.");
            return CommandResult.Ok();
        }

        var f = last.Value;
        debugContext.Output.WriteLine("Most recent fault:");
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Kind:       {f.Kind}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Address:    ${f.Address:X4}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Width:      {f.WidthBits} bits"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Intent:     {f.Intent}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Mode:       {f.Mode}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  DeviceId:   {f.DeviceId}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Region:     {f.RegionTag}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  SourceId:   {f.SourceId}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Cycle:      {f.Cycle}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Pc:         ${f.Pc:X4}"));

        return CommandResult.Ok();
    }

    /// <summary>
    /// Writes the latched fault status and address.
    /// </summary>
    /// <param name="debugContext">The context whose output receives the lines.</param>
    /// <param name="status">The latched fault status.</param>
    /// <param name="faultAddress">The latched fault address.</param>
    private static void WriteRegisters(IDebugContext debugContext, FaultKind status, uint faultAddress)
    {
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Status:         {status}"));
        debugContext.Output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Address:        ${faultAddress:X4}"));
    }
}