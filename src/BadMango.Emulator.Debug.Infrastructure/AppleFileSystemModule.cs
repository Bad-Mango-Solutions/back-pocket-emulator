// <copyright file="AppleFileSystemModule.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using Autofac;

using BadMango.Emulator.Storage.FileSystems;

using Serilog;

/// <summary>
/// Registers the read-only Apple II file-system probes and their detector.
/// </summary>
/// <remarks>
/// Probes are ordered by <see cref="AppleFileSystemDetector.RecommendedProbeOrder"/>:
/// ProDOS, then DOS 3.3, then Pascal. Commands resolve <see cref="IAppleFileSystemDetector"/>
/// from this module. They do not construct a detector per call.
/// </remarks>
public sealed class AppleFileSystemModule : Module
{
    /// <inheritdoc/>
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register(static ctx => new ProdosFileSystemProbe(Logger(ctx)))
            .As<IAppleFileSystemProbe>()
            .SingleInstance();
        builder.Register(static ctx => new Dos33FileSystemProbe(Logger(ctx)))
            .As<IAppleFileSystemProbe>()
            .SingleInstance();
        builder.Register(static ctx => new PascalFileSystemProbe(Logger(ctx)))
            .As<IAppleFileSystemProbe>()
            .SingleInstance();
        builder.Register(static ctx => new AppleFileSystemDetector(Logger(ctx), Order(ctx.Resolve<IEnumerable<IAppleFileSystemProbe>>())))
            .As<IAppleFileSystemDetector>()
            .SingleInstance();
    }

    private static ILogger Logger(IComponentContext context) =>
        context.ResolveOptional<ILogger>() ?? Serilog.Core.Logger.None;

    private static IReadOnlyList<IAppleFileSystemProbe> Order(IEnumerable<IAppleFileSystemProbe> probes)
    {
        var available = probes.ToList();
        var ordered = new List<IAppleFileSystemProbe>(available.Count);
        foreach (var kind in AppleFileSystemDetector.RecommendedProbeOrder())
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