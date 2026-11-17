// <copyright file="Program.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Benchmarks;

using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

/// <summary>
/// Entry point for the emulator benchmarks.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the benchmarks selected by the command line.
    /// </summary>
    /// <param name="args">BenchmarkDotNet arguments, including <c>--filter</c>.</param>
    /// <returns>Zero when every selected benchmark completed, otherwise one.</returns>
    public static int Main(string[] args)
    {
        foreach (Summary summary in BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args))
        {
            if (summary.HasCriticalValidationErrors)
            {
                return 1;
            }
        }

        return 0;
    }
}