// <copyright file="IVideoSignal.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

/// <summary>
/// Marker for a published video signal. Concrete markers carry the compatibility rules
/// that pair an output with a frame provider.
/// </summary>
/// <remarks>
/// Signals are pooled reference types, never ref structs, so a lease can cross an await
/// or a dispatcher callback. Published sequences start at 1; 0 means none yet.
/// </remarks>
public interface IVideoSignal
{
    /// <summary>
    /// Gets the frame sequence this signal was generated from.
    /// </summary>
    /// <value>The sequence number. Published frames start at 1; 0 means none yet.</value>
    ulong Sequence { get; }

    /// <summary>
    /// Gets the machine cycle this signal was captured at.
    /// </summary>
    /// <value>The VBlank-start cycle, or the cycle of an explicit frame request.</value>
    ulong Cycle { get; }
}