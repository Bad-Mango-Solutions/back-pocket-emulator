// <copyright file="MainBus.FastPath.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Runtime.InteropServices;

using Interfaces;

/// <content>
/// Fast-path field publication for page-table stores.
/// </content>
public sealed partial class MainBus
{
    /// <summary>
    /// Entries into <see cref="TryRead8"/>. The CPU fast path does not increment this.
    /// </summary>
    private int tryRead8CallCount;

    /// <summary>
    /// Entries into <see cref="TryWrite8"/>. The CPU fast path does not increment this.
    /// </summary>
    private int tryWrite8CallCount;

    /// <summary>
    /// Gets how many times <see cref="TryRead8"/> has been entered on this bus.
    /// </summary>
    /// <remarks>
    /// Tests use the count to prove a RAM byte access indexed <see cref="PageEntry.FastBacking"/>
    /// instead of calling the checked path. The count is not architectural bus state.
    /// </remarks>
    internal int TryRead8CallCount => tryRead8CallCount;

    /// <summary>
    /// Gets how many times <see cref="TryWrite8"/> has been entered on this bus.
    /// </summary>
    /// <remarks>
    /// Tests use the count to prove a RAM byte store indexed <see cref="PageEntry.FastBacking"/>
    /// instead of calling the checked path. The count is not architectural bus state.
    /// </remarks>
    internal int TryWrite8CallCount => tryWrite8CallCount;

    /// <summary>
    /// Recomputes <see cref="PageEntry.FastBacking"/>, <see cref="PageEntry.FastBase"/>, and
    /// <see cref="PageEntry.FastOps"/> from the entry's target, window, permissions, and privilege.
    /// </summary>
    /// <param name="entry">The entry about to be stored. Caller-supplied fast-path fields are ignored.</param>
    /// <returns>The entry with fast-path fields that match the published target.</returns>
    /// <remarks>
    /// <para>
    /// Every method that stores a <see cref="PageEntry"/> calls this before the store returns.
    /// <see cref="Clear"/> does not. The method takes no lock; <see cref="SelectSwapVariant"/>
    /// already holds <c>swapGroupLock</c>, and the CPU fast path must not take it.
    /// </para>
    /// <para>
    /// An active mapping-trace range (spec section 13.1) forces <see cref="FastPathOps.None"/>
    /// on covered pages inside this same function, so a remap cannot put a covered page back
    /// on the fast path. That range is not implemented yet.
    /// </para>
    /// </remarks>
    private static PageEntry WithFastPath(PageEntry entry)
    {
        if (!TryGetFastSlice(entry, out ArraySegment<byte> segment))
        {
            return entry with
            {
                FastBacking = null,
                FastBase = 0,
                FastOps = FastPathOps.None,
            };
        }

        return entry with
        {
            FastBacking = segment.Array,
            FastBase = segment.Offset,
            FastOps = ComputeFastOps(entry),
        };
    }

    /// <summary>
    /// Resolves an array-backed window that contains the page's closed target-offset interval.
    /// </summary>
    /// <param name="entry">The entry being published.</param>
    /// <param name="segment">The array and the index of target-local offset 0 when eligible.</param>
    /// <returns><see langword="true"/> when the page may carry a backing array.</returns>
    private static bool TryGetFastSlice(PageEntry entry, out ArraySegment<byte> segment)
    {
        segment = default;

        if (entry.Target is null or ICompositeTarget)
        {
            return false;
        }

        if ((entry.Caps & (TargetCaps.HasSideEffects | TargetCaps.TimingSensitive)) != 0)
        {
            return false;
        }

        if (entry.MinReadPrivilege != PrivilegeLevel.Ring0
            || entry.MinWritePrivilege != PrivilegeLevel.Ring0
            || entry.MinExecutePrivilege != PrivilegeLevel.Ring0)
        {
            return false;
        }

        if (!TryGetArraySlice(entry.Target, out segment) || segment.Array is null)
        {
            return false;
        }

        // [PhysicalBase, PhysicalBase + 0xFFF] must lie inside the slice. A short target
        // or a base that runs off the end stays on the slow path. The CPU index does not
        // check the length.
        if (segment.Count < PageSize)
        {
            return false;
        }

        uint lastOffset = entry.PhysicalBase + (uint)(PageSize - 1);
        if (lastOffset < entry.PhysicalBase || lastOffset >= (uint)segment.Count)
        {
            return false;
        }

        long lastIndex = (long)segment.Offset + lastOffset;
        if (segment.Offset < 0 || lastIndex > int.MaxValue || lastIndex >= segment.Array.Length)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Recovers the array behind a <see cref="RamTarget"/> or <see cref="RomTarget"/> slice.
    /// </summary>
    /// <param name="target">The page target.</param>
    /// <param name="segment">The array segment when <paramref name="target"/> is array-backed.</param>
    /// <returns><see langword="true"/> when <see cref="MemoryMarshal.TryGetArray{T}(ReadOnlyMemory{T}, out ArraySegment{T})"/> succeeds.</returns>
    private static bool TryGetArraySlice(IBusTarget target, out ArraySegment<byte> segment)
    {
        switch (target)
        {
            case RamTarget ram:
                return MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)ram.GetBackingSlice(), out segment);
            case RomTarget rom:
                return MemoryMarshal.TryGetArray(rom.GetBackingSlice(), out segment);
            default:
                segment = default;
                return false;
        }
    }

    /// <summary>
    /// Computes the operations that may index an eligible page.
    /// </summary>
    /// <param name="entry">An entry that already passed the slice check.</param>
    /// <returns>The fast-path operations for <paramref name="entry"/>.</returns>
    private static FastPathOps ComputeFastOps(PageEntry entry)
    {
        FastPathOps ops = FastPathOps.None;
        if ((entry.Perms & PagePerms.Read) != 0)
        {
            ops |= FastPathOps.Read;
        }

        // A RomTarget never publishes Write. Normal ROM writes stay a slow-path permission
        // fault, and a page that has Write but ignores the store stays on RomTarget.Write8.
        if ((entry.Perms & PagePerms.Write) != 0 && entry.Target is RamTarget)
        {
            ops |= FastPathOps.Write;
        }

        if ((ops & FastPathOps.Read) != 0 && (entry.Perms & PagePerms.Execute) != 0)
        {
            ops |= FastPathOps.Fetch;
        }

        return ops;
    }
}