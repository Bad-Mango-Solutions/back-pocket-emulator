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
/// Probes are ordered by <see cref="IGuestOsProbe.Order"/>: a smaller value runs first.
/// ProDOS 8 is 0 and DOS 3.3 is 100. A layered system uses a value below its base OS.
/// The same enabled module registers the DOS 3.3 and ProDOS 8 <see cref="IGuestOsBootProfile"/>
/// implementations. The boot monitor walks those profiles; it is attached to a machine
/// only when the profile sets <c>guestOs.bootLog</c> or the console runs <c>os on</c>.
/// The default constructor leaves detection off and registers no services, no traps,
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
        builder.RegisterType<ProDos8GuestOsBootProfile>()
            .As<IGuestOsBootProfile>()
            .SingleInstance();
        builder.RegisterType<Dos33GuestOsBootProfile>()
            .As<IGuestOsBootProfile>()
            .SingleInstance();
        builder.Register(static ctx => new GuestOsDetector(
                Logger(ctx),
                GuestOsDetector.OrderByPriority(ctx.Resolve<IEnumerable<IGuestOsProbe>>()),
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
}