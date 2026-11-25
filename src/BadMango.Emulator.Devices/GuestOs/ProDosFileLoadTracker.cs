// <copyright file="ProDosFileLoadTracker.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using BadMango.Emulator.Bus;
using BadMango.Emulator.Bus.Interfaces;
using BadMango.Emulator.Core.Interfaces.Cpu;

/// <summary>
/// Tracks ProDOS file loads from the boot monitor's <c>$BF00</c> calls.
/// </summary>
/// <remarks>
/// <para>
/// The monitor already owns the <c>$BF00</c> trap and raises
/// <see cref="BootSequenceMonitor.SystemCallObserved"/>. This tracker listens.
/// It does not register a second trap there. READ's trans_count, OPEN's ref_num,
/// and the other returned fields are read by a one-shot observe-only trap on the
/// instruction after the call. That trap returns <see cref="TrapResult.NotHandled"/>
/// and is registered with <see cref="TrapScope.GuestOsSession"/>, so a session end
/// drops it with the rest of the session.
/// </para>
/// <para>
/// Decoding stays in <see cref="ProDosMliFileCall"/> so it can move to the OS call
/// log's decoder (#686). When the boot log is off, <see cref="Install"/> is not called.
/// </para>
/// </remarks>
public sealed class ProDosFileLoadTracker : IFileLoadTracker
{
    private const int RowLimit = 256;
    private const string ReturnTrapName = "MLI file return";
    private const string ReturnTrapDescription = "Observe-only return from a ProDOS MLI file call.";

    private readonly ITrapRegistry registry;
    private readonly CpuGuestMemoryReader memory;
    private readonly TrapHandler returnHandler;
    private readonly List<Row> rows = [];

