// <copyright file="DebugMcpHost.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using Autofac;

using BadMango.Emulator.Debug.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Events;

/// <summary>
/// Builds the headless generic host for the debug MCP server.
/// </summary>
public static class DebugMcpHost
{
    /// <summary>
    /// Creates a host builder rooted at the application directory.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The configured builder.</returns>
    public static HostApplicationBuilder Create(string[] args)
    {
        var settings = new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        };

        return Configure(Host.CreateApplicationBuilder(settings));
    }

    /// <summary>
    /// Configures logging, sandbox options, and the headless debug composition.
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type.</typeparam>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// Serilog writes only to stderr and a rolling file. The Microsoft console logger is removed
    /// so stdout stays reserved for the MCP protocol. Avalonia and the debug UI are not registered.
    /// </remarks>
    public static TBuilder Configure<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        Serilog.ILogger logger = CreateLogger();
        Log.Logger = logger;
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(logger, dispose: false);

        builder.Services.AddSingleton<IValidateOptions<DebugMcpOptions>, DebugMcpOptionsValidator>();
        builder.Services.AddOptions<DebugMcpOptions>()
            .Bind(builder.Configuration.GetSection(DebugMcpOptions.SectionName))
            .ValidateOnStart();

        builder.ConfigureAutofac(container =>
        {
            container.RegisterInstance(logger).As<Serilog.ILogger>().SingleInstance();
            container.RegisterInstance(new EmudbgOptions(
                Headless: true,
                JsonOutput: true,
                AgentMode: true,
                NoBanner: true,
                MaxCycles: DebugMcpBudgets.DefaultMaxCycles)).AsSelf().SingleInstance();
            container.RegisterType<SandboxPathGuard>().AsSelf().SingleInstance();
            container.RegisterType<DebugMcpRunTracker>().AsSelf().SingleInstance();
            container.RegisterType<DebugMcpProfileLoader>().AsSelf().SingleInstance();
            container.RegisterModule<DebugConsoleModule>();
        });

        logger.Information("Debug MCP host starting.");
        return builder;
    }

    private static Serilog.ILogger CreateLogger()
    {
        string logDirectory = Path.Join(AppContext.BaseDirectory, "logs");
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.File(
                Path.Join(logDirectory, "debug-mcp-.log"),
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}