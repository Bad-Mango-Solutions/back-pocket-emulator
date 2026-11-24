// <copyright file="GuestOsDetector.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices.GuestOs;

using Serilog;

/// <summary>
/// Runs guest-OS probes in order and remembers the last result.
/// </summary>
/// <remarks>
/// <para>
/// The detector does not know the ProDOS global page or the DOS vector table.
/// <see cref="RecommendedProbeOrder"/> is ProDOS 8, then DOS 3.3. The first
/// recognized probe wins. Later matches are logged and not selected.
/// </para>
/// <para>
/// <see cref="Detect"/> only reads through <see cref="IGuestMemoryReader"/>. It
/// registers no traps and does not run from the instruction loop.
/// </para>
/// </remarks>
public sealed class GuestOsDetector : IGuestOsDetector
{
    private static readonly GuestOsInfo NotDetected = new(
        GuestOsKind.None,
        string.Empty,
        null,
        null,
        null,
        false,
        "Guest OS detection has not run.");

    private static readonly GuestOsInfo NoSignature = new(
        GuestOsKind.None,
        string.Empty,
        null,
        null,
        null,
        false,
        "No DOS 3.3 or ProDOS 8 signature.");

    private readonly ILogger logger;
    private readonly IGuestOsProbe[] probes;
    private readonly IGuestMemoryReader memory;
    private GuestOsInfo current = NotDetected;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsDetector"/> class.
    /// </summary>
    /// <param name="logger">Logger for one line per probe. Not used on the instruction loop.</param>
    /// <param name="probes">Probes in selection order. The sequence is copied.</param>
    /// <param name="memory">Side-effect-free guest memory.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="logger"/>, <paramref name="probes"/>, <paramref name="memory"/>, or a probe is <see langword="null"/>.</exception>
    public GuestOsDetector(ILogger logger, IEnumerable<IGuestOsProbe> probes, IGuestMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(memory);
        this.logger = logger.ForContext<GuestOsDetector>();
        this.probes = CopyProbes(probes);
        this.memory = memory;
    }

    /// <inheritdoc />
    public event EventHandler<GuestOsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public GuestOsInfo Current => this.current;

    /// <summary>
    /// Returns the order the ProDOS 8 and DOS 3.3 probes should be registered in.
    /// </summary>
    /// <returns>ProDOS 8, then DOS 3.3. A new list on every call.</returns>
    public static IReadOnlyList<GuestOsKind> RecommendedProbeOrder() =>
        new[]
        {
            GuestOsKind.ProDos8,
            GuestOsKind.Dos33,
        };

    /// <inheritdoc />
    public GuestOsInfo Detect()
    {
        var detected = this.RunProbes();
        if (detected == this.current)
        {
            return this.current;
        }

        var previous = this.current;
        this.current = detected;
        this.Changed?.Invoke(this, new GuestOsChangedEventArgs(previous, detected));
        return this.current;
    }

    private static IGuestOsProbe[] CopyProbes(IEnumerable<IGuestOsProbe> registered)
    {
        var copy = registered.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(copy[i]);
        }

        return copy;
    }

    private static void ValidateAttempt(IGuestOsProbe probe, GuestOsProbeAttempt attempt)
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

        if (attempt.Recognized && attempt.Info is null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Kind} recognized a guest OS but returned no info.");
        }

        if (!attempt.Recognized && attempt.Info is not null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Kind} rejected the image but returned info.");
        }
    }

    private GuestOsInfo RunProbes()
    {
        GuestOsInfo? selected = null;
        foreach (var probe in this.probes)
        {
            var attempt = probe.Probe(this.memory);
            ValidateAttempt(probe, attempt);
            this.logger.Debug(
                "Guest OS probe {GuestOsKind} recognized {Recognized}: {Reason}",
                attempt.Kind,
                attempt.Recognized,
                attempt.Reason);

            if (!attempt.Recognized)
            {
                continue;
            }

            if (selected is null)
            {
                selected = attempt.Info;
                continue;
            }

            this.logger.Debug(
                "Guest OS probe {GuestOsKind} also matched; keeping the earlier match.",
                attempt.Kind);
        }

        return selected ?? NoSignature;
    }
}