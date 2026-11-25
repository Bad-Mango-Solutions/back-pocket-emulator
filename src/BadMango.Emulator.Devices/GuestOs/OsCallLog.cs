// <copyright file="OsCallLog.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core;
using BadMango.Emulator.Core.Interfaces.Cpu;

using Serilog;
using Serilog.Events;

/// <summary>
/// Records ProDOS MLI and DOS 3.3 calls from the boot monitor's observe-only hooks.
/// </summary>
/// <remarks>
/// <para>
/// MLI calls arrive on <see cref="BootSequenceMonitor.SystemCallObserved"/>. DOS calls arrive on
/// <see cref="BootSequenceMonitor.SessionTrapEntered"/>. Both run before the entry instruction,
/// and this log only peeks. The ring and the pathname buffers are allocated when the log is
/// constructed. A call stores integers and copies characters into those buffers. A string is
/// built when the entry is read, when Debug logging is enabled, or when <see cref="Recorded"/>
/// has a listener.
/// </para>
/// <para>
/// <see cref="OsCallLogMode.Off"/> removes both subscriptions. The boot monitor's traps stay owned
/// by the monitor and keep <see cref="BadMango.Emulator.Bus.TrapScope.GuestOsSession"/>.
/// </para>
/// </remarks>
public sealed class OsCallLog : IOsCallLog
{
    /// <summary>The number of calls the ring keeps. Older calls are dropped.</summary>
    public const int Capacity = 256;

    private const int PathStride = ProDosMliCall.MaxPathnameLength * 2;
    private const byte KindMli = 1;
    private const byte KindFile = 2;
    private const byte KindRwts = 3;
    private const int IobSlot = 0x01;
    private const int IobDrive = 0x02;
    private const int IobTrack = 0x04;
    private const int IobSector = 0x05;
    private const int IobCommand = 0x0C;

    private readonly ILogger logger;
    private readonly CallSlot[] slots = new CallSlot[Capacity];
    private readonly char[] chars = new char[Capacity * PathStride];

