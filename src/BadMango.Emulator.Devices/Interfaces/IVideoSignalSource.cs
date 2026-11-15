// <copyright file="IVideoSignalSource.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// An output that produces a signal of type <typeparamref name="TSignal"/> and can
/// lease a frame decoded by a compatible provider.
/// </summary>
/// <typeparam name="TSignal">The signal marker. Must be a reference type implementing <see cref="IVideoSignal"/>.</typeparam>
/// <remarks>
/// The type parameter is invariant. <see cref="TryAcquireLatestFrame"/> accepts only an
/// <see cref="IFrameProvider{TSignal}"/> of this same signal, so an incompatible provider
/// is a compile error (CS1503).
/// </remarks>
public interface IVideoSignalSource<TSignal> : IVideoOutput
    where TSignal : class, IVideoSignal
{
    /// <summary>
    /// Tries to lease the newest signal.
    /// </summary>
    /// <param name="signal">
    /// When this method returns <see langword="true"/>, the leased signal. The default
    /// value when this method returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a lease was taken; <see langword="false"/> when no
    /// signal is available or the pool cannot take another holder.
    /// </returns>
    bool TryAcquireLatestSignal(out SignalLease<TSignal> signal);

    /// <summary>
    /// Tries to lease the newest frame for this output, shared per sequence, output,
    /// provider id, and provider options.
    /// </summary>
    /// <param name="provider">A provider compatible with <typeparamref name="TSignal"/>.</param>
    /// <param name="frame">
    /// When this method returns <see langword="true"/>, the leased frame. The default
    /// value when this method returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a lease was taken; <see langword="false"/> when no
    /// frame is available or the pool cannot take another holder.
    /// </returns>
    bool TryAcquireLatestFrame(IFrameProvider<TSignal> provider, out VideoFrame frame);
}