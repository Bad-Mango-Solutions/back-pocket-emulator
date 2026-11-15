// <copyright file="IVideoScanner.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.Interfaces;

using BadMango.Emulator.Devices;

/// <summary>
/// Replays a captured frame into a <see cref="ScanFrame"/>. The scanner never touches the machine.
/// </summary>
/// <typeparam name="TState">The machine-specific state stored on the captured frame.</typeparam>
/// <remarks>
/// The signature is the whole contract in this step. The Pocket2 scanner, and the contents
/// of <see cref="ScanFrame"/>, arrive later. <see cref="ScanFrame"/> is an empty placeholder
/// so this interface can compile.
/// </remarks>
public interface IVideoScanner<TState>
    where TState : class
{
    /// <summary>
    /// Replays <paramref name="frame"/> into <paramref name="destination"/>. Consumer side. No allocation.
    /// </summary>
    /// <param name="frame">The captured frame. It is the scanner's only input.</param>
    /// <param name="destination">The scan buffer to fill. Reused across scans.</param>
    void Scan(CapturedFrame<TState> frame, ScanFrame destination);
}