// <copyright file="MappingTraceRing.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Bus;

using Interfaces;

/// <summary>
/// A fixed-capacity ring of <see cref="MappingTraceRecord"/> values.
/// </summary>
/// <remarks>
/// The buffer is allocated in the constructor. <see cref="Record"/> copies a struct into
/// that buffer under a <see cref="Lock"/> and does not allocate. The same shape as
/// <see cref="BusFaultRing"/>.
/// </remarks>
public sealed class MappingTraceRing : IMappingTraceRing
{
    /// <summary>
    /// The default number of records retained.
    /// </summary>
    public const int DefaultCapacity = 256;

    private readonly MappingTraceRecord[] buffer;
    private readonly Lock syncLock = new();

    private int writeIndex;
    private int count;
    private ulong totalRecords;

    /// <summary>
    /// Initializes a new instance of the <see cref="MappingTraceRing"/> class.
    /// </summary>
    /// <param name="capacity">How many records to retain. Must be at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is less than one.</exception>
    public MappingTraceRing(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        buffer = new MappingTraceRecord[capacity];
    }

    /// <inheritdoc />
    public int Capacity => buffer.Length;

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (syncLock)
            {
                return count;
            }
        }
    }

    /// <inheritdoc />
    public ulong TotalRecords
    {
        get
        {
            lock (syncLock)
            {
                return totalRecords;
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="record"/> by copying it into the pre-sized buffer.
    /// </summary>
    /// <param name="record">The access or remap to retain.</param>
    public void Record(in MappingTraceRecord record)
    {
        lock (syncLock)
        {
            buffer[writeIndex] = record;
            writeIndex++;
            if (writeIndex == buffer.Length)
            {
                writeIndex = 0;
            }

            if (count < buffer.Length)
            {
                count++;
            }

            totalRecords++;
        }
    }

    /// <inheritdoc />
    public MappingTraceRecord[] Snapshot()
    {
        lock (syncLock)
        {
            if (count == 0)
            {
                return [];
            }

            var snapshot = new MappingTraceRecord[count];
            int start = count < buffer.Length ? 0 : writeIndex;
            for (int i = 0; i < count; i++)
            {
                snapshot[i] = buffer[(start + i) % buffer.Length];
            }

            return snapshot;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (syncLock)
        {
            Array.Clear(buffer);
            writeIndex = 0;
            count = 0;
            totalRecords = 0;
        }
    }
}