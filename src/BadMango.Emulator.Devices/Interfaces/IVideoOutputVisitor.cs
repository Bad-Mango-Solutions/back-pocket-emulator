// <copyright file="IVideoOutputVisitor.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Typed dispatch for code that holds an output as <see cref="IVideoOutput"/>.
/// </summary>
/// <typeparam name="TResult">The result produced for the visited output.</typeparam>
public interface IVideoOutputVisitor<TResult>
{
    /// <summary>
    /// Visits one typed output.
    /// </summary>
    /// <typeparam name="TSignal">The signal produced by <paramref name="output"/>.</typeparam>
    /// <param name="output">The output to visit.</param>
    /// <returns>The visitor's result for <paramref name="output"/>.</returns>
    TResult Visit<TSignal>(IVideoSignalSource<TSignal> output)
        where TSignal : class, IVideoSignal;
}