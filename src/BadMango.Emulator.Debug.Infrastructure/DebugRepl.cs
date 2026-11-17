// <copyright file="DebugRepl.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Debug.Infrastructure.Commands;

/// <summary>
/// Read-Eval-Print Loop (REPL) for the debug console.
/// </summary>
/// <remarks>
/// Provides an interactive command-line interface for debugging
/// the emulator. The REPL reads commands from the user, dispatches
/// them to registered handlers, and displays the results.
/// </remarks>
public sealed class DebugRepl
{
    private readonly ICommandDispatcher dispatcher;
    private readonly ICommandContext context;
    private readonly TextReader input;
    private readonly string prompt;
    private readonly IDebugContext debugContext;
    private readonly EmudbgOptions? options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugRepl"/> class.
    /// </summary>
    /// <param name="dispatcher">The command dispatcher.</param>
    /// <param name="context">The command context.</param>
    /// <param name="input">The input reader.</param>
    /// <param name="prompt">The prompt string to display.</param>
    /// <param name="options">Startup options used to decide whether the session is interactive.</param>
    public DebugRepl(ICommandDispatcher dispatcher, ICommandContext context, TextReader input, string prompt = "> ", EmudbgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(prompt);

        this.dispatcher = dispatcher;
        this.context = context;
        this.input = input;
        this.prompt = prompt;
        this.options = options;
        this.debugContext = context as IDebugContext ?? throw new ArgumentException("Context must implement IDebugContext.", nameof(context));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the REPL should show the banner on start.
    /// </summary>
    public bool ShowBanner { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the REPL should display the prompt.
    /// </summary>
    public bool ShowPrompt { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether exit messages should be printed.
    /// </summary>
    public bool PrintExitMessage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this REPL should be treated as interactive.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="null"/>, <see cref="Run"/> derives it. Startup options that request
    /// a batch or JSON session are non-interactive. Otherwise a console input reader follows
    /// <see cref="Console.IsInputRedirected"/>, and any other reader (a test double or a script reader)
    /// stays interactive so the caller that supplied it still sees prompts unless it sets this property.
    /// </remarks>
    public bool? IsInteractive { get; set; }

    /// <summary>
    /// Gets the most recent command result processed by this REPL.
    /// </summary>
    public CommandResult LastCommandResult { get; private set; } = CommandResult.Ok();

    /// <summary>
    /// Creates a REPL with standard console I/O and default built-in commands.
    /// </summary>
    /// <param name="windowManager">
    /// Optional debug window manager for opening popup windows from commands.
    /// If null, commands that support popups will fall back to console output.
    /// </param>
    /// <returns>A new <see cref="DebugRepl"/> configured for console use.</returns>
    public static DebugRepl CreateConsoleRepl(IDebugWindowManager? windowManager = null)
    {
        var dispatcher = new CommandDispatcher();

        // Register built-in commands
        dispatcher.Register(new HelpCommand());
        dispatcher.Register(new ExitCommand());
        dispatcher.Register(new VersionCommand());
        dispatcher.Register(new ClearCommand());
        dispatcher.Register(new AboutCommand(windowManager));

        var context = DebugContext.CreateConsoleContext(dispatcher);

        return new(dispatcher, context, Console.In);
    }

    /// <summary>
    /// Runs the REPL until the user exits or input is exhausted.
    /// </summary>
    public void Run()
    {
        bool isInteractiveInput = this.ResolveInteractive();
        bool effectiveShowBanner = this.ShowBanner && isInteractiveInput;
        bool effectiveShowPrompt = this.ShowPrompt && isInteractiveInput;
        bool effectivePrintExit = this.PrintExitMessage && isInteractiveInput;

        if (effectiveShowBanner)
        {
            this.DisplayBanner();
        }

        while (true)
        {
            if (effectiveShowPrompt)
            {
                this.context.Output.Write(this.prompt);
            }

            var line = this.input.ReadLine();
            if (line is null)
            {
                // End of input
                break;
            }

            var result = this.ProcessLine(line);
            if (result.ShouldExit)
            {
                if (!string.IsNullOrEmpty(result.Message) && effectivePrintExit)
                {
                    this.context.Output.WriteLine(result.Message);
                }

                break;
            }
        }
    }

    /// <summary>
    /// Executes a sequence of command lines.
    /// </summary>
    /// <param name="commands">The command lines to execute.</param>
    /// <returns>The final command result.</returns>
    public CommandResult ExecuteBatch(IEnumerable<string> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        CommandResult last = CommandResult.Ok();
        foreach (string raw in commands)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            last = this.ProcessLine(raw);
            if (last.ShouldExit)
            {
                break;
            }
        }

        this.LastCommandResult = last;
        return last;
    }

    /// <summary>
    /// Processes a single command line.
    /// </summary>
    /// <param name="line">The command line to process.</param>
    /// <returns>The result of processing the command.</returns>
    public CommandResult ProcessLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return CommandResult.Ok();
        }

        var result = this.dispatcher.Dispatch(this.context, trimmed);
        this.LastCommandResult = result;

        if (!string.IsNullOrEmpty(result.Message) && !result.ShouldExit)
        {
            if (result.Success)
            {
                this.context.Output.WriteLine(result.Message);
            }
            else
            {
                this.context.Error.WriteLine($"Error: {result.Message}");
            }
        }

        return result;
    }

    private bool ResolveInteractive()
    {
        if (this.IsInteractive is bool setByCaller)
        {
            return setByCaller;
        }

        if (this.options is { IsNonInteractive: true } or { JsonOutput: true })
        {
            return false;
        }

        if (!ReferenceEquals(this.input, Console.In))
        {
            return true;
        }

        return !Console.IsInputRedirected;
    }

    private void DisplayBanner()
    {
        var machineDescription = this.debugContext.MachineInfo?.Summary ?? "No machine attached";

        this.context.Output.WriteLine("═══════════════════════════════════════════════════════════════════════");
        this.context.Output.WriteLine("  Emulator Debug Console");
        this.context.Output.WriteLine($"  Machine:  {machineDescription}");
        this.context.Output.WriteLine("  Type 'help' for a list of commands, 'exit' to quit");
        this.context.Output.WriteLine("═══════════════════════════════════════════════════════════════════════");
        this.context.Output.WriteLine();
    }
}