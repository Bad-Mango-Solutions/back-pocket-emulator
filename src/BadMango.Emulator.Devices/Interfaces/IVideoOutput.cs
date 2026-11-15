// <copyright file="IVideoOutput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// One video connector on a machine. Outputs are machine components.
/// </summary>
/// <remarks>
/// Several outputs on one machine share a captured frame and one scan per sequence.
/// Each generates its own signal at most once per sequence. Declared here; the
/// concrete connectors arrive in later steps.
/// </remarks>
public interface IVideoOutput : IDisposable
{
    /// <summary>
    /// Raised on the emulator thread right after a frame is published.
    /// </summary>
    /// <remarks>
    /// The argument is the sequence number. Handlers must not block and must not call
    /// back into the machine; they marshal to their own thread.
    /// </remarks>
    event Action<ulong>? FrameReady;

    /// <summary>
    /// Gets the connector name, such as "composite" or "rgb".
    /// </summary>
    /// <value>The output name.</value>
    string Name { get; }

    /// <summary>
    /// Gets the signal marker this output produces.
    /// </summary>
    /// <value><c>typeof(ICompositeSignal)</c> or <c>typeof(IRgbSignal)</c>.</value>
    Type SignalType { get; }

    /// <summary>
    /// Gets the newest published sequence, or 0 when nothing has been published.
    /// </summary>
    /// <value>The latest sequence number.</value>
    ulong LatestSequence { get; }

    /// <summary>
    /// Asks the machine for a frame when it is not running.
    /// </summary>
    /// <remarks>
    /// If the machine is running, this is a no-op: the next VBlank is at most one frame
    /// away. Otherwise the capture is posted through <c>IMachineDispatcher.Post</c> and
    /// runs inline when the machine is quiescent.
    /// </remarks>
    void RequestFrame();

    /// <summary>
    /// Gets a snapshot of the pipeline counters.
    /// </summary>
    /// <returns>The current <see cref="VideoPipelineStats"/>.</returns>
    VideoPipelineStats GetStats();

    /// <summary>
    /// Dispatches to a visitor so a caller that holds only <see cref="IVideoOutput"/>
    /// can reach the typed signal source.
    /// </summary>
    /// <typeparam name="TResult">The result type produced by the visitor.</typeparam>
    /// <param name="visitor">The visitor that receives the typed output.</param>
    /// <returns>The value returned by <paramref name="visitor"/>.</returns>
    TResult Accept<TResult>(IVideoOutputVisitor<TResult> visitor);
}