    private BootSequenceMonitor? monitor;
    private IGuestMemoryReader? reader;
    private OsCallLogMode mode;
    private int head;
    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="OsCallLog"/> class.
    /// </summary>
    /// <param name="logger">Debug lines use source context GuestOs. Information and above stay quiet.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public OsCallLog(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger.ForContext("SourceContext", "GuestOs");
    }

    /// <inheritdoc />
    public event EventHandler<OsCall>? Recorded;

    /// <inheritdoc />
    public OsCallLogMode Mode => this.mode;

    /// <inheritdoc />
    public IReadOnlyList<OsCall> Entries => this.Recent(this.count);

    /// <summary>
    /// Parses <c>off</c>, <c>mli</c>, <c>dos</c>, <c>all</c>, and <c>on</c>. <c>on</c> is <see cref="OsCallLogMode.All"/>.
    /// </summary>
    /// <param name="text">The profile or command token. Null and empty are <see cref="OsCallLogMode.Off"/>.</param>
    /// <param name="parsed">The mode when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when <paramref name="text"/> is not one of those tokens.</returns>
    public static bool TryParseMode(string? text, out OsCallLogMode parsed)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            parsed = OsCallLogMode.Off;
            return true;
        }

        if (text.Equals("mli", StringComparison.OrdinalIgnoreCase))
        {
            parsed = OsCallLogMode.Mli;
            return true;
        }

        if (text.Equals("dos", StringComparison.OrdinalIgnoreCase))
        {
            parsed = OsCallLogMode.Dos;
            return true;
        }

        if (text.Equals("all", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            parsed = OsCallLogMode.All;
            return true;
        }

        parsed = OsCallLogMode.Off;
        return false;
    }

    /// <summary>
    /// Subscribes to <paramref name="machine"/>'s boot monitor for <paramref name="next"/>.
    /// </summary>
    /// <param name="machine">The machine whose CPU is peeked. The monitor is taken from the machine when <paramref name="bootMonitor"/> is omitted.</param>
    /// <param name="next">The calls to record. <see cref="OsCallLogMode.Off"/> unsubscribes.</param>
    /// <param name="bootMonitor">The monitor to subscribe to. Pass it when it is not yet a machine component.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> is <see langword="null"/>.</exception>
    public void Attach(IMachine machine, OsCallLogMode next, BootSequenceMonitor? bootMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        this.Unsubscribe();
        this.monitor = bootMonitor ?? machine.GetComponent<BootSequenceMonitor>();
        this.reader = this.monitor is null ? null : new CpuGuestMemoryReader(machine.Cpu);
        this.mode = next;
        this.Subscribe();
    }

    /// <summary>Changes the mode and the monitor subscription. Has no effect before <see cref="Attach"/>.</summary>
    /// <param name="next">The calls to record.</param>
    public void SetMode(OsCallLogMode next)
    {
        this.mode = next;
        this.Unsubscribe();
        this.Subscribe();
    }

    /// <summary>Returns the newest <paramref name="limit"/> calls, oldest first within that window.</summary>
    /// <param name="limit">How many calls to return. Zero returns an empty list.</param>
    /// <returns>The window. Shorter than <paramref name="limit"/> when fewer calls are stored.</returns>
    public IReadOnlyList<OsCall> Recent(int limit)
    {
        if (limit <= 0 || this.count == 0)
        {
            return [];
        }

        int take = limit > this.count ? this.count : limit;
        int skip = this.count - take;
        int start = this.count == Capacity ? this.head : 0;
        var list = new OsCall[take];
        for (int index = 0; index < take; index++)
        {
            int slot = (start + skip + index) % Capacity;
            list[index] = this.Materialize(slot);
        }

        return list;
    }

    private static string NameOf(CallSlot stored) => stored.Kind switch
    {
        KindMli => ProDosMliCall.FormatName(stored.Code),
        KindFile => FileManagerName(stored.Code),
        _ => RwtsName(stored.Code),
    };

    private static string FileManagerName(byte opcode) => opcode switch
    {
        1 => "OPEN",
        2 => "CLOSE",
        3 => "READ",
        4 => "WRITE",
        5 => "DELETE",
        6 => "CATALOG",
        7 => "LOCK",
        8 => "UNLOCK",
        9 => "RENAME",
        10 => "POSITION",
        11 => "INIT",
        12 => "VERIFY",
        _ => $"${opcode:X2} (unknown)",
    };

    private static string RwtsName(byte command) => command switch
    {
        0 => "SEEK",
        1 => "READ",
        2 => "WRITE",
        4 => "FORMAT",
        _ => $"${command:X2} (unknown)",
    };

    private void Subscribe()
    {
        if (this.monitor is null)
        {
            return;
        }

        if (this.mode is OsCallLogMode.Mli or OsCallLogMode.All)
        {
            this.monitor.SystemCallObserved += this.OnMli;
        }

        if (this.mode is OsCallLogMode.Dos or OsCallLogMode.All)
        {
            this.monitor.SessionTrapEntered += this.OnDos;
        }
    }

    private void Unsubscribe()
    {
        if (this.monitor is null)
        {
            return;
        }

        this.monitor.SystemCallObserved -= this.OnMli;
        this.monitor.SessionTrapEntered -= this.OnDos;
    }

    private void OnMli(object? sender, GuestOsSystemCallEventArgs args)
    {
        if (this.mode is not (OsCallLogMode.Mli or OsCallLogMode.All)
            || args.Pc != ProDosGlobalPage.MliEntry
            || this.reader is null)
        {
            return;
        }

        ushort stacked = (ushort)(args.ParameterAddress - 1);
        ProDosMliCall call = ProDosMliCall.Decode(this.reader, stacked);
        var stored = new CallSlot
        {
            Cycle = args.At.Value,
            Kind = KindMli,
            Code = call.Number,
            Caller = (ushort)(stacked - 2),
            Parameters = call.Parameters,
        };
        Span<char> primary = stackalloc char[ProDosMliCall.MaxPathnameLength];
        Span<char> renamed = stackalloc char[ProDosMliCall.MaxPathnameLength];
        int primaryCount = call.CopyPrimaryPath(this.reader, primary);
        int renamedCount = call.CopyNewPath(this.reader, renamed);
        this.Commit(stored, primary[..primaryCount], renamed[..renamedCount]);
    }

    private void OnDos(ushort address, ICpu cpu, Cycle at)
    {
        if (this.mode is not (OsCallLogMode.Dos or OsCallLogMode.All) || this.reader is null)
        {
            return;
        }

        if (address == Dos33VectorPage.FileManagerTarget)
        {
            ushort list = ProDosMliCall.ReadPointer(this.reader, Dos33VectorPage.FileManagerParameterList);
            ushort stacked = ProDosMliCall.ReadStackedReturn(cpu);
            this.Commit(
                new CallSlot
                {
                    Cycle = at.Value,
                    Kind = KindFile,
                    Code = this.reader.Read(list),
                    Caller = (ushort)(stacked - 2),
                    Parameters = list,
                },
                ReadOnlySpan<char>.Empty,
                ReadOnlySpan<char>.Empty);
            return;
        }

        if (address != Dos33VectorPage.RwtsTarget)
        {
            return;
        }

        ushort iob = (ushort)(cpu.Registers.Y.GetByte() | (cpu.Registers.A.GetByte() << 8));
        ushort stackedReturn = ProDosMliCall.ReadStackedReturn(cpu);
        this.Commit(
            new CallSlot
            {
                Cycle = at.Value,
                Kind = KindRwts,
                Code = this.reader.Read((ushort)(iob + IobCommand)),
                Caller = (ushort)(stackedReturn - 2),
                Parameters = iob,
                RwtsSlot = this.reader.Read((ushort)(iob + IobSlot)),
                RwtsDrive = this.reader.Read((ushort)(iob + IobDrive)),
                RwtsTrack = this.reader.Read((ushort)(iob + IobTrack)),
                RwtsSector = this.reader.Read((ushort)(iob + IobSector)),
            },
            ReadOnlySpan<char>.Empty,
            ReadOnlySpan<char>.Empty);
    }

    private void Commit(CallSlot stored, ReadOnlySpan<char> path, ReadOnlySpan<char> renamed)
    {
        int index = this.head;
        stored.PathLength = (byte)path.Length;
        stored.RenameLength = (byte)renamed.Length;
        this.slots[index] = stored;
        if (path.Length > 0)
        {
            path.CopyTo(this.chars.AsSpan(index * PathStride, path.Length));
        }

        if (renamed.Length > 0)
        {
            renamed.CopyTo(this.chars.AsSpan((index * PathStride) + path.Length, renamed.Length));
        }

        this.head = (index + 1) % Capacity;
        if (this.count < Capacity)
        {
            this.count++;
        }

        EventHandler<OsCall>? recorded = this.Recorded;
        bool debug = this.logger.IsEnabled(LogEventLevel.Debug);
        if (recorded is null && !debug)
        {
            return;
        }

        OsCall made = this.Materialize(index);
        if (debug)
        {
            this.logger.Debug(
                "OS call {Os} ${Code:X2} {Name} from ${Caller:X4} parms ${Parms:X4} {Summary}",
                made.Os.Value,
                made.Code,
                made.Name,
                made.Caller,
                made.ParamList,
                made.Summary ?? string.Empty);
        }

        recorded?.Invoke(this, made);
    }

    private OsCall Materialize(int index)
    {
        CallSlot stored = this.slots[index];
        string? summary = null;
        if (stored.Kind == KindMli && stored.PathLength > 0)
        {
            string path = new(this.chars, index * PathStride, stored.PathLength);
            if (stored.RenameLength > 0)
            {
                string renamed = new(this.chars, (index * PathStride) + stored.PathLength, stored.RenameLength);
                summary = path + " -> " + renamed;
            }
            else
            {
                summary = path;
            }
        }
        else if (stored.Kind == KindRwts)
        {
            summary = $"slot {stored.RwtsSlot >> 4} drive {stored.RwtsDrive} track ${stored.RwtsTrack:X2} sector ${stored.RwtsSector:X2}";
        }

        GuestOsId os = stored.Kind == KindMli ? GuestOsId.ProDos8 : GuestOsId.Dos33;
        return new OsCall(new Cycle(stored.Cycle), os, stored.Code, NameOf(stored), stored.Caller, stored.Parameters, summary);
    }

    private struct CallSlot
    {
        public ulong Cycle;
        public byte Kind;
        public byte Code;
        public ushort Caller;
        public ushort Parameters;
        public byte PathLength;
        public byte RenameLength;
        public byte RwtsSlot;
        public byte RwtsDrive;
        public byte RwtsTrack;
        public byte RwtsSector;
    }
}