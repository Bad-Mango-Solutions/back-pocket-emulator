// <copyright file="MainBus.MappingTrace.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Runtime.CompilerServices;

using Interfaces;

/// <content>
/// Mapping trace: the fixed ring, the optional slow-path range, and remap snapshots.
/// </content>
public sealed partial class MainBus
{
    /// <summary>
    /// The pre-sized ring. Allocated with the bus, never on an access.
    /// </summary>
    private readonly MappingTraceRing mappingTrace = new();

    /// <summary>
    /// Providers the machine already has. Read only while trace is on, and only from a remap.
    /// </summary>
    private Func<IEnumerable<ISoftSwitchProvider>>? softSwitchSource;

    /// <summary>
    /// Backing field for <see cref="MappingTraceEnabled"/>. The slow path loads it once per byte.
    /// The CPU fast path does not.
    /// </summary>
    private bool mappingTraceEnabled;

    /// <summary>
    /// <see langword="true"/> when a trace range is forcing covered pages to <see cref="FastPathOps.None"/>.
    /// Read from page-table stores, not from the CPU fast path.
    /// </summary>
    private bool traceRangeActive;

    /// <summary>
    /// First covered page, inclusive, when <see cref="traceRangeActive"/> is set.
    /// </summary>
    private int traceRangeStartPage;

    /// <summary>
    /// Last covered page, inclusive, when <see cref="traceRangeActive"/> is set.
    /// </summary>
    private int traceRangeEndPage;

    /// <summary>
    /// The start address passed to <see cref="SetMappingTraceRange"/>, for the debugger.
    /// </summary>
    private uint traceRangeStartAddress;

    /// <summary>
    /// The inclusive end address passed to <see cref="SetMappingTraceRange"/>, for the debugger.
    /// </summary>
    private uint traceRangeEndAddress;

    /// <summary>
    /// Nesting depth of rebuilds that share one soft-switch snapshot.
    /// </summary>
    private int traceBatchDepth;

    /// <summary>
    /// The snapshot for the current rebuild, or <see langword="null"/> outside one.
    /// </summary>
    private IReadOnlyList<SoftSwitchState>? remapSnapshot;

    /// <summary>
    /// Guards <see cref="CaptureSoftSwitches"/> against a provider that touches the bus.
    /// </summary>
    private int captureDepth;

    /// <inheritdoc />
    public IMappingTraceRing MappingTrace => mappingTrace;

    /// <inheritdoc />
    public bool MappingTraceEnabled
    {
        get => mappingTraceEnabled;
        set => mappingTraceEnabled = value;
    }

    /// <inheritdoc />
    public bool HasMappingTraceRange => traceRangeActive;

    /// <inheritdoc />
    public uint MappingTraceRangeStart => traceRangeActive ? traceRangeStartAddress : 0;

    /// <inheritdoc />
    public uint MappingTraceRangeEnd => traceRangeActive ? traceRangeEndAddress : 0;

    /// <summary>
    /// Points remap snapshots at the soft-switch providers the machine already has.
    /// </summary>
    /// <param name="source">
    /// The providers to ask, or <see langword="null"/> to record remaps with an empty snapshot.
    /// <see cref="MachineBuilder"/> passes the machine's <see cref="ISoftSwitchProvider"/> components.
    /// </param>
    /// <remarks>
    /// The delegate is invoked only when trace is on and a page entry is stored. The CPU fast path
    /// does not call it. <see cref="ISoftSwitchProvider.GetSoftSwitchStates"/> may allocate; that
    /// allocation stays on the remap.
    /// </remarks>
    public void SetSoftSwitchSource(Func<IEnumerable<ISoftSwitchProvider>>? source)
    {
        softSwitchSource = source;
    }

