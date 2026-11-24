// <copyright file="ProDos8GuestOsProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Recognizes ProDOS 8 through 2.4.3 from the global page.
/// </summary>
/// <remarks>
/// The checks live in <see cref="ProDosGlobalPage.Inspect"/>. A matching page with an
/// unknown <c>KVERSION</c> is reported as <see cref="GuestOsKind.Unknown"/> so a later
/// DOS probe cannot claim the machine. The probe only reads memory.
/// </remarks>
public sealed class ProDos8GuestOsProbe : IGuestOsProbe
{
    /// <inheritdoc />
    public GuestOsKind Kind => GuestOsKind.ProDos8;

    /// <inheritdoc />
    public GuestOsProbeAttempt Probe(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var page = ProDosGlobalPage.Inspect(memory);
        if (page.StructuralRejection is string rejection)
        {
            return GuestOsProbeAttempt.Reject(this.Kind, rejection);
        }

        if (page.VersionLabel is string label)
        {
            return GuestOsProbeAttempt.Match(
                this.Kind,
                "MLI entry, DATETIME vector, and KVERSION match ProDOS 8.",
                new GuestOsInfo(
                    GuestOsKind.ProDos8,
                    label,
                    page.KVersion,
                    page.IVersion,
                    page.MachId,
                    page.HasClock,
                    "MLI entry, DATETIME vector, and KVERSION match ProDOS 8."));
        }

        var reason = $"KVERSION ${page.KVersion:X2} is not a known ProDOS 8 version.";
        return GuestOsProbeAttempt.Match(
            this.Kind,
            reason,
            new GuestOsInfo(
                GuestOsKind.Unknown,
                "Unknown",
                page.KVersion,
                page.IVersion,
                page.MachId,
                page.HasClock,
                reason));
    }
}