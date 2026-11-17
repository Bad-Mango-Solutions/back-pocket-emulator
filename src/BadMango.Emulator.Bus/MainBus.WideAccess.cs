// <copyright file="MainBus.WideAccess.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using System.Diagnostics.CodeAnalysis;

using Interfaces;

/// <content>
/// Wide access (16/32-bit) operations and decomposition helpers for the main memory bus.
/// </content>
public sealed partial class MainBus
{
    /// <inheritdoc />
    /// <remarks>
    /// A same-page access whose live slot publishes <see cref="FastPathOps.Read"/> is indexed
    /// and does not call <see cref="IBusTarget.Read16"/>. An access that crosses a 4 KB page
    /// is one byte at a time, and each byte indexes or uses <see cref="Read8"/>.
    /// </remarks>
    public Word Read16(in BusAccess access)
    {
        if (TryReadIndexedSpan(access.Address, 2, out uint indexed))
        {
            return (Word)indexed;
        }

        // A 4 KB crossing is one byte at a time. Each byte indexes or uses Read8.
        if (CrossesPageBoundary(access.Address, 2))
        {
            return (Word)ReadIndexedSplit(access, 2);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeRead16(access);
        }

        ref readonly var page = ref pageTable[access.Address >> PageShift];

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            return InvokeTargetRead16(page.Target!, physicalAddress, access);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeRead16(access);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            return InvokeTargetRead16(page.Target!, physicalAddress, access);
        }

        return DecomposeRead16(access);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A same-page access whose live slot publishes <see cref="FastPathOps.Write"/> is indexed
    /// and does not call <see cref="IBusTarget.Write16"/>. A <see cref="RomTarget"/> never
    /// publishes write. An access that crosses a 4 KB page is one byte at a time.
    /// </remarks>
    public void Write16(in BusAccess access, Word value)
    {
        if (TryWriteIndexedSpan(access.Address, value, 2))
        {
            return;
        }

        if (CrossesPageBoundary(access.Address, 2))
        {
            WriteIndexedSplit(access, value, 2);
            return;
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            DecomposeWrite16(access, value);
            return;
        }

        ref readonly var page = ref pageTable[access.Address >> PageShift];

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            InvokeTargetWrite16(page.Target!, physicalAddress, value, access);
            return;
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            DecomposeWrite16(access, value);
            return;
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            InvokeTargetWrite16(page.Target!, physicalAddress, value, access);
            return;
        }

        DecomposeWrite16(access, value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A same-page access whose live slot publishes <see cref="FastPathOps.Read"/> is indexed
    /// and does not call <see cref="IBusTarget.Read32"/>. An access that crosses a 4 KB page
    /// is one byte at a time.
    /// </remarks>
    public DWord Read32(in BusAccess access)
    {
        if (TryReadIndexedSpan(access.Address, 4, out uint indexed))
        {
            return indexed;
        }

        if (CrossesPageBoundary(access.Address, 4))
        {
            return ReadIndexedSplit(access, 4);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeRead32(access);
        }

        ref readonly var page = ref pageTable[access.Address >> PageShift];

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            return InvokeTargetRead32(page.Target!, physicalAddress, access);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeRead32(access);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            return InvokeTargetRead32(page.Target!, physicalAddress, access);
        }

        return DecomposeRead32(access);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A same-page access whose live slot publishes <see cref="FastPathOps.Write"/> is indexed
    /// and does not call <see cref="IBusTarget.Write32"/>. An access that crosses a 4 KB page
    /// is one byte at a time.
    /// </remarks>
    public void Write32(in BusAccess access, DWord value)
    {
        if (TryWriteIndexedSpan(access.Address, value, 4))
        {
            return;
        }

        if (CrossesPageBoundary(access.Address, 4))
        {
            WriteIndexedSplit(access, value, 4);
            return;
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            DecomposeWrite32(access, value);
            return;
        }

        ref readonly var page = ref pageTable[access.Address >> PageShift];

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            InvokeTargetWrite32(page.Target!, physicalAddress, value, access);
            return;
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            DecomposeWrite32(access, value);
            return;
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            InvokeTargetWrite32(page.Target!, physicalAddress, value, access);
            return;
        }

        DecomposeWrite32(access, value);
    }

