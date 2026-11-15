// <copyright file="IFrameProvider.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// Turns a signal into a BGRA8888 frame. The provider is contravariant in its signal
/// so a provider written against a wider signal could accept a narrower one.
/// </summary>
/// <typeparam name="TSignal">The signal this provider can decode.</typeparam>
/// <remarks>
/// <para>
/// Declared beside the signal markers so <see cref="IVideoSignalSource{TSignal}"/> can
/// name it. The implementations live in the rendering assembly and are not part of this step.
/// Providers are stateless, immutable, and shared by every output and machine.
/// </para>
/// <para>
/// <typeparamref name="TSignal"/> is a class constraint so variance applies. Signals are
/// never ref structs.
/// </para>
/// </remarks>
public interface IFrameProvider<in TSignal>
    where TSignal : class, IVideoSignal
{
    /// <summary>
    /// Gets the stable provider id, such as "greyscale" or "ntsc".
    /// </summary>
    /// <value>The provider id used in profiles and cache keys.</value>
    string Id { get; }

    /// <summary>
    /// Gets the name shown in a monitor menu.
    /// </summary>
    /// <value>The display name.</value>
    string DisplayName { get; }

    /// <summary>
    /// Gets the immutable options. Each provider's default is its exact options.
    /// </summary>
    /// <value>The options this instance was constructed with.</value>
    FrameProviderOptions Options { get; }

    /// <summary>
    /// Gets the pixel geometry <see cref="Provide"/> will write for <paramref name="signal"/>.
    /// </summary>
    /// <param name="signal">The signal being measured.</param>
    /// <returns>The width, height, and stride in pixels.</returns>
    VideoFrameGeometry GetOutputGeometry(TSignal signal);

    /// <summary>
    /// Writes the frame for <paramref name="signal"/> into <paramref name="bgraOutput"/>.
    /// </summary>
    /// <param name="signal">The signal to decode.</param>
    /// <param name="bgraOutput">The destination, in BGRA8888 pixels.</param>
    /// <remarks>
    /// Allocates nothing. Writes <c>Height</c> rows of <c>Width</c> pixels at <c>Stride</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="bgraOutput"/> is shorter than stride times height.
    /// </exception>
    void Provide(TSignal signal, Span<uint> bgraOutput);
}