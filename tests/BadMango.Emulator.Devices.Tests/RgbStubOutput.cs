// <copyright file="RgbStubOutput.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// Stub RGB output used by the compile-time pairing test.
/// </summary>
public sealed class RgbStubOutput : IVideoSignalSource<IRgbSignal>
{
    private Action<ulong>? frameReady;

    /// <summary>
    /// Raised when <see cref="RequestFrame"/> is called. The pairing snippets never subscribe.
    /// </summary>
    public event Action<ulong>? FrameReady
    {
        add
        {
            frameReady += value;
        }

        remove
        {
            frameReady -= value;
        }
    }

    /// <summary>
    /// Gets the connector name.
    /// </summary>
    /// <value><c>rgb</c>.</value>
    public string Name => "rgb";

    /// <summary>
    /// Gets the RGB signal marker.
    /// </summary>
    /// <value><see cref="IRgbSignal"/>.</value>
    public Type SignalType => typeof(IRgbSignal);

    /// <summary>
    /// Gets the latest sequence. The stub has never published a frame.
    /// </summary>
    /// <value>Zero.</value>
    public ulong LatestSequence => 0;

    /// <summary>
    /// Raises <see cref="FrameReady"/> so the event is used.
    /// </summary>
    public void RequestFrame()
    {
        frameReady?.Invoke(LatestSequence);
    }

    /// <summary>
    /// Gets an empty stats snapshot.
    /// </summary>
    /// <returns>The default <see cref="VideoPipelineStats"/>.</returns>
    public VideoPipelineStats GetStats() => default;

    /// <summary>
    /// Dispatches <paramref name="visitor"/> to this RGB output.
    /// </summary>
    /// <typeparam name="TResult">The visitor's result type.</typeparam>
    /// <param name="visitor">The visitor.</param>
    /// <returns>The visitor's result.</returns>
    public TResult Accept<TResult>(IVideoOutputVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }

    /// <summary>
    /// Releases nothing. The stub holds no resources.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Does not lease a signal.
    /// </summary>
    /// <param name="signal">The default lease.</param>
    /// <returns><see langword="false"/>.</returns>
    public bool TryAcquireLatestSignal(out SignalLease<IRgbSignal> signal)
    {
        signal = default;
        return false;
    }

    /// <summary>
    /// Does not lease a frame. The pairing test only needs the signature to compile.
    /// </summary>
    /// <param name="provider">The RGB provider that would pass the frame through.</param>
    /// <param name="frame">The default frame.</param>
    /// <returns><see langword="false"/>.</returns>
    public bool TryAcquireLatestFrame(IFrameProvider<IRgbSignal> provider, out VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(provider);
        frame = default;
        return false;
    }
}