    private FileLoadProgress[] published = [];
    private FileLoadProgress? current;
    private PendingCall? pending;
    private string prefix = string.Empty;
    private string? announcedPath;
    private uint? announcedSize;
    private bool announcedExact;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProDosFileLoadTracker"/> class.
    /// </summary>
    /// <param name="registry">The registry that receives the one-shot return trap.</param>
    /// <param name="cpu">The CPU peeked while a call is decoded. The peek does not run the guest instruction.</param>
    /// <param name="monitor">The boot monitor whose MLI calls and session end drive the timeline.</param>
    /// <exception cref="ArgumentNullException">If any argument is <see langword="null"/>.</exception>
    public ProDosFileLoadTracker(ITrapRegistry registry, ICpu cpu, BootSequenceMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(monitor);
        this.registry = registry;
        this.memory = new CpuGuestMemoryReader(cpu);
        this.returnHandler = this.OnReturn;
        monitor.SystemCallObserved += this.OnSystemCall;
        monitor.GuestOsSessionEnded += this.OnSessionEnded;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<FileLoadProgress> Files => this.published;

    /// <inheritdoc />
    public FileLoadProgress? Current => this.current;

    /// <summary>
    /// Attaches a tracker when the machine does not already have one.
    /// </summary>
    /// <param name="machine">The machine that stores the tracker and the trap registry.</param>
    /// <param name="monitor">The monitor the tracker subscribes to.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="machine"/> or <paramref name="monitor"/> is <see langword="null"/>.</exception>
    /// <remarks>A missing trap registry leaves the machine without a tracker. No trap is registered here.</remarks>
    public static void Install(IMachine machine, BootSequenceMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(monitor);
        if (machine.GetComponent<ProDosFileLoadTracker>() is not null)
        {
            return;
        }

        if (machine.GetComponent<ITrapRegistry>() is not ITrapRegistry registry)
        {
            return;
        }

        machine.AddComponent(new ProDosFileLoadTracker(registry, machine.Cpu, monitor));
    }

    /// <inheritdoc />
    public void Report(FileLoadProgress progress)
    {
        if (progress.Path is null)
        {
            throw new ArgumentException("A file load progress row needs a path.", nameof(progress));
        }

        int index = this.FindReportTarget(progress);
        var row = new Row(progress, progress.Size is not null);
        if (index >= 0)
        {
            this.rows[index] = row;
        }
        else
        {
            this.AddRow(row);
        }

        if (progress.Active)
        {
            this.current = progress;
        }
        else if (this.IsCurrent(progress))
        {
            this.RefreshCurrent();
        }

        this.Publish();
    }

    /// <inheritdoc />
    public void Clear()
    {
        this.DropPending();
        this.rows.Clear();
        this.published = [];
        this.current = null;
        this.prefix = string.Empty;
        this.announcedPath = null;
        this.announcedSize = null;
        this.announcedExact = false;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static FileLoadProgress Copy(
        FileLoadProgress progress,
        uint? size,
        uint bytesRead,
        uint position,
        bool active) =>
        new(progress.Path, size, bytesRead, position, progress.OpenedAtCycle, progress.RefNum, active);

    private void OnSessionEnded(object? sender, GuestOsSessionEventArgs args)
    {
        _ = sender;
        _ = args;
        this.Clear();
    }

    private void OnSystemCall(object? sender, GuestOsSystemCallEventArgs args)
    {
        _ = sender;
        if (args.Os != GuestOsId.ProDos8 || args.Pc != ProDosGlobalPage.MliEntry)
        {
            return;
        }

        if (!ProDosMliFileCall.TryDecode(this.memory, args.ParameterAddress, out var call))
        {
            return;
        }

        if (!call.CompletesOnReturn)
        {
            this.ApplyInput(call);
            return;
        }

        this.ArmReturn(new PendingCall(call.ReturnAddress, call.Command, call.ParameterList, args.At.Value));
    }

    private TrapResult OnReturn(ICpu cpu, IMemoryBus bus, IEventContext context)
    {
        _ = bus;
        _ = context;
        if (this.pending is not PendingCall waiting || cpu.GetPC() != waiting.ReturnAddress)
        {
            return TrapResult.NotHandled;
        }

        this.ApplyReturn(waiting);
        this.DropPending();
        return TrapResult.NotHandled;
    }

    private void ApplyInput(ProDosMliFileCall.Decoded call)
    {
        switch (call.Command)
        {
            case ProDosMliFileCall.SetPrefix:
                this.prefix = this.Resolve(ProDosMliFileCall.ReadReferencedPath(this.memory, call.ParameterList));
                break;
            case ProDosMliFileCall.SetMark:
                this.NotePosition(
                    ProDosMliFileCall.ReadRef(this.memory, call.ParameterList),
                    ProDosMliFileCall.Read24(this.memory, call.ParameterList));
                break;
            case ProDosMliFileCall.Close:
            case ProDosMliFileCall.Flush:
                this.CloseRef(ProDosMliFileCall.ReadRef(this.memory, call.ParameterList));
                break;
        }
    }

    private void ApplyReturn(PendingCall waiting)
    {
        switch (waiting.Command)
        {
            case ProDosMliFileCall.Open:
                this.Open(
                    this.Resolve(ProDosMliFileCall.ReadReferencedPath(this.memory, waiting.ParameterList)),
                    ProDosMliFileCall.ReadOpenRef(this.memory, waiting.ParameterList),
                    waiting.Cycle);
                break;
            case ProDosMliFileCall.Read:
                this.NoteRead(
                    ProDosMliFileCall.ReadRef(this.memory, waiting.ParameterList),
                    ProDosMliFileCall.ReadTransCount(this.memory, waiting.ParameterList));
                break;
            case ProDosMliFileCall.GetEof:
                this.NoteSize(
                    ProDosMliFileCall.ReadRef(this.memory, waiting.ParameterList),
                    ProDosMliFileCall.Read24(this.memory, waiting.ParameterList),
                    exact: true);
                break;
            case ProDosMliFileCall.GetMark:
                this.NotePosition(
                    ProDosMliFileCall.ReadRef(this.memory, waiting.ParameterList),
                    ProDosMliFileCall.Read24(this.memory, waiting.ParameterList));
                break;
            case ProDosMliFileCall.GetFileInfo:
                this.NoteFileInfo(
                    this.Resolve(ProDosMliFileCall.ReadReferencedPath(this.memory, waiting.ParameterList)),
                    ProDosMliFileCall.ReadBlocks(this.memory, waiting.ParameterList));
                break;
            case ProDosMliFileCall.GetPrefix:
                this.prefix = ProDosMliFileCall.ReadReferencedPath(this.memory, waiting.ParameterList);
                break;
        }
    }

    private void Open(string path, byte refNum, ulong cycle)
    {
        if (refNum == 0)
        {
            return;
        }

        uint? size = null;
        var exact = false;
        if (this.announcedSize is uint announced
            && this.announcedPath is not null
            && PathsEqual(this.announcedPath, path))
        {
            size = announced;
            exact = this.announcedExact;
        }

        var progress = new FileLoadProgress(path, size, 0, 0, cycle, refNum, true);
        this.AddRow(new Row(progress, exact));
        this.current = progress;
        this.Publish();
    }

    private void NoteRead(byte refNum, ushort transferred)
    {
        if (!this.TryGetActiveRef(refNum, out int index))
        {
            return;
        }

        var row = this.rows[index];
        var progress = row.Progress;
        var updated = Copy(
            progress,
            progress.Size,
            progress.BytesRead + transferred,
            progress.Position + transferred,
            progress.Active);
        this.rows[index] = new Row(updated, row.ExactSize);
        this.current = updated;
        this.Publish();
    }

    private void NoteSize(byte refNum, uint size, bool exact)
    {
        if (!this.TryGetActiveRef(refNum, out int index))
        {
            return;
        }

        var row = this.rows[index];
        if (!exact && row.ExactSize)
        {
            return;
        }

        var progress = row.Progress;
        var updated = Copy(progress, size, progress.BytesRead, progress.Position, progress.Active);
        this.rows[index] = new Row(updated, exact || row.ExactSize);
        if (this.IsCurrent(updated))
        {
            this.current = updated;
        }

        this.Publish();
    }

    private void NotePosition(byte refNum, uint position)
    {
        if (!this.TryGetActiveRef(refNum, out int index))
        {
            return;
        }

        var row = this.rows[index];
        var progress = row.Progress;
        var updated = Copy(progress, progress.Size, progress.BytesRead, position, progress.Active);
        this.rows[index] = new Row(updated, row.ExactSize);
        this.current = updated;
        this.Publish();
    }

    private void NoteFileInfo(string path, ushort blocks)
    {
        uint size = (uint)blocks * 512u;
        this.announcedPath = path;
        this.announcedSize = size;
        this.announcedExact = false;
        int index = this.FindActivePath(path);
        if (index < 0)
        {
            return;
        }

        var row = this.rows[index];
        if (row.ExactSize)
        {
            return;
        }

        var progress = row.Progress;
        var updated = Copy(progress, size, progress.BytesRead, progress.Position, progress.Active);
        this.rows[index] = new Row(updated, false);
        if (this.IsCurrent(updated))
        {
            this.current = updated;
        }

        this.Publish();
    }

    private void CloseRef(byte refNum)
    {
        if (refNum == 0)
        {
            var any = false;
            for (var i = 0; i < this.rows.Count; i++)
            {
                var row = this.rows[i];
                if (row.Progress.RefNum is null || !row.Progress.Active)
                {
                    continue;
                }

                var progress = row.Progress;
                this.rows[i] = new Row(
                    Copy(progress, progress.Size, progress.BytesRead, progress.Position, false),
                    row.ExactSize);
                any = true;
            }

            if (!any)
            {
                return;
            }

            this.RefreshCurrent();
            this.Publish();
            return;
        }

        if (!this.TryGetActiveRef(refNum, out int index))
        {
            return;
        }

        var selected = this.rows[index];
        var selectedProgress = selected.Progress;
        bool wasCurrent = this.IsCurrent(selectedProgress);
        var closed = Copy(
            selectedProgress,
            selectedProgress.Size,
            selectedProgress.BytesRead,
            selectedProgress.Position,
            false);
        this.rows[index] = new Row(closed, selected.ExactSize);
        if (wasCurrent)
        {
            this.RefreshCurrent();
        }

        this.Publish();
    }

    private void ArmReturn(PendingCall next)
    {
        if (this.pending is PendingCall previous && previous.ReturnAddress != next.ReturnAddress)
        {
            this.UnregisterReturn(previous.ReturnAddress);
        }

        this.pending = next;
        if (this.IsOurReturn(next.ReturnAddress))
        {
            return;
        }

        if (this.registry.HasTrap(next.ReturnAddress))
        {
            this.pending = null;
            return;
        }

        this.registry.Register(
            next.ReturnAddress,
            ReturnTrapName,
            TrapCategory.OperatingSystem,
            this.returnHandler,
            ReturnTrapDescription,
            TrapScope.GuestOsSession);
    }

    private void DropPending()
    {
        if (this.pending is not PendingCall waiting)
        {
            return;
        }

        this.pending = null;
        this.UnregisterReturn(waiting.ReturnAddress);
    }

    private void UnregisterReturn(ushort address)
    {
        if (this.IsOurReturn(address))
        {
            this.registry.Unregister(address);
        }
    }

    private bool IsOurReturn(ushort address)
    {
        var info = this.registry.GetTrapInfo(address);
        return info is not null && info.Value.Name == ReturnTrapName;
    }

    private void AddRow(Row row)
    {
        if (this.rows.Count == RowLimit)
        {
            this.rows.RemoveAt(0);
        }

        this.rows.Add(row);
    }

    private bool TryGetActiveRef(byte refNum, out int index)
    {
        index = this.FindActiveRef(refNum);
        return index >= 0;
    }

    private int FindActiveRef(byte refNum)
    {
        for (var i = this.rows.Count - 1; i >= 0; i--)
        {
            var progress = this.rows[i].Progress;
            if (progress.Active && progress.RefNum == refNum)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindActivePath(string path)
    {
        for (var i = this.rows.Count - 1; i >= 0; i--)
        {
            var progress = this.rows[i].Progress;
            if (progress.Active && progress.RefNum is not null && PathsEqual(progress.Path, path))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindReportTarget(FileLoadProgress progress)
    {
        if (progress.RefNum is byte refNum)
        {
            int active = this.FindActiveRef(refNum);
            if (active >= 0)
            {
                return active;
            }

            for (var i = this.rows.Count - 1; i >= 0; i--)
            {
                if (this.rows[i].Progress.RefNum == refNum)
                {
                    return i;
                }
            }

            return -1;
        }

        for (var i = this.rows.Count - 1; i >= 0; i--)
        {
            var existing = this.rows[i].Progress;
            if (existing.RefNum is null && PathsEqual(existing.Path, progress.Path))
            {
                return i;
            }
        }

        return -1;
    }

    private void RefreshCurrent()
    {
        FileLoadProgress? latest = null;
        for (var i = this.rows.Count - 1; i >= 0; i--)
        {
            if (this.rows[i].Progress.Active)
            {
                latest = this.rows[i].Progress;
                break;
            }
        }

        this.current = latest;
    }

    private bool IsCurrent(FileLoadProgress progress)
    {
        if (this.current is not FileLoadProgress shown)
        {
            return false;
        }

        if (progress.RefNum is byte refNum)
        {
            return shown.RefNum == refNum;
        }

        return shown.RefNum is null && PathsEqual(shown.Path, progress.Path);
    }

    private void Publish()
    {
        var copy = new FileLoadProgress[this.rows.Count];
        for (var i = 0; i < this.rows.Count; i++)
        {
            copy[i] = this.rows[i].Progress;
        }

        this.published = copy;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private string Resolve(string path)
    {
        if (path.Length == 0 || path[0] == '/' || this.prefix.Length == 0)
        {
            return path;
        }

        if (this.prefix[^1] == '/')
        {
            return this.prefix + path;
        }

        return this.prefix + "/" + path;
    }

    private readonly record struct PendingCall(ushort ReturnAddress, byte Command, ushort ParameterList, ulong Cycle);

    private readonly record struct Row(FileLoadProgress Progress, bool ExactSize);
}