// <copyright file="IGuestOsProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One operating system <see cref="GuestOsDetector"/> asks to recognize in memory.
/// </summary>
/// <remarks>
/// A rejection is not an exception. Register probes in
/// <see cref="GuestOsDetector.RecommendedProbeOrder"/>: ProDOS 8, then DOS 3.3,
/// because ProDOS reuses page 3.
/// </remarks>
public interface IGuestOsProbe
{
    /// <summary>Gets the operating system this probe recognizes.</summary>
    /// <value>The probe's kind.</value>
    GuestOsKind Kind { get; }

    /// <summary>
    /// Tries to recognize <see cref="Kind"/> in <paramref name="memory"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <returns>
    /// A recognized attempt, or a rejected attempt that explains why the bytes are not this operating system.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    GuestOsProbeAttempt Probe(IGuestMemoryReader memory);
}