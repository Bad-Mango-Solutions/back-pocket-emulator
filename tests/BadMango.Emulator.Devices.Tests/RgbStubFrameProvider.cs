// <copyright file="RgbStubFrameProvider.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// Stub RGB frame provider used by the compile-time pairing test.
/// </summary>
public sealed class RgbStubFrameProvider : IFrameProvider<IRgbSignal>
{
    /// <summary>
    /// Gets the stub provider id.
    /// </summary>
    /// <value>The id <c>rgb-stub</c>.</value>
    public string Id => "rgb-stub";

    /// <summary>
    /// Gets the stub display name.
    /// </summary>
    /// <value>The name shown if a menu ever listed this stub.</value>
    public string DisplayName => "RGB stub";

    /// <summary>
    /// Gets the exact RGB options.
    /// </summary>
    /// <value>A default <see cref="RgbOptions"/>.</value>
    public FrameProviderOptions Options => new RgbOptions();

    /// <summary>
    /// Gets a geometry derived from the signal.
    /// </summary>
    /// <param name="signal">The RGB signal being measured.</param>
    /// <returns>The signal's width, height, and stride.</returns>
    public VideoFrameGeometry GetOutputGeometry(IRgbSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new VideoFrameGeometry(signal.Width, signal.Height, signal.Stride);
    }

    /// <summary>
    /// Accepts a destination and writes nothing. The pairing test only needs this to compile.
    /// </summary>
    /// <param name="signal">The RGB signal.</param>
    /// <param name="bgraOutput">The destination pixels.</param>
    public void Provide(IRgbSignal signal, Span<uint> bgraOutput)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _ = bgraOutput.Length;
    }
}