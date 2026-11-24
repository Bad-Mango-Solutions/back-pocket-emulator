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
/// <see cref="OrderByPriority"/> asks the probe with the smaller <see cref="IGuestOsProbe.Order"/> first.
/// The first recognized probe wins. Later matches are logged and not selected.
/// </para>
/// <para>
/// <see cref="Detect"/> only reads through <see cref="IGuestMemoryReader"/>. It
/// registers no traps and does not run from the instruction loop.
/// </para>
/// </remarks>
public sealed class GuestOsDetector : IGuestOsDetector
{
    private static readonly GuestOsInfo NotDetected = new(
        GuestOsId.None,
        "None",
        string.Empty,
        "Guest OS detection has not run.",
        null);

    private static readonly GuestOsInfo NoSignature = new(
        GuestOsId.None,
        "None",
        string.Empty,
        "No DOS 3.3 or ProDOS 8 signature.",
        null);

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
        this.probes = OrderByPriority(probes);
        this.memory = memory;
    }

    /// <inheritdoc />
    public event EventHandler<GuestOsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public GuestOsInfo Current => this.current;

    /// <summary>
    /// Sorts probes so a smaller <see cref="IGuestOsProbe.Order"/> runs first.
    /// </summary>
    /// <param name="registered">The probes to sort. The sequence is copied.</param>
    /// <returns>The probes, lowest order first, then by <see cref="GuestOsId"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="registered"/> or an entry is <see langword="null"/>.</exception>
    public static IGuestOsProbe[] OrderByPriority(IEnumerable<IGuestOsProbe> registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        var copy = registered.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(copy[i]);
        }

        Array.Sort(copy, static (left, right) =>
        {
            var byPriority = left.Order.CompareTo(right.Order);
            return byPriority != 0 ? byPriority : left.Id.CompareTo(right.Id);
        });
        return copy;
    }

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

    private static void ValidateAttempt(IGuestOsProbe probe, GuestOsProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Id != probe.Id)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Id} returned an attempt for {attempt.Id}.");
        }

        if (string.IsNullOrWhiteSpace(attempt.Reason))
        {
            throw new InvalidOperationException($"Probe {probe.Id} returned an empty reason.");
        }

        if (attempt.Recognized && attempt.Info is null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Id} recognized a guest OS but returned no info.");
        }

        if (!attempt.Recognized && attempt.Info is not null)
        {
            throw new InvalidOperationException(
                $"Probe {probe.Id} rejected the image but returned info.");
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
                "Guest OS probe {GuestOsId} recognized {Recognized}: {Reason}",
                attempt.Id,
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
                "Guest OS probe {GuestOsId} also matched; keeping the earlier match.",
                attempt.Id);
        }

        return selected ?? NoSignature;
    }
}