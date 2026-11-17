// <copyright file="Program.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

using Autofac;
using Autofac.Extensions.DependencyInjection;

using BadMango.Emulator.Debug;
using BadMango.Emulator.Debug.Infrastructure;
using BadMango.Emulator.Debug.Infrastructure.Commands;
using BadMango.Emulator.Devices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;

// Configure Serilog
// For verbose device-level logging, add to the chain below:
// .MinimumLevel.Override("BadMango.Emulator.Devices", LogEventLevel.Verbose)
var debugLogEventStream = new DebugLogEventStream();

var options = EmudbgOptions.Parse(args);
bool dumpAssemblies = DebugHostArguments.Has(args, DebugHostArguments.DumpAssembliesOption);

if (options.AgentMode)
{
    options = options with { Headless = true, JsonOutput = true, NoBanner = true };
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        restrictedToMinimumLevel: LogEventLevel.Warning)
    .WriteTo.File(
        "logs/emudbg-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Sink(
        new DebugLogWindowSink(debugLogEventStream),
        restrictedToMinimumLevel: LogEventLevel.Warning)
    .CreateLogger();

// Give devices built by the device factory registry (speaker, Disk II, ...) a logger scoped
// to their type, so device diagnostics such as audio-backend failures reach the log.
DeviceFactoryRegistry.LoggerFactory = deviceType => Log.Logger.ForContext(deviceType);

try
{
    if (options.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    if (!string.IsNullOrWhiteSpace(options.ScriptFile) && !File.Exists(options.ScriptFile))
    {
        Console.WriteLine($"Error: Script file not found: {options.ScriptFile}");
        return 1;
    }

    bool nonInteractive = options.IsNonInteractive || Console.IsInputRedirected;
    if (nonInteractive)
    {
        options = options with { Headless = true, NoBanner = true };
    }

    Log.Information(options.Headless
        ? "Starting Emulator Debug Console (headless)"
        : "Starting Emulator Debug Console");

    string[] hostArgs = DebugHostArguments.WithoutHostOptions(args);

    // Build the host. The headless branch never calls InteractiveUiRegistration.Register,
    // and that method is not inlined, so Avalonia and the debug UI assembly stay unloaded.
    var host = Host.CreateDefaultBuilder(hostArgs)
        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .ConfigureContainer<ContainerBuilder>(builder =>
        {
            builder.RegisterInstance(options).AsSelf().SingleInstance();
            builder.RegisterModule<DebugConsoleModule>();
            if (!options.Headless)
            {
                InteractiveUiRegistration.Register(builder);
            }

            builder.RegisterInstance(debugLogEventStream).As<IDebugLogEventStream>().SingleInstance();
            builder.RegisterInstance(Log.Logger).As<ILogger>().SingleInstance();
        })
        .UseSerilog(Log.Logger)
        .Build();

    // Run the REPL
    using var scope = host.Services.CreateScope();
    var repl = scope.ServiceProvider.GetRequiredService<DebugRepl>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger>();

    repl.ShowBanner = !options.NoBanner;
    if (nonInteractive || options.JsonOutput)
    {
        repl.ShowBanner = false;
        repl.ShowPrompt = false;
        repl.PrintExitMessage = false;
        repl.IsInteractive = false;
    }

    logger.Information("Debug console initialized");

    CommandResult lastResult;
    if (!string.IsNullOrWhiteSpace(options.ExecCommands))
    {
        lastResult = repl.ExecuteBatch(options.GetExecCommandLines());
    }
    else if (!string.IsNullOrWhiteSpace(options.ScriptFile))
    {
        string[] commandLines = File.ReadAllLines(options.ScriptFile)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .ToArray();

        lastResult = repl.ExecuteBatch(commandLines);
    }
    else
    {
        repl.Run();
        lastResult = repl.LastCommandResult;
    }

    logger.Information("Debug console exited normally");
    return lastResult.Success ? 0 : 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Debug console terminated unexpectedly");
    Console.WriteLine($"Fatal error: {ex.Message}");
    return 1;
}
finally
{
    if (dumpAssemblies)
    {
        DebugHostArguments.WriteLoadedAssemblies();
    }

    await Log.CloseAndFlushAsync();
}

static void PrintUsage()
{
    Console.WriteLine("""
        emudbg - Emulator Debug Console

        Usage:
          emudbg [options]

        Options:
          -p, --profile <name>      Load the specified machine profile on startup
          -e, --exec <commands>     Execute commands (semicolon/newline separated) and exit
          -f, --file <path>         Execute commands from a script file and exit
          --no-banner               Suppress the startup banner
          --json, -j                Enable JSON output mode
          --agent                   Agent mode (headless + json + non-interactive)
          --headless                Disable interactive UI loading
          --max-cycles <n>          Optional script cycle budget hint
          --seed <n>                Optional deterministic seed for scripts
          -h, --help                Show this help and exit

        Examples:
          emudbg --profile pocket2e-lite
          emudbg --exec "boot;regs;step 20;regs;exit"
          emudbg --file script.emudbg --no-banner
          emudbg --agent --exec "switches;regions;exit"
        """);
}