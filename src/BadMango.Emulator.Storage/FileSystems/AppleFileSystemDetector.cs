// <copyright file="AppleFileSystemDetector.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Runs file-system probes and records why each format was accepted or rejected.
/// </summary>
/// <remarks>
/// <para>
/// The detector does not know DOS, ProDOS, or Pascal layouts. Format readers register
/// <see cref="IAppleFileSystemProbe"/> implementations. <see cref="RecommendedProbeOrder"/>
/// is ProDOS, then DOS 3.3, then Pascal, because the Pascal header is the loosest signature.
/// The first recognized probe wins. Every probe still runs, so the result names each format
/// that was tried, including a later match that was not selected.
/// </para>
/// <para>
/// Exceptions from <see cref="IAppleFileSystemProbe.TryOpen"/> propagate. This class has
/// no <c>catch</c>. A probe that cannot read the media must not convert that failure into
/// a rejection reason; a rejection is only for "these bytes are not this format".
/// </para>
/// </remarks>
public sealed class AppleFileSystemDetector : IAppleFileSystemDetector
{
    private readonly ILogger logger;
    private readonly IAppleFileSystemProbe[] probes;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleFileSystemDetector"/> class.
    /// </summary>
    /// <param name="logger">Logger for one line per probe. Not used for per-block reads.</param>
    /// <param name="probes">Probes in selection order. The sequence is copied.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/>, <paramref name="probes"/>, or an entry is <see langword="null"/>.</exception>
    public AppleFileSystemDetector(ILogger logger, IEnumerable<IAppleFileSystemProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(probes);
        this.logger = logger.ForContext<AppleFileSystemDetector>();
        this.probes = CopyProbes(probes);
    }

    /// <summary>
    /// Returns the order the DOS, ProDOS, and Pascal probes should be registered in.
    /// </summary>
    /// <returns>ProDOS, then DOS 3.3, then Pascal. A new list on every call.</returns>
    public static IReadOnlyList<AppleFileSystemKind> RecommendedProbeOrder() =>
        new[]
        {
            AppleFileSystemKind.Prodos,
            AppleFileSystemKind.Dos33,
            AppleFileSystemKind.Pascal,
        };

    /// <inheritdoc />
    public FileSystemDetectionResult Detect(FileSystemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var attempts = new List<FileSystemProbeAttempt>(this.probes.Length);
        IAppleFileSystem? selected = null;
        foreach (var probe in this.probes)
        {
            var attempt = probe.TryOpen(source);
            ValidateAttempt(probe, attempt);
            attempts.Add(attempt);
            this.logger.Debug(
                "File system probe {FileSystemKind} recognized {Recognized}: {Reason}",
                attempt.Kind,
                attempt.Recognized,
                attempt.Reason);

            if (!attempt.Recognized)
            {
                continue;
            }

            if (selected is null)
            {
                selected = attempt.FileSystem;
                continue;
            }

            this.logger.Warning(
                "File system probe {FileSystemKind} also matched; keeping the earlier match.",
                attempt.Kind);
        }

        if (selected is null)
        {
            this.logger.Information("No file system recognized after {ProbeCount} probes.", attempts.Count);
        }

        return new FileSystemDetectionResult(selected, attempts);
    }

    private static IAppleFileSystemProbe[] CopyProbes(IEnumerable<IAppleFileSystemProbe> probes)
    {
        var copy = probes.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(copy[i]);
        }

        return copy;
    }

    private static void ValidateAttempt(IAppleFileSystemProbe probe, FileSystemProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Kind != probe.Kind)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Kind} returned an attempt for {attempt.Kind}.");
        }

        if (string.IsNullOrWhiteSpace(attempt.Reason))
        {
            throw new InvalidOperationException($"Probe {probe.Kind} returned an empty reason.");
        }

        if (attempt.Recognized && attempt.FileSystem is null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Kind} recognized a volume but returned no file system.");
        }

        if (!attempt.Recognized && attempt.FileSystem is not null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Kind} rejected a volume but returned a file system.");
        }
    }
}