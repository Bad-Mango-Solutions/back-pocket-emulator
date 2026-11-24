// <copyright file="ProDos8GuestOsProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Recognizes ProDOS 8 through 2.4.3 from the global page.
/// </summary>
/// <remarks>
/// The checks live in <see cref="ProDosGlobalPage.Inspect"/>. A matching page with an
/// unknown <c>KVERSION</c> is reported as <see cref="GuestOsId.Unknown"/> so a later
/// DOS probe cannot claim the machine. The page bytes are still <see cref="ProDos8Details"/>.
/// The probe only reads memory.
/// </remarks>
public sealed class ProDos8GuestOsProbe : IGuestOsProbe
{
    /// <inheritdoc />
    public GuestOsId Id => GuestOsId.ProDos8;

    /// <inheritdoc />
    public string DisplayName => "ProDOS 8";

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public GuestOsProbeAttempt Probe(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var page = ProDosGlobalPage.Inspect(memory);
        if (page.StructuralRejection is string rejection)
        {
            return GuestOsProbeAttempt.Reject(this.Id, rejection);
        }

        var details = new ProDos8Details(page.KVersion, page.IVersion, page.MachId, page.HasClock);
        if (page.VersionLabel is string label)
        {
            const string reason = "MLI entry, DATETIME vector, and KVERSION match ProDOS 8.";
            return GuestOsProbeAttempt.Match(
                this.Id,
                reason,
                new GuestOsInfo(GuestOsId.ProDos8, this.DisplayName, label, reason, details));
        }

        var unknownReason = $"KVERSION ${page.KVersion:X2} is not a known ProDOS 8 version.";
        return GuestOsProbeAttempt.Match(
            this.Id,
            unknownReason,
            new GuestOsInfo(GuestOsId.Unknown, "Unknown", "Unknown", unknownReason, details));
    }
}