    /// <inheritdoc />
    public BusResult<Word> TryRead16(in BusAccess access)
    {
        // Cross-page check: always decompose
        if (CrossesPageBoundary(access.Address, 2))
        {
            return DecomposeTryRead16(access);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeTryRead16(access);
        }

        int pageIndex = (int)(access.Address >> PageShift);
        if (pageIndex >= pageTable.Length)
        {
            return LatchAndReturn(in access, BusFault.Unmapped(in access));
        }

        ref readonly var page = ref pageTable[pageIndex];

        // Check for unmapped page
        if (page.Target is null)
        {
            return LatchAndReturn(in access, BusFault.Unmapped(in access));
        }

        // Check read permission
        if (!page.CanRead)
        {
            return LatchAndReturn(in access, BusFault.PermissionDenied(in access, page.DeviceId, page.RegionTag));
        }

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            Word value = page.Target.Read16(physicalAddress, access);
            return BusResult<Word>.Success(value, access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeTryRead16(access);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            Word value = page.Target.Read16(physicalAddress, access);
            return BusResult<Word>.Success(value, access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        return DecomposeTryRead16(access);
    }

    /// <inheritdoc />
    public BusResult TryWrite16(in BusAccess access, Word value)
    {
        // Cross-page check: always decompose
        if (CrossesPageBoundary(access.Address, 2))
        {
            return DecomposeTryWrite16(access, value);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeTryWrite16(access, value);
        }

        int pageIndex = (int)(access.Address >> PageShift);
        if (pageIndex >= pageTable.Length)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.Unmapped(in access)));
        }

        ref readonly var page = ref pageTable[pageIndex];

