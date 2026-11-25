// <copyright file="InspectionTools.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using System.ComponentModel;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Serilog;

/// <summary>
/// MCP tools for registers, memory, and disassembly.
/// </summary>
[McpServerToolType]
public sealed class InspectionTools
{
    private readonly IDebugSession session;
    private readonly IDebugContext context;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectionTools"/> class.
    /// </summary>
    /// <param name="session">The shared debug session.</param>
    /// <param name="context">The debug context used to resolve addresses.</param>
    /// <param name="logger">The host logger.</param>
    public InspectionTools(IDebugSession session, IDebugContext context, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);
        this.session = session;
        this.context = context;
        this.logger = logger.ForContext<InspectionTools>();
    }

    /// <summary>
    /// Reads the CPU registers.
    /// </summary>
    /// <returns>An emudbg envelope whose payload is the register snapshot, or an error envelope.</returns>
    [McpServerTool(Name = "get_registers", UseStructuredContent = true)]
    [Description("Get the CPU registers.")]
    public object GetRegisters()
    {
        try
        {
            RegisterSnapshot registers = this.session.GetRegisters();
            return DebugEnvelope<RegisterSnapshot>.Create(registers);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("get_registers", ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads memory without side effects.
    /// </summary>
    /// <param name="address">The starting address. Accepts $hex, 0xhex, decimal, or a soft-switch name.</param>
    /// <param name="length">The number of bytes. Defaults to 16. The maximum is 4096.</param>
    /// <returns>
    /// An emudbg envelope whose payload is the memory block and any bus faults formatted with the shared
    /// fault-kind formatter. Invalid arguments return an error envelope.
    /// </returns>
    [McpServerTool(Name = "read_memory", UseStructuredContent = true)]
    [Description("Read memory without side effects. Address accepts $hex, 0xhex, decimal, or a soft-switch name. Length defaults to 16 and cannot exceed 4096.")]
    public object ReadMemory(
        [Description("Start address. Accepts $hex, 0xhex, decimal, or a soft-switch name.")] string address,
        [Description("Number of bytes. Defaults to 16. Maximum is 4096.")] int length = DebugMcpLimits.DefaultReadLength)
    {
        if (!DebugMcpArguments.TryParseAddress(address, this.context.Machine, out uint start, out string? addressError))
        {
            return this.Fail("read_memory", addressError!);
        }

        if (!DebugMcpArguments.TryValidateRange(start, length, DebugMcpLimits.MaxReadLength, out string? rangeError))
        {
            return this.Fail("read_memory", rangeError!);
        }

        try
        {
            MemoryBlock block = this.session.ReadMemory(start, length);
            return DebugEnvelope<MemoryReadReceipt>.Create(ToReceipt(block));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            return this.Fail("read_memory", ex.Message, ex);
        }
    }

    /// <summary>
    /// Writes bytes to memory.
    /// </summary>
    /// <param name="address">The starting address. Accepts $hex, 0xhex, decimal, or a soft-switch name.</param>
    /// <param name="data">Packed hex or space-separated bytes. At most 256 bytes.</param>
    /// <returns>
    /// An emudbg envelope whose payload is the write receipt. A bus fault returns an error envelope whose
    /// message and fault list use the shared fault-kind formatter.
    /// </returns>
    [McpServerTool(Name = "write_memory", UseStructuredContent = true)]
    [Description("Write bytes to memory. Data is packed hex (A900) or space-separated bytes ($A9 00). Length cannot exceed 256 bytes.")]
    public object WriteMemory(
        [Description("Start address. Accepts $hex, 0xhex, decimal, or a soft-switch name.")] string address,
        [Description("Bytes as packed hex or space-separated tokens ($AB, 0xAB, or AB). Maximum is 256 bytes.")] string data)
    {
        if (!DebugMcpArguments.TryParseAddress(address, this.context.Machine, out uint start, out string? addressError))
        {
            return this.Fail("write_memory", addressError!);
        }

        if (!DebugMcpArguments.TryParseBytes(data, out byte[] bytes, out string? dataError))
        {
            return this.Fail("write_memory", dataError!);
        }

        if (!DebugMcpArguments.TryValidateRange(start, bytes.Length, DebugMcpLimits.MaxWriteLength, out string? rangeError))
        {
            return this.Fail("write_memory", rangeError!);
        }

        try
        {
            this.session.WriteMemory(start, bytes);
            return DebugEnvelope<MemoryWriteReceipt>.Create(new MemoryWriteReceipt(start, bytes.Length));
        }
        catch (MemoryWriteException ex)
        {
            this.logger.Warning(ex, "write_memory faulted at {Address}.", ex.Address);
            return DebugMcpResults.Error(ex.Message, [new MemoryFaultLine(ex.Address, ex.Kind)]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException)
        {
            return this.Fail("write_memory", ex.Message, ex);
        }
    }

    /// <summary>
    /// Disassembles instructions.
    /// </summary>
    /// <param name="address">The starting address. Omit it to start at the program counter.</param>
    /// <param name="count">The number of instructions. Defaults to 16. The maximum is 256.</param>
    /// <returns>An emudbg envelope whose payload is the disassembly lines, or an error envelope.</returns>
    [McpServerTool(Name = "disassemble", UseStructuredContent = true)]
    [Description("Disassemble instructions. Address defaults to the program counter and accepts $hex, 0xhex, decimal, or a soft-switch name. Count defaults to 16 and cannot exceed 256.")]
    public object Disassemble(
        [Description("Start address. Omit to use the program counter. Accepts $hex, 0xhex, decimal, or a soft-switch name.")] string? address = null,
        [Description("Number of instructions. Defaults to 16. Maximum is 256.")] int count = DebugMcpLimits.DefaultDisassemblyCount)
    {
        if (count < 1 || count > DebugMcpLimits.MaxDisassemblyCount)
        {
            return this.Fail("disassemble", $"Disassembly count must be from 1 through {DebugMcpLimits.MaxDisassemblyCount}.");
        }

        try
        {
            uint start;
            if (string.IsNullOrWhiteSpace(address))
            {
                start = checked((uint)this.session.GetRegisters().ProgramCounter);
            }
            else if (!DebugMcpArguments.TryParseAddress(address, this.context.Machine, out start, out string? addressError))
            {
                return this.Fail("disassemble", addressError!);
            }

            IReadOnlyList<DisassemblyLine> lines = this.session.Disassemble(start, count);
            return DebugEnvelope<IReadOnlyList<DisassemblyLine>>.Create(lines);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            return this.Fail("disassemble", ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the detected guest operating system and the boot stage.
    /// </summary>
    /// <returns>
    /// An emudbg envelope whose payload is the guest OS snapshot. When the boot monitor is on and no
    /// milestone was seen, the monitor asks the detector and may set the stage to Ready.
    /// </returns>
    [McpServerTool(Name = "get_guest_os", UseStructuredContent = true)]
    [Description("Get the detected guest operating system and boot stage. Stage names include None, Init, BootDeviceSelected, BootRom, Boot1, Loader, Ready, Unknown, CustomBoot, and NoBootDevice. When the boot monitor is on and no milestone was seen, detection can set the stage to Ready. Init is left as Init.")]
    public object GetGuestOs()
    {
        try
        {
            GuestOsSnapshot snapshot = GuestOsInspection.ReadGuestOs(this.context.Machine);
            return DebugEnvelope<GuestOsSnapshot>.Create(snapshot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("get_guest_os", ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the boot milestone log.
    /// </summary>
    /// <returns>An emudbg envelope whose payload is the stage and the milestones, oldest first.</returns>
    [McpServerTool(Name = "get_boot_log", UseStructuredContent = true)]
    [Description("Get the guest boot milestone log: stage, boot-sector re-entries, and each milestone's cycle, PC, kind, OS, and detail. RomInitEntered detail includes the reset vector and cold or warm (open-apple when that key made it cold). BootDeviceSelected detail includes method autostart, method user, or method open-apple. A cold boot --log or reset --log starts with RomInitEntered.")]
    public object GetBootLog()
    {
        try
        {
            BootLogSnapshot log = GuestOsInspection.ReadBootLog(this.context.Machine);
            return DebugEnvelope<BootLogSnapshot>.Create(log);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("get_boot_log", ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the file-load timeline.
    /// </summary>
    /// <returns>
    /// An emudbg envelope whose payload is the current load and every row, oldest first.
    /// Each row's <c>line</c> is <c>BASIC.SYSTEM 6656/10240 (65%)</c>, or <c>BASIC.SYSTEM 6656</c> when the size is unknown.
    /// </returns>
    [McpServerTool(Name = "get_file_activity", UseStructuredContent = true)]
    [Description("Get the guest file-load timeline. Each line is BASIC.SYSTEM 6656/10240 (65%) when the size is known, or BASIC.SYSTEM 6656 when it is not. RefNum is the ProDOS ref; a pre-MLI row leaves it null and matches on path. Enabled is false when the boot log is off. Current is the file being loaded.")]
    public object GetFileActivity()
    {
        try
        {
            FileActivitySnapshot activity = GuestOsInspection.ReadFileActivity(this.context.Machine);
            return DebugEnvelope<FileActivitySnapshot>.Create(activity);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("get_file_activity", ex.Message, ex);
        }
    }

    /// <summary>
    /// Reads the recorded ProDOS MLI and DOS 3.3 calls.
    /// </summary>
    /// <param name="limit">How many of the newest calls to return. Defaults to the ring size. Maximum is 256.</param>
    /// <returns>An emudbg envelope whose payload is the call-log mode and the calls, oldest first.</returns>
    [McpServerTool(Name = "get_os_calls", UseStructuredContent = true)]
    [Description("Get the recorded guest OS calls, oldest first. limit is how many of the newest calls to return, from 1 through 256, and defaults to 256. The mode is off, mli, dos, or all. An unknown MLI command is named $xx (unknown). The log is off until the profile or os calls turns it on.")]
    public object GetOsCalls(
        [Description("How many of the newest calls to return. Defaults to 256. Maximum is 256.")] int limit = GuestOsInspection.MaxOsCalls)
    {
        if (limit < 1 || limit > GuestOsInspection.MaxOsCalls)
        {
            return this.Fail("get_os_calls", $"OS call limit must be from 1 through {GuestOsInspection.MaxOsCalls}.");
        }

        try
        {
            OsCallLogSnapshot log = GuestOsInspection.ReadOsCalls(this.context.Machine, limit);
            return DebugEnvelope<OsCallLogSnapshot>.Create(log);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return this.Fail("get_os_calls", ex.Message, ex);
        }
    }

    private static MemoryReadReceipt ToReceipt(MemoryBlock block)
    {
        var reports = new List<MemoryFaultLine>();
        if (block.Faults is not null)
        {
            for (int i = 0; i < block.Faults.Count; i++)
            {
                byte mask = block.Faults[i];
                if (mask == 0)
                {
                    continue;
                }

                uint faultAddress = block.Address + (uint)i;
                reports.Add(new MemoryFaultLine(faultAddress, DebugOutputRenderer.FormatFaultBits(mask)));
            }
        }

        return new MemoryReadReceipt(block.Address, block.Length, block.Data, block.Faults, reports);
    }

    private CallToolResult Fail(string tool, string message, Exception? exception = null)
    {
        if (exception is null)
        {
            this.logger.Warning("{Tool} failed: {Message}", tool, message);
        }
        else
        {
            this.logger.Warning(exception, "{Tool} failed: {Message}", tool, message);
        }

        return DebugMcpResults.Error(message);
    }
}