// <copyright file="GuestOsModule.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using Autofac;

using BadMango.Emulator.Core.Interfaces.Cpu;
using BadMango.Emulator.Devices.GuestOs;

using Serilog;

/// <summary>
/// Registers the guest-OS probes and their detector, or registers nothing when detection is off.
/// </summary>
/// <remarks>
/// <para>
/// Probes are ordered by <see cref="GuestOsDetector.RecommendedProbeOrder"/>: ProDOS 8, then
/// DOS 3.3. The default constructor leaves detection off and registers no services, no traps,
/// and no instruction-loop callback. A host that wants detection loads
/// <c>new GuestOsModule(enabled: true)</c> and supplies <see cref="IGuestMemoryReader"/>,
/// or <see cref="ICpu"/>, which this module adapts with <see cref="CpuGuestMemoryReader"/>.
/// </para>
/// <para>
/// <see cref="CpuGuestMemoryReader.Read"/> uses <see cref="ICpu.Peek8"/> only. The detector
/// runs when <see cref="IGuestOsDetector.Detect"/> is called. It is not part of
/// <c>Cpu65C02.Step</c>.
/// </para>
/// </remarks>
public sealed class GuestOsModule : Module
{
    private readonly bool enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsModule"/> class with detection off.
    /// </summary>
    public GuestOsModule()
        : this(enabled: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GuestOsModule"/> class.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> to register the probes and detector.</param>
    public GuestOsModule(bool enabled)
    {
        this.enabled = enabled;
    }

    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        if (!this.enabled)
        {
            return;
        }

        builder.RegisterType<ProDos8GuestOsProbe>()
            .As<IGuestOsProbe>()
            .SingleInstance();
        builder.RegisterType<Dos33GuestOsProbe>()
            .As<IGuestOsProbe>()
            .SingleInstance();
        builder.Register(static ctx => new GuestOsDetector(
                Logger(ctx),
                Order(ctx.Resolve<IEnumerable<IGuestOsProbe>>()),
                ctx.Resolve<IGuestMemoryReader>()))
            .As<IGuestOsDetector>()
            .SingleInstance();
        builder.Register(static ctx => new CpuGuestMemoryReader(ctx.Resolve<ICpu>()))
            .As<IGuestMemoryReader>()
            .SingleInstance()
            .IfNotRegistered(typeof(IGuestMemoryReader));
    }

    private static ILogger Logger(IComponentContext context) =>
        context.ResolveOptional<ILogger>() ?? Serilog.Core.Logger.None;

    private static IReadOnlyList<IGuestOsProbe> Order(IEnumerable<IGuestOsProbe> probes)
    {
        var available = probes.ToList();
        var ordered = new List<IGuestOsProbe>(available.Count);
        foreach (var kind in GuestOsDetector.RecommendedProbeOrder())
        {
            foreach (var probe in available)
            {
                if (probe.Kind == kind && !ordered.Contains(probe))
                {
                    ordered.Add(probe);
                }
            }
        }

        foreach (var probe in available)
        {
            if (!ordered.Contains(probe))
            {
                ordered.Add(probe);
            }
        }

        return ordered;
    }
}