    /// <inheritdoc />
    public void SetMappingTraceRange(uint start, uint endInclusive)
    {
        if (endInclusive < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endInclusive),
                endInclusive,
                "Mapping trace range end is before the start.");
        }

        int lastPage = pageTable.Length - 1;
        int newStart = (int)(start >> PageShift);
        if (newStart > lastPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "Mapping trace range starts outside the address space.");
        }

        uint lastAddress = ((uint)pageTable.Length << PageShift) - 1;
        if (endInclusive > lastAddress)
        {
            endInclusive = lastAddress;
        }

        int newEnd = (int)(endInclusive >> PageShift);

        int oldStart = traceRangeStartPage;
        int oldEnd = traceRangeEndPage;
        bool hadRange = traceRangeActive;

        mappingTraceEnabled = true;
        traceRangeActive = true;
        traceRangeStartPage = newStart;
        traceRangeEndPage = newEnd;
        traceRangeStartAddress = start;
        traceRangeEndAddress = endInclusive;

        int from = hadRange ? Math.Min(oldStart, newStart) : newStart;
        int to = hadRange ? Math.Max(oldEnd, newEnd) : newEnd;
        RepublishPages(from, to);
    }

    /// <inheritdoc />
    public void ClearMappingTraceRange()
    {
        if (!traceRangeActive)
        {
            return;
        }

        int from = traceRangeStartPage;
        int to = traceRangeEndPage;
        traceRangeActive = false;
        traceRangeStartPage = 0;
        traceRangeEndPage = 0;
        traceRangeStartAddress = 0;
        traceRangeEndAddress = 0;
        RepublishPages(from, to);
    }

    /// <summary>
    /// Opens a rebuild that shares one soft-switch snapshot across every page it stores.
    /// </summary>
    /// <returns>A token for <see cref="EndTraceBatch"/>. Zero when trace is off, so the caller allocates nothing.</returns>
    private int BeginTraceBatch()
    {
        if (!mappingTraceEnabled)
        {
            return 0;
        }

        if (traceBatchDepth == 0)
        {
            remapSnapshot = CaptureSoftSwitches();
        }

        traceBatchDepth++;
        return traceBatchDepth;
    }

    /// <summary>
    /// Closes a batch opened by <see cref="BeginTraceBatch"/>.
    /// </summary>
    /// <param name="token">The token from <see cref="BeginTraceBatch"/>. Zero is a no-op.</param>
    private void EndTraceBatch(int token)
    {
        if (token == 0 || traceBatchDepth == 0)
        {
            return;
        }

        traceBatchDepth--;
        if (traceBatchDepth == 0)
        {
            remapSnapshot = null;
        }
    }

    /// <summary>
    /// Recomputes fast-path fields for <paramref name="startPage"/> through <paramref name="endPage"/> inclusive.
    /// </summary>
    /// <param name="startPage">The first page index.</param>
    /// <param name="endPage">The last page index.</param>
    private void RepublishPages(int startPage, int endPage)
    {
        int batch = BeginTraceBatch();
        try
        {
            for (int pageIndex = startPage; pageIndex <= endPage; pageIndex++)
            {
                pageTable[pageIndex] = WithFastPath(pageTable[pageIndex], pageIndex);
            }
        }
        finally
        {
            EndTraceBatch(batch);
        }
    }

    /// <summary>
    /// Appends a remap record when trace is on. The name is the target's existing reference.
    /// </summary>
    /// <param name="pageIndex">The page that was stored.</param>
    /// <param name="published">The entry just published, including a range-forced <see cref="PageEntry.FastOps"/>.</param>
    /// <param name="natural">The section 7.2 operations before the range was applied.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RecordMappingChange(int pageIndex, in PageEntry published, FastPathOps natural)
    {
        IReadOnlyList<SoftSwitchState> snapshot = remapSnapshot ?? CaptureSoftSwitches();
        string? name = published.Target is null ? null : published.Target.Name;
        mappingTrace.Record(MappingTraceRecord.Remap(
            pageIndex,
            name,
            published.RegionTag,
            published.PhysicalBase,
            published.DeviceId,
            published.Perms,
            natural,
            snapshot));
    }

    /// <summary>
    /// Appends one slow-path access record. Called only after the caller has loaded
    /// <see cref="mappingTraceEnabled"/> and found it set.
    /// </summary>
    /// <param name="access">The byte access that just completed.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TraceSlowAccess(in BusAccess access)
    {
        int pageIndex = (int)(access.Address >> PageShift);
        if ((uint)pageIndex >= (uint)pageTable.Length)
        {
            mappingTrace.Record(MappingTraceRecord.Access(
                access.Cycle,
                access.Address,
                access.Intent,
                null,
                default,
                0,
                0,
                pageIndex));
            return;
        }

        ref readonly PageEntry page = ref pageTable[pageIndex];
        string? name = page.Target is null ? null : page.Target.Name;
        mappingTrace.Record(MappingTraceRecord.Access(
            access.Cycle,
            access.Address,
            access.Intent,
            name,
            page.RegionTag,
            page.PhysicalBase,
            page.DeviceId,
            pageIndex));
    }

    /// <summary>
    /// Takes one soft-switch snapshot from the providers the machine supplied.
    /// </summary>
    /// <returns>The provider lists, or an empty array when none are registered. A re-entrant call returns empty.</returns>
    private IReadOnlyList<SoftSwitchState> CaptureSoftSwitches()
    {
        if (captureDepth != 0 || softSwitchSource is null)
        {
            return Array.Empty<SoftSwitchState>();
        }

        captureDepth++;
        try
        {
            IEnumerable<ISoftSwitchProvider> providers = softSwitchSource();
            if (providers is IReadOnlyCollection<ISoftSwitchProvider> collection)
            {
                if (collection.Count == 0)
                {
                    return Array.Empty<SoftSwitchState>();
                }

                if (collection.Count == 1)
                {
                    foreach (ISoftSwitchProvider only in collection)
                    {
                        return only.GetSoftSwitchStates();
                    }
                }
            }

            var combined = new List<SoftSwitchState>();
            foreach (ISoftSwitchProvider provider in providers)
            {
                combined.AddRange(provider.GetSoftSwitchStates());
            }

            return combined;
        }
        finally
        {
            captureDepth--;
        }
    }

    /// <summary>
    /// Reports whether <paramref name="pageIndex"/> is covered by the active trace range.
    /// </summary>
    /// <param name="pageIndex">The page being published.</param>
    /// <returns><see langword="true"/> when the page must publish <see cref="FastPathOps.None"/>.</returns>
    private bool PageInTraceRange(int pageIndex)
    {
        return traceRangeActive
            && pageIndex >= traceRangeStartPage
            && pageIndex <= traceRangeEndPage;
    }
}