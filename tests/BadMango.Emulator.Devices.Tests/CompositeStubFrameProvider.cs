// <copyright file="CompositeStubFrameProvider.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Tests;

/// <summary>
/// Stub composite frame provider used by the compile-time pairing test.
/// </summary>
public sealed class CompositeStubFrameProvider : IFrameProvider<ICompositeSignal>
{
    /// <summary>
    /// Gets the stub provider id.
    /// </summary>
    /// <value>The id <c>composite-stub</c>.</value>
    public string Id => "composite-stub";

    /// <summary>
    /// Gets the stub display name.
    /// </summary>
    /// <value>The name shown if a menu ever listed this stub.</value>
    public string DisplayName => "Composite stub";

    /// <summary>
    /// Gets the exact monochrome options.
    /// </summary>
    /// <value>A default <see cref="MonochromeOptions"/>.</value>
    public FrameProviderOptions Options => new MonochromeOptions();

    /// <summary>
    /// Gets a geometry derived from the signal's dot and line counts.
    /// </summary>
    /// <param name="signal">The composite signal being measured.</param>
    /// <returns>A geometry using the signal's dot and line counts.</returns>
    public VideoFrameGeometry GetOutputGeometry(ICompositeSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new VideoFrameGeometry(signal.DotsPerLine, signal.Lines, signal.DotsPerLine);
    }

    /// <summary>
    /// Accepts a destination and writes nothing. The pairing test only needs this to compile.
    /// </summary>
    /// <param name="signal">The composite signal.</param>
    /// <param name="bgraOutput">The destination pixels.</param>
    public void Provide(ICompositeSignal signal, Span<uint> bgraOutput)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _ = bgraOutput.Length;
    }
}