        // Check for unmapped page
        if (page.Target is null)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.Unmapped(in access)));
        }

        // Check write permission
        if (!page.CanWrite)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.PermissionDenied(in access, page.DeviceId, page.RegionTag)));
        }

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            page.Target.Write16(physicalAddress, value, access);
            return BusResult.Success(access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeTryWrite16(access, value);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            page.Target.Write16(physicalAddress, value, access);
            return BusResult.Success(access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        return DecomposeTryWrite16(access, value);
    }

    /// <inheritdoc />
    public BusResult<DWord> TryRead32(in BusAccess access)
    {
        // Cross-page check: always decompose
        if (CrossesPageBoundary(access.Address, 4))
        {
            return DecomposeTryRead32(access);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeTryRead32(access);
        }

        int pageIndex = (int)(access.Address >> PageShift);
        if (pageIndex >= pageTable.Length)
        {
            return LatchAndReturn(in access, BusFault.Unmapped(in access));
        }

        ref readonly var page = ref pageTable[pageIndex];

        // Check for unmapped page
        if (page.Target is null)
        {
            return LatchAndReturn(in access, BusFault.Unmapped(in access));
        }

        // Check read permission
        if (!page.CanRead)
        {
            return LatchAndReturn(in access, BusFault.PermissionDenied(in access, page.DeviceId, page.RegionTag));
        }

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            DWord value = page.Target.Read32(physicalAddress, access);
            return BusResult<DWord>.Success(value, access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeTryRead32(access);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            DWord value = page.Target.Read32(physicalAddress, access);
            return BusResult<DWord>.Success(value, access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        return DecomposeTryRead32(access);
    }

    /// <inheritdoc />
    public BusResult TryWrite32(in BusAccess access, DWord value)
    {
        // Cross-page check: always decompose
        if (CrossesPageBoundary(access.Address, 4))
        {
            return DecomposeTryWrite32(access, value);
        }

        // Decompose flag forces byte-wise
        if (access.IsDecomposeForced)
        {
            return DecomposeTryWrite32(access, value);
        }

        int pageIndex = (int)(access.Address >> PageShift);
        if (pageIndex >= pageTable.Length)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.Unmapped(in access)));
        }

        ref readonly var page = ref pageTable[pageIndex];

        // Check for unmapped page
        if (page.Target is null)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.Unmapped(in access)));
        }

        // Check write permission
        if (!page.CanWrite)
        {
            return BusResult.FromFault(LatchAndReturn(in access, BusFault.PermissionDenied(in access, page.DeviceId, page.RegionTag)));
        }

        // Atomic request + target supports it
        if (access.IsAtomicRequested && page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            page.Target.Write32(physicalAddress, value, access);
            return BusResult.Success(access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        // Decomposed mode default: decompose (Apple II expects byte-visible cycles)
        if (access.Mode == BusAccessMode.Decomposed)
        {
            return DecomposeTryWrite32(access, value);
        }

        // Atomic mode: use wide if available
        if (page.SupportsWide)
        {
            Addr physicalAddress = page.PhysicalBase + (access.Address & PageMask);
            page.Target.Write32(physicalAddress, value, access);
            return BusResult.Success(access, page.DeviceId, page.RegionTag, cycles: 1);
        }

        return DecomposeTryWrite32(access, value);
    }

    /// <summary>
    /// Checks if an access of the given width crosses a page boundary.
    /// </summary>
    /// <param name="address">The starting address.</param>
    /// <param name="bytes">The number of bytes in the access.</param>
    /// <returns><see langword="true"/> if the access crosses a page boundary; otherwise, <see langword="false"/>.</returns>
    private static bool CrossesPageBoundary(Addr address, int bytes)
    {
        return ((address & DefaultPageMask) + (uint)(bytes - 1)) > DefaultPageMask;
    }

    /// <summary>
    /// Decomposes a 16-bit read into two 8-bit reads.
    /// </summary>
    private Word DecomposeRead16(in BusAccess access)
    {
        var access0 = access with { WidthBits = 8 };
        byte low = Read8(access0);

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        byte high = Read8(access1);

        return (Word)(low | (high << 8));
    }

    /// <summary>
    /// Decomposes a 16-bit write into two 8-bit writes.
    /// </summary>
    private void DecomposeWrite16(in BusAccess access, Word value)
    {
        var access0 = access with { WidthBits = 8 };
        Write8(access0, (byte)value);

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        Write8(access1, (byte)(value >> 8));
    }

    /// <summary>
    /// Decomposes a 32-bit read into four 8-bit reads.
    /// </summary>
    private DWord DecomposeRead32(in BusAccess access)
    {
        var access0 = access with { WidthBits = 8 };
        byte b0 = Read8(access0);

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        byte b1 = Read8(access1);

        var access2 = access with { Address = access.Address + 2, WidthBits = 8 };
        byte b2 = Read8(access2);

        var access3 = access with { Address = access.Address + 3, WidthBits = 8 };
        byte b3 = Read8(access3);

        return (DWord)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
    }

    /// <summary>
    /// Decomposes a 32-bit write into four 8-bit writes.
    /// </summary>
    private void DecomposeWrite32(in BusAccess access, DWord value)
    {
        var access0 = access with { WidthBits = 8 };
        Write8(access0, (byte)value);

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        Write8(access1, (byte)(value >> 8));

        var access2 = access with { Address = access.Address + 2, WidthBits = 8 };
        Write8(access2, (byte)(value >> 16));

        var access3 = access with { Address = access.Address + 3, WidthBits = 8 };
        Write8(access3, (byte)(value >> 24));
    }

    /// <summary>
    /// Decomposes a 16-bit try-read into two 8-bit try-reads.
    /// </summary>
    private BusResult<Word> DecomposeTryRead16(in BusAccess access)
    {
        var access0 = access with { WidthBits = 8 };
        var result0 = TryRead8(access0);
        if (result0.Failed)
        {
            return BusResult<Word>.FromFault(result0.Fault);
        }

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        var result1 = TryRead8(access1);
        if (result1.Failed)
        {
            return BusResult<Word>.FromFault(result1.Fault, cycles: result0.Cycles);
        }

        Word value = (Word)(result0.Value | (result1.Value << 8));
        return BusResult<Word>.Success(value, cycles: result0.Cycles + result1.Cycles);
    }

    /// <summary>
    /// Decomposes a 16-bit try-write into two 8-bit try-writes.
    /// </summary>
    private BusResult DecomposeTryWrite16(in BusAccess access, Word value)
    {
        var access0 = access with { WidthBits = 8 };
        var result0 = TryWrite8(access0, (byte)value);
        if (result0.Failed)
        {
            return result0;
        }

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        var result1 = TryWrite8(access1, (byte)(value >> 8));
        if (result1.Failed)
        {
            return BusResult.FromFault(result1.Fault, cycles: result0.Cycles);
        }

        return BusResult.Success(cycles: result0.Cycles + result1.Cycles);
    }

    /// <summary>
    /// Decomposes a 32-bit try-read into four 8-bit try-reads.
    /// </summary>
    private BusResult<DWord> DecomposeTryRead32(in BusAccess access)
    {
        var access0 = access with { WidthBits = 8 };
        var result0 = TryRead8(access0);
        if (result0.Failed)
        {
            return BusResult<DWord>.FromFault(result0.Fault);
        }

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        var result1 = TryRead8(access1);
        if (result1.Failed)
        {
            return BusResult<DWord>.FromFault(result1.Fault, cycles: result0.Cycles);
        }

        var access2 = access with { Address = access.Address + 2, WidthBits = 8 };
        var result2 = TryRead8(access2);
        if (result2.Failed)
        {
            return BusResult<DWord>.FromFault(result2.Fault, cycles: result0.Cycles + result1.Cycles);
        }

        var access3 = access with { Address = access.Address + 3, WidthBits = 8 };
        var result3 = TryRead8(access3);
        if (result3.Failed)
        {
            return BusResult<DWord>.FromFault(result3.Fault, cycles: result0.Cycles + result1.Cycles + result2.Cycles);
        }

        DWord value = (DWord)(result0.Value | (result1.Value << 8) | (result2.Value << 16) | (result3.Value << 24));
        return BusResult<DWord>.Success(value, cycles: result0.Cycles + result1.Cycles + result2.Cycles + result3.Cycles);
    }

    /// <summary>
    /// Decomposes a 32-bit try-write into four 8-bit try-writes.
    /// </summary>
    private BusResult DecomposeTryWrite32(in BusAccess access, DWord value)
    {
        var access0 = access with { WidthBits = 8 };
        var result0 = TryWrite8(access0, (byte)value);
        if (result0.Failed)
        {
            return result0;
        }

        var access1 = access with { Address = access.Address + 1, WidthBits = 8 };
        var result1 = TryWrite8(access1, (byte)(value >> 8));
        if (result1.Failed)
        {
            return BusResult.FromFault(result1.Fault, cycles: result0.Cycles);
        }

        var access2 = access with { Address = access.Address + 2, WidthBits = 8 };
        var result2 = TryWrite8(access2, (byte)(value >> 16));
        if (result2.Failed)
        {
            return BusResult.FromFault(result2.Fault, cycles: result0.Cycles + result1.Cycles);
        }

        var access3 = access with { Address = access.Address + 3, WidthBits = 8 };
        var result3 = TryWrite8(access3, (byte)(value >> 24));
        if (result3.Failed)
        {
            return BusResult.FromFault(result3.Fault, cycles: result0.Cycles + result1.Cycles + result2.Cycles);
        }

        return BusResult.Success(cycles: result0.Cycles + result1.Cycles + result2.Cycles + result3.Cycles);
    }

    /// <summary>
    /// Indexes every byte of a same-page read when each live slot publishes <see cref="FastPathOps.Read"/>.
    /// </summary>
    /// <param name="address">The address of the least-significant byte.</param>
    /// <param name="bytes">The number of bytes.</param>
    /// <param name="value">The little-endian value when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when every byte was indexed.</returns>
    private bool TryReadIndexedSpan(Addr address, int bytes, out uint value)
    {
        value = 0;
        if (CrossesPageBoundary(address, bytes))
        {
            return false;
        }

        uint assembled = 0;
        for (int i = 0; i < bytes; i++)
        {
            if (!TryReadIndexed(address + (uint)i, out byte piece))
            {
                return false;
            }

            assembled |= (uint)piece << (8 * i);
        }

        value = assembled;
        return true;
    }

    /// <summary>
    /// Stores every byte of a same-page write when each live slot publishes <see cref="FastPathOps.Write"/>.
    /// </summary>
    /// <param name="address">The address of the least-significant byte.</param>
    /// <param name="value">The little-endian value.</param>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>
    /// <see langword="true"/> when the store is finished. A miss on the first byte returns
    /// <see langword="false"/> so the caller keeps the target wide path.
    /// </returns>
    private bool TryWriteIndexedSpan(Addr address, uint value, int bytes)
    {
        if (CrossesPageBoundary(address, bytes))
        {
            return false;
        }

        for (int i = 0; i < bytes; i++)
        {
            if (TryWriteIndexed(address + (uint)i, (byte)(value >> (8 * i))))
            {
                continue;
            }

            return i > 0;
        }

        return true;
    }

    /// <summary>
    /// Reads a crossing access one byte at a time, indexing a byte that publishes <see cref="FastPathOps.Read"/>.
    /// </summary>
    /// <param name="access">The wide access. Each byte keeps its intent and replaces the address.</param>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The little-endian value.</returns>
    private uint ReadIndexedSplit(in BusAccess access, int bytes)
    {
        uint assembled = 0;
        for (int i = 0; i < bytes; i++)
        {
            byte piece = ReadIndexedOrSlow(access, access.Address + (uint)i);
            assembled |= (uint)piece << (8 * i);
        }

        return assembled;
    }

    /// <summary>
    /// Stores a crossing access one byte at a time, indexing a byte that publishes <see cref="FastPathOps.Write"/>.
    /// </summary>
    /// <param name="access">The wide access.</param>
    /// <param name="value">The little-endian value.</param>
    /// <param name="bytes">The number of bytes.</param>
    private void WriteIndexedSplit(in BusAccess access, uint value, int bytes)
    {
        for (int i = 0; i < bytes; i++)
        {
            WriteIndexedOrSlow(access, access.Address + (uint)i, (byte)(value >> (8 * i)));
        }
    }

    /// <summary>
    /// Indexes one byte when the live slot publishes <see cref="FastPathOps.Read"/>.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="value">The indexed byte when this method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the byte was indexed.</returns>
    private bool TryReadIndexed(Addr address, out byte value)
    {
        value = 0;
        if (!TryGetIndexedSlot(address, FastPathOps.Read, out byte[]? backing, out int index))
        {
            return false;
        }

        value = backing[index];
        return true;
    }

    /// <summary>
    /// Stores one byte when the live slot publishes <see cref="FastPathOps.Write"/>.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="stored">The byte to store.</param>
    /// <returns><see langword="true"/> when the byte was indexed.</returns>
    private bool TryWriteIndexed(Addr address, byte stored)
    {
        if (!TryGetIndexedSlot(address, FastPathOps.Write, out byte[]? backing, out int index))
        {
            return false;
        }

        backing[index] = stored;
        return true;
    }

    /// <summary>
    /// Resolves the live backing array when <paramref name="operation"/> is published.
    /// </summary>
    /// <param name="address">The CPU address.</param>
    /// <param name="operation">The operation that must be published.</param>
    /// <param name="backing">The page array when this method returns <see langword="true"/>.</param>
    /// <param name="index">The index of <paramref name="address"/> in <paramref name="backing"/>.</param>
    /// <returns><see langword="true"/> when the page can be indexed for <paramref name="operation"/>.</returns>
    private bool TryGetIndexedSlot(
        Addr address,
        FastPathOps operation,
        [NotNullWhen(true)] out byte[]? backing,
        out int index)
    {
        backing = null;
        index = 0;
        int pageIndex = (int)(address >> PageShift);
        if ((uint)pageIndex >= (uint)pageTable.Length)
        {
            return false;
        }

        // Eligibility is FastOps. The null check only refuses an index that would throw.
        ref readonly PageEntry page = ref pageTable[pageIndex];
        byte[]? slot = page.FastBacking;
        if ((page.FastOps & operation) == 0 || slot is null)
        {
            return false;
        }

        backing = slot;
        index = page.FastBase + (int)page.PhysicalBase + (int)(address & PageMask);
        return true;
    }

    /// <summary>
    /// Indexes one read byte, or uses <see cref="Read8"/> when the page does not publish read.
    /// </summary>
    /// <param name="access">The wide access whose address is replaced.</param>
    /// <param name="address">The byte address.</param>
    /// <returns>The byte at <paramref name="address"/>.</returns>
    private byte ReadIndexedOrSlow(in BusAccess access, Addr address)
    {
        if (TryReadIndexed(address, out byte value))
        {
            return value;
        }

        return Read8(access with { Address = address, WidthBits = 8 });
    }

    /// <summary>
    /// Indexes one stored byte, or uses <see cref="Write8"/> when the page does not publish write.
    /// </summary>
    /// <param name="access">The wide access whose address is replaced.</param>
    /// <param name="address">The byte address.</param>
    /// <param name="stored">The byte to store.</param>
    private void WriteIndexedOrSlow(in BusAccess access, Addr address, byte stored)
    {
        if (TryWriteIndexed(address, stored))
        {
            return;
        }

        Write8(access with { Address = address, WidthBits = 8 }, stored);
    }

    /// <summary>
    /// Calls <see cref="IBusTarget.Read16"/> and counts the call.
    /// </summary>
    /// <param name="target">The page target.</param>
    /// <param name="physicalAddress">The target-local address.</param>
    /// <param name="access">The wide access.</param>
    /// <returns>The target's 16-bit value.</returns>
    private Word InvokeTargetRead16(IBusTarget target, Addr physicalAddress, in BusAccess access)
    {
        targetWideCallCount++;
        return target.Read16(physicalAddress, access);
    }

    /// <summary>
    /// Calls <see cref="IBusTarget.Write16"/> and counts the call.
    /// </summary>
    /// <param name="target">The page target.</param>
    /// <param name="physicalAddress">The target-local address.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="access">The wide access.</param>
    private void InvokeTargetWrite16(IBusTarget target, Addr physicalAddress, Word value, in BusAccess access)
    {
        targetWideCallCount++;
        target.Write16(physicalAddress, value, access);
    }

    /// <summary>
    /// Calls <see cref="IBusTarget.Read32"/> and counts the call.
    /// </summary>
    /// <param name="target">The page target.</param>
    /// <param name="physicalAddress">The target-local address.</param>
    /// <param name="access">The wide access.</param>
    /// <returns>The target's 32-bit value.</returns>
    private DWord InvokeTargetRead32(IBusTarget target, Addr physicalAddress, in BusAccess access)
    {
        targetWideCallCount++;
        return target.Read32(physicalAddress, access);
    }

    /// <summary>
    /// Calls <see cref="IBusTarget.Write32"/> and counts the call.
    /// </summary>
    /// <param name="target">The page target.</param>
    /// <param name="physicalAddress">The target-local address.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="access">The wide access.</param>
    private void InvokeTargetWrite32(IBusTarget target, Addr physicalAddress, DWord value, in BusAccess access)
    {
        targetWideCallCount++;
        target.Write32(physicalAddress, value, access);
    }
}