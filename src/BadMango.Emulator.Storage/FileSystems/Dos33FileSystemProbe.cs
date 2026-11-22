// <copyright file="Dos33FileSystemProbe.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Recognizes a DOS 3.3 VTOC and opens a <see cref="Dos33FileSystem"/>.
/// </summary>
/// <remarks>
/// Register this probe in <see cref="AppleFileSystemDetector.RecommendedProbeOrder"/>.
/// The probe calls <see cref="Dos33Layout.IsVtoc"/> and returns
/// <see cref="FileSystemProbeAttempt.Reject"/> when the check is false. It does not catch.
/// A missing sector reader is a rejection. A sector read that throws propagates.
/// The constructor's <see cref="ArgumentException"/> is only a guard for a caller that
/// skips this check.
/// </remarks>
public sealed class Dos33FileSystemProbe : IAppleFileSystemProbe
{
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Dos33FileSystemProbe"/> class.
    /// </summary>
    /// <param name="logger">Logger for the recognized or rejected volume. Not <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/> is <see langword="null"/>.</exception>
    public Dos33FileSystemProbe(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger.ForContext<Dos33FileSystemProbe>();
    }

    /// <summary>Gets the DOS 3.3 format.</summary>
    /// <value><see cref="AppleFileSystemKind.Dos33"/>.</value>
    public AppleFileSystemKind Kind => AppleFileSystemKind.Dos33;

    /// <summary>
    /// Tries to open a DOS 3.3 volume.
    /// </summary>
    /// <param name="source">The sector view used to read track 17. Not <see langword="null"/>.</param>
    /// <returns>A recognized DOS 3.3 volume, or a rejection that names the missing signature.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">If the DOS reader rejects a VTOC that this probe already accepted.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If the sector reader rejects the VTOC read.</exception>
    /// <exception cref="InvalidOperationException">If the sector reader refuses the VTOC read.</exception>
    /// <exception cref="IOException">If track 17 sector 0 cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">If the sector reader has been disposed.</exception>
    public FileSystemProbeAttempt TryOpen(FileSystemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Sectors is not ISectorReader sectors)
        {
            this.logger.Debug("DOS 3.3 probe rejected the image because it has no sector reader.");
            return FileSystemProbeAttempt.Reject(this.Kind, "DOS 3.3 needs a sector reader.");
        }

        if (sectors.BytesPerSector != Dos33Layout.SectorSize
            || sectors.SectorsPerTrack != Dos33Layout.DefaultSectorsPerTrack)
        {
            this.logger.Debug("DOS 3.3 probe rejected a sector geometry that is not 16 by 256.");
            return FileSystemProbeAttempt.Reject(this.Kind, "DOS 3.3 needs 16 sectors of 256 bytes.");
        }

        if (sectors.TrackCount <= Dos33Layout.VtocTrack)
        {
            this.logger.Debug("DOS 3.3 probe rejected an image that has no track 17.");
            return FileSystemProbeAttempt.Reject(this.Kind, "The image has no track 17.");
        }

        var sector = new byte[Dos33Layout.SectorSize];
        sectors.ReadSector(Dos33Layout.VtocTrack, Dos33Layout.VtocSector, sector);
        if (!Dos33Layout.IsVtoc(sector, sectors.TrackCount, sectors.SectorsPerTrack))
        {
            this.logger.Debug("DOS 3.3 probe rejected track 17 sector 0.");
            return FileSystemProbeAttempt.Reject(this.Kind, "Track 17 sector 0 is not a DOS 3.3 VTOC.");
        }

        var volume = new Dos33FileSystem(sectors, this.logger);
        this.logger.Debug("DOS 3.3 probe recognized volume {VolumeName}.", volume.VolumeName);
        return FileSystemProbeAttempt.Match(this.Kind, "DOS 3.3 VTOC version is 3.", volume);
    }
}