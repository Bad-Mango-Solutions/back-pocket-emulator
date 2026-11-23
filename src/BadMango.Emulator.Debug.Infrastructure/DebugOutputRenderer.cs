// <copyright file="DebugOutputRenderer.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Renders debug command results as text or as an <c>emudbg/v1</c> envelope.
/// </summary>
/// <remarks>
/// JSON is produced only through <see cref="DebugSessionJsonContext"/>. The renderer does not
/// serialize anonymous objects and does not use reflection-based JSON.
/// </remarks>
public sealed class DebugOutputRenderer : IDebugOutputRenderer
{
    private static readonly (FaultKind Mask, string Name)[] KnownFaultNames =
    [
        (FaultKind.Unmapped, nameof(FaultKind.Unmapped)),
        (FaultKind.Permission, nameof(FaultKind.Permission)),
        (FaultKind.Nx, nameof(FaultKind.Nx)),
        (FaultKind.Misaligned, nameof(FaultKind.Misaligned)),
        (FaultKind.DeviceFault, nameof(FaultKind.DeviceFault)),
        (FaultKind.Write, nameof(FaultKind.Write)),
        (FaultKind.Fetch, nameof(FaultKind.Fetch)),
        (FaultKind.BankCrossing, nameof(FaultKind.BankCrossing)),
        (FaultKind.Privilege, nameof(FaultKind.Privilege)),
    ];

    private static readonly ushort KnownFaultMask = (ushort)(
        FaultKind.Unmapped |
        FaultKind.Permission |
        FaultKind.Nx |
        FaultKind.Misaligned |
        FaultKind.DeviceFault |
        FaultKind.Write |
        FaultKind.Fetch |
        FaultKind.BankCrossing |
        FaultKind.Privilege);

    private readonly Func<DateTimeOffset> clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugOutputRenderer"/> class.
    /// </summary>
    /// <param name="options">Startup options. <see langword="null"/> selects text output.</param>
    /// <param name="envelopeClock">Clock used to stamp envelopes. <see langword="null"/> uses UTC now.</param>
    public DebugOutputRenderer(EmudbgOptions? options = null, Func<DateTimeOffset>? envelopeClock = null)
    {
        this.JsonOutput = options?.JsonOutput == true;
        this.DefaultMaxCycles = options?.MaxCycles;
        this.clock = envelopeClock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Gets a text renderer with no cycle budget.
    /// </summary>
    public static IDebugOutputRenderer PlainText { get; } = new DebugOutputRenderer();

    /// <inheritdoc/>
    public bool JsonOutput { get; }

    /// <inheritdoc/>
    public ulong? DefaultMaxCycles { get; }

    /// <summary>
    /// Formats a bus fault kind as the named low-byte bitmask, preserving unknown bits.
    /// </summary>
    /// <param name="fault">The bus fault to format.</param>
    /// <returns>A pipe-delimited list of known fault names and any remaining unknown mask bits.</returns>
    public static string FormatFault(BusFault fault)
    {
        return FormatFaultBits((ushort)fault.Kind);
    }

    /// <summary>
    /// Formats a raw fault mask as the named low-byte bitmask, preserving unknown bits.
    /// </summary>
    /// <param name="mask">The raw fault mask.</param>
    /// <returns>A pipe-delimited list of known fault names and any remaining unknown mask bits.</returns>
    public static string FormatFaultBits(ushort mask)
    {
        var names = new List<string>();
        foreach ((FaultKind kind, string name) in KnownFaultNames)
        {
            if ((mask & (ushort)kind) != 0)
            {
                names.Add(name);
            }
        }

        ushort unknownBits = (ushort)(mask & ~KnownFaultMask);
        if (unknownBits != 0)
        {
            names.Add($"unknown 0x{unknownBits:X4}");
        }

        if (names.Count == 0)
        {
            return "No fault";
        }

        return string.Join("|", names);
    }

    /// <inheritdoc/>
    public void WriteRegisters(TextWriter output, RegisterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, snapshot, DebugSessionJsonContext.Default.DebugEnvelopeRegisterSnapshot);
            return;
        }

