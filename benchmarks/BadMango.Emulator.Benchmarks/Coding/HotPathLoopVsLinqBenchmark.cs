// <copyright file="HotPathLoopVsLinqBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Coding;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Compares a hand-written loop with LINQ for a repeated, small-array filter and aggregation.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Coding", "LinqLoops", "HotPath")]
public class HotPathLoopVsLinqBenchmark
{
    private readonly int[] samples =
    [
        0, 1, 2, 3, 4, 5, 6, 7,
        8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23,
        24, 25, 26, 27, 28, 29, 30, 31,
    ];

    /// <summary>
    /// Filters and sums the samples with an indexed hand-written loop.
    /// </summary>
    /// <returns>The sum of the selected samples.</returns>
    [Benchmark(Baseline = true)]
    public int HandWrittenLoop()
    {
        int sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            int sample = samples[i];
            if ((sample & 1) == 0)
            {
                sum += sample;
            }
        }

        return sum;
    }

    /// <summary>
    /// Filters and sums the samples with LINQ.
    /// </summary>
    /// <returns>The sum of the selected samples.</returns>
    [Benchmark]
    public int LinqPipeline()
    {
        return samples.Where(sample => (sample & 1) == 0).Sum();
    }
}