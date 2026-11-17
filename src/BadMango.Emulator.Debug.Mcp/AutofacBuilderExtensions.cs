// <copyright file="AutofacBuilderExtensions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Registers Autofac as the generic host service provider.
/// </summary>
internal static class AutofacBuilderExtensions
{
    private const string ActionsKey = "BadMango.Emulator.Debug.Mcp.AutofacActions";

    /// <summary>
    /// Adds an Autofac configuration action to the host builder.
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type.</typeparam>
    /// <param name="builder">The host builder.</param>
    /// <param name="configure">The container configuration.</param>
    /// <returns>The same builder.</returns>
    public static TBuilder ConfigureAutofac<TBuilder>(this TBuilder builder, Action<ContainerBuilder> configure)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        if (!builder.Properties.TryGetValue(ActionsKey, out object? value))
        {
            var actions = new List<Action<ContainerBuilder>>();
            builder.Properties[ActionsKey] = actions;
            builder.ConfigureContainer(new AutofacServiceProviderFactory(), (ContainerBuilder container) =>
            {
                foreach (Action<ContainerBuilder> action in actions)
                {
                    action(container);
                }
            });
            value = actions;
        }

        ((List<Action<ContainerBuilder>>)value).Add(configure);
        return builder;
    }
}