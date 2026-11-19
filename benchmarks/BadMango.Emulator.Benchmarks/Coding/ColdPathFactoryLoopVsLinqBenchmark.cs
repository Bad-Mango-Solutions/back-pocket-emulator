// <copyright file="ColdPathFactoryLoopVsLinqBenchmark.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks.Coding;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Compares the machine-build factory loop with a LINQ equivalent.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Coding", "LinqLoops", "ColdPath")]
public class ColdPathFactoryLoopVsLinqBenchmark
{
    private readonly FactoryContext context = new();

    private readonly Func<FactoryContext, object?>[] factories =
    [
        static factoryContext => factoryContext.First,
        static _ => null,
        static factoryContext => factoryContext.Second,
        static _ => null,
        static factoryContext => factoryContext.Third,
        static _ => null,
        static factoryContext => factoryContext.Fourth,
        static _ => null,
    ];

    /// <summary>
    /// Invokes component factories and retains non-null results with a loop.
    /// </summary>
    /// <returns>The created components.</returns>
    [Benchmark(Baseline = true)]
    public List<object> HandWrittenLoop()
    {
        var components = new List<object>(factories.Length);
        foreach (Func<FactoryContext, object?> factory in factories)
        {
            object? created = factory(context);
            if (created is not null)
            {
                components.Add(created);
            }
        }

        return components;
    }

    /// <summary>
    /// Invokes component factories and retains non-null results with LINQ.
    /// </summary>
    /// <returns>The created components.</returns>
    [Benchmark]
    public List<object> LinqPipeline()
    {
        return factories.Select(factory => factory(context)).OfType<object>().ToList();
    }

    private sealed class FactoryContext
    {
        public object First { get; } = new();

        public object Second { get; } = new();

        public object Third { get; } = new();

        public object Fourth { get; } = new();
    }
}