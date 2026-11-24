// <copyright file="IGuestOsProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// One operating system <see cref="GuestOsDetector"/> asks to recognize in memory.
/// </summary>
/// <remarks>
/// <para>
/// A rejection is not an exception. A new operating system is a new class with its own
/// <see cref="Id"/>. Apple Pascal is a probe. A layered system such as GEOS is a probe
/// whose <see cref="Order"/> is lower than the base OS, so it is asked first.
/// </para>
/// <para>
/// Smaller <see cref="Order"/> runs first. ProDOS 8 uses 0 and DOS 3.3 uses 100, because
/// ProDOS reuses page 3.
/// </para>
/// </remarks>
public interface IGuestOsProbe
{
    /// <summary>Gets the operating system this probe recognizes.</summary>
    /// <value>The probe's id.</value>
    GuestOsId Id { get; }

    /// <summary>Gets the short name shown for this operating system.</summary>
    /// <value>A name such as "ProDOS 8".</value>
    string DisplayName { get; }

    /// <summary>Gets the priority. Smaller values run first.</summary>
    /// <value>ProDOS 8 is 0. DOS 3.3 is 100. A layered system uses a value below its base OS.</value>
    int Order { get; }

    /// <summary>
    /// Tries to recognize <see cref="Id"/> in <paramref name="memory"/>.
    /// </summary>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <returns>
    /// A recognized attempt, or a rejected attempt that explains why the bytes are not this operating system.
    /// </returns>
    /// <exception cref="ArgumentNullException">If <paramref name="memory"/> is <see langword="null"/>.</exception>
    GuestOsProbeAttempt Probe(IGuestMemoryReader memory);
}