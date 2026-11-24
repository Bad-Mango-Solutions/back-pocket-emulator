// <copyright file="Dos33GuestOsProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

/// <summary>
/// Recognizes DOS 3.3 from the page-3 vector table.
/// </summary>
/// <remarks>
/// <para>
/// All five of <c>$03D0</c>, <c>$03D3</c>, <c>$03D6</c>, <c>$03D9</c>, and <c>$03EA</c>
/// must be <c>JMP</c>. The standard 48K targets are labeled "DOS 3.3". The same
/// <c>JMP</c>s with other targets are labeled "DOS 3.3-compatible (nonstandard vectors)".
/// </para>
/// <para>
/// The probe rejects the image when <see cref="ProDosGlobalPage.Inspect"/> says the
/// ProDOS global page is present, including a page whose <c>KVERSION</c> is unknown.
/// ProDOS reuses page 3, so those <c>JMP</c>s are not DOS.
/// </para>
/// </remarks>
public sealed class Dos33GuestOsProbe : IGuestOsProbe
{
    /// <inheritdoc />
    public GuestOsKind Kind => GuestOsKind.Dos33;

    /// <inheritdoc />
    public GuestOsProbeAttempt Probe(IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        if (ProDosGlobalPage.Inspect(memory).StructuralRejection is null)
        {
            return GuestOsProbeAttempt.Reject(this.Kind, "ProDOS global page is present.");
        }

        var vectors = Dos33VectorPage.Inspect(memory);
        if (!vectors.AllJumps)
        {
            return GuestOsProbeAttempt.Reject(this.Kind, Dos33VectorPage.MissingJmpReason(vectors.MissingJmp));
        }

        if (!vectors.StandardTargets)
        {
            return Match(Dos33VectorPage.NonstandardLabel, "Page 3 JMPs do not use the 48K DOS 3.3 targets.");
        }

        return Match(Dos33VectorPage.StandardLabel, "Page 3 vectors match 48K DOS 3.3.");
    }

    private GuestOsProbeAttempt Match(string versionLabel, string reason) =>
        GuestOsProbeAttempt.Match(
            this.Kind,
            reason,
            new GuestOsInfo(GuestOsKind.Dos33, versionLabel, null, null, null, false, reason));
}