        output.WriteLine("CPU Registers");
        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  PC = ${0:X4}    SP = ${1:X4}",
            snapshot.ProgramCounter,
            snapshot.StackPointer));
        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  A = ${0:X2}    X = ${1:X2}    Y = ${2:X2}",
            snapshot.Accumulator,
            snapshot.XRegister,
            snapshot.YRegister));
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  P = ${0:X2}", snapshot.StatusRegister));
    }

    /// <inheritdoc/>
    public void WriteMemory(TextWriter output, MemoryBlock block)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(block);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, block, DebugSessionJsonContext.Default.DebugEnvelopeMemoryBlock);
            return;
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Memory dump: ${0:X4} ({1} bytes)",
            block.Address,
            block.Length));

        static bool Unreadable(MemoryBlock source, int index) =>
            source.Faults is not null && index < source.Faults.Count && source.Faults[index] != 0;

        static string FaultText(byte mask)
        {
            if (mask == 0)
            {
                return "No fault";
            }

            string formatted = FormatFaultBits(mask);
            if (formatted == "No fault")
            {
                return string.Format(CultureInfo.InvariantCulture, "Fault mask {0:X2}", mask);
            }

            return formatted;
        }

        var ascii = new StringBuilder();
        var hex = new StringBuilder();
        int faultCount = 0;
        for (int i = 0; i < block.Data.Count; i++)
        {
            if (i % 16 == 0)
            {
                if (i > 0)
                {
                    output.WriteLine(string.Format(CultureInfo.InvariantCulture, "${0:X4}: {1} |{2}|", block.Address + (uint)(i - 16), hex, ascii));
                    hex.Clear();
                    ascii.Clear();
                }
            }
            else if (hex.Length > 0)
            {
                hex.Append(' ');
            }

            if (Unreadable(block, i))
            {
                faultCount++;
                hex.Append("??");
                ascii.Append('?');
            }
            else
            {
                byte value = block.Data[i];
                hex.Append(value.ToString("X2", CultureInfo.InvariantCulture));
                ascii.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
            }
        }

        if (hex.Length > 0)
        {
            int remainder = block.Data.Count % 16;
            int lineStart = remainder == 0 ? block.Data.Count - 16 : block.Data.Count - remainder;
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "${0:X4}: {1} |{2}|", block.Address + (uint)lineStart, hex, ascii));
        }

        if (faultCount > 0 && block.Faults is not null)
        {
            output.WriteLine();
            output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Bus faults encountered ({0}):", faultCount));
            for (int i = 0; i < block.Faults.Count; i++)
            {
                byte kind = block.Faults[i];
                if (kind == 0)
                {
                    continue;
                }

                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  ${0:X4}: {1}",
                    block.Address + (uint)i,
                    FaultText(kind)));
            }
        }
    }

    /// <summary>
    /// Writes a disassembly listing.
    /// </summary>
    /// <param name="output">Output writer.</param>
    /// <param name="lines">Disassembly lines to emit.</param>
    public void WriteDisassembly(TextWriter output, IReadOnlyList<DisassemblyLine> lines)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(lines);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, lines, DebugSessionJsonContext.Default.DebugEnvelopeIReadOnlyListDisassemblyLine);
            return;
        }

        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Disassembly: {0} instruction(s)", lines.Count));
        foreach (DisassemblyLine line in lines)
        {
            string operand = string.IsNullOrEmpty(line.Operand) ? string.Empty : " " + line.Operand;
            output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "${0:X4}:  {1,-12} {2}{3}",
                line.Address,
                line.Bytes,
                line.Mnemonic,
                operand));
        }
    }

    /// <inheritdoc/>
    public void WriteRun(TextWriter output, RunResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, result, DebugSessionJsonContext.Default.DebugEnvelopeRunResult);
            return;
        }

        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "Stopped: {0}", result.StopReason));
        if (!string.IsNullOrEmpty(result.Message))
        {
            output.WriteLine(result.Message);
        }

        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Instructions executed: {0:N0}", result.InstructionCount));
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Cycles consumed: {0:N0}", result.CycleCount));
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Final PC = ${0:X4}", result.ProgramCounter));
    }

    /// <inheritdoc/>
    public void WriteStep(TextWriter output, StepReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(receipt);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, receipt, DebugSessionJsonContext.Default.DebugEnvelopeStepReceipt);
            return;
        }

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Executed {0} instruction(s).",
            receipt.InstructionsExecuted));
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "PC = ${0:X4}", receipt.ProgramCounter));
    }

    /// <inheritdoc/>
    public void WriteBreakpoints(TextWriter output, IReadOnlyList<Breakpoint> breakpoints, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(breakpoints);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, breakpoints, DebugSessionJsonContext.Default.DebugEnvelopeIReadOnlyListBreakpoint);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteBreakpoint(TextWriter output, Breakpoint breakpoint, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(breakpoint);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, breakpoint, DebugSessionJsonContext.Default.DebugEnvelopeBreakpoint);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteWatchpoints(TextWriter output, IReadOnlyList<Watchpoint> watchpoints, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(watchpoints);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, watchpoints, DebugSessionJsonContext.Default.DebugEnvelopeIReadOnlyListWatchpoint);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteWatchpoint(TextWriter output, Watchpoint watchpoint, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(watchpoint);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, watchpoint, DebugSessionJsonContext.Default.DebugEnvelopeWatchpoint);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteDiskControllers(TextWriter output, IReadOnlyList<DiskControllerSnapshot> controllers, string text, bool forceJson = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(controllers);
        ArgumentNullException.ThrowIfNull(text);
        if (forceJson || this.JsonOutput)
        {
            this.WriteEnvelope(output, controllers, DebugSessionJsonContext.Default.DebugEnvelopeIReadOnlyListDiskControllerSnapshot);
            return;
        }

        output.Write(text);
    }

    /// <inheritdoc/>
    public void WriteReset(TextWriter output, ResetReceipt receipt, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, receipt, DebugSessionJsonContext.Default.DebugEnvelopeResetReceipt);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteAbort(TextWriter output, AbortReceipt receipt, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, receipt, DebugSessionJsonContext.Default.DebugEnvelopeAbortReceipt);
            return;
        }

        output.WriteLine(text);
    }

    /// <inheritdoc/>
    public void WriteDiskFileSystemInfo(TextWriter output, DiskFileSystemInfoPayload payload, string text, bool forceJson = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(text);
        if (forceJson || this.JsonOutput)
        {
            this.WriteEnvelope(output, payload, DebugSessionJsonContext.Default.DebugEnvelopeDiskFileSystemInfoPayload);
            return;
        }

        output.Write(text);
    }

    /// <inheritdoc/>
    public void WriteDiskCatalog(TextWriter output, DiskCatalogPayload payload, string text, bool forceJson = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(text);
        if (forceJson || this.JsonOutput)
        {
            this.WriteEnvelope(output, payload, DebugSessionJsonContext.Default.DebugEnvelopeDiskCatalogPayload);
            return;
        }

        output.Write(text);
    }

    /// <inheritdoc/>
    public void WriteDiskFile(TextWriter output, DiskFilePayload payload, string text, bool forceJson = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(text);
        if (forceJson || this.JsonOutput)
        {
            this.WriteEnvelope(output, payload, DebugSessionJsonContext.Default.DebugEnvelopeDiskFilePayload);
            return;
        }

        output.Write(text);
    }

    /// <inheritdoc/>
    public void WriteMapTrace(TextWriter output, MapTraceReceipt receipt, string text)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(text);
        if (this.JsonOutput)
        {
            this.WriteEnvelope(output, receipt, DebugSessionJsonContext.Default.DebugEnvelopeMapTraceReceipt);
            return;
        }

        output.WriteLine(text);
    }

    private void WriteEnvelope<T>(TextWriter output, T payload, JsonTypeInfo<DebugEnvelope<T>> typeInfo)
    {
        var envelope = new DebugEnvelope<T>(payload, this.clock(), null);
        output.WriteLine(JsonSerializer.Serialize(envelope, typeInfo));
    }
}