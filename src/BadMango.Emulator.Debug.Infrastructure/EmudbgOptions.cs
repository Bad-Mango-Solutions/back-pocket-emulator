// <copyright file="EmudbgOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using System.Globalization;

/// <summary>
/// Parsed startup options for emudbg.
/// </summary>
/// <param name="Profile">Optional profile name to load at startup.</param>
/// <param name="ExecCommands">Optional commands to execute in batch mode.</param>
/// <param name="ScriptFile">Optional path to a command script file.</param>
/// <param name="NoBanner">When true, suppresses the startup banner.</param>
/// <param name="ShowHelp">When true, prints usage and exits.</param>
/// <param name="JsonOutput">When true, enables JSON output mode.</param>
/// <param name="Headless">When true, starts without loading the interactive UI.</param>
/// <param name="AgentMode">When true, implies headless + JSON + non-interactive behavior.</param>
/// <param name="MaxCycles">Optional maximum cycle budget used by run, run-until, and step.</param>
/// <param name="Seed">Reserved for a future seeded RNG. Parsing rejects <c>--seed</c> today.</param>
/// <param name="ExportRoot">Directory <c>disk read --to</c> may write into, or <see langword="null"/>.</param>
/// <remarks>
/// Names are matched by <see cref="EmudbgOptionTable"/> using case-insensitive comparison.
/// </remarks>
public sealed record EmudbgOptions(
    string? Profile = null,
    string? ExecCommands = null,
    string? ScriptFile = null,
    bool NoBanner = false,
    bool ShowHelp = false,
    bool JsonOutput = false,
    bool Headless = false,
    bool AgentMode = false,
    ulong? MaxCycles = null,
    int? Seed = null,
    string? ExportRoot = null)
{
    /// <summary>
    /// Gets a value indicating whether the options represent non-interactive execution.
    /// </summary>
    public bool IsNonInteractive =>
        this.AgentMode ||
        !string.IsNullOrWhiteSpace(this.ExecCommands) ||
        !string.IsNullOrWhiteSpace(this.ScriptFile);

    /// <summary>
    /// Parses process arguments.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="EmudbgUsageException">The arguments are not valid usage.</exception>
    public static EmudbgOptions Parse(string[] args)
    {
        if (!TryParse(args, out EmudbgOptions? options, out string? error))
        {
            throw new EmudbgUsageException(error ?? "Invalid arguments.");
        }

        return options!;
    }

    /// <summary>
    /// Parses process arguments.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="options">The parsed options when the method returns <see langword="true"/>.</param>
    /// <param name="error">The usage error when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="args"/> is valid usage.</returns>
    public static bool TryParse(string[] args, out EmudbgOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        options = null;
        error = null;

        string? profile = null;
        string? exec = null;
        string? file = null;
        bool noBanner = false;
        bool showHelp = false;
        bool jsonOutput = false;
        bool headless = false;
        bool agentMode = false;
        ulong? maxCycles = null;
        string? exportRoot = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (EmudbgOptionTable.TryMatchValueOption(arg, out string name, out string? inline))
            {
                if (!TryTakeValue(args, ref i, name, inline, out string? value, out error))
                {
                    return false;
                }

                if (IsName(name, "--profile"))
                {
                    profile = value;
                }
                else if (IsName(name, "--exec"))
                {
                    exec = value;
                }
                else if (IsName(name, "--file"))
                {
                    file = value;
                }
                else if (IsName(name, "--max-cycles"))
                {
                    if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed) || value!.StartsWith('-'))
                    {
                        error = $"error: invalid --max-cycles value '{value}'. Expected a non-negative integer.";
                        return false;
                    }

                    maxCycles = parsed;
                }
                else if (IsName(name, "--export-root"))
                {
                    exportRoot = value;
                }
                else if (IsName(name, "--seed"))
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        error = $"error: invalid --seed value '{value}'. Expected an integer.";
                        return false;
                    }

                    error = "error: --seed cannot be applied because the machine has no seeded random-number source.";
                    return false;
                }

                continue;
            }

            if (EmudbgOptionTable.IsFlag(arg))
            {
                if (IsName(arg, "--no-banner"))
                {
                    noBanner = true;
                }
                else if (IsName(arg, "--json") || IsName(arg, "-j"))
                {
                    jsonOutput = true;
                }
                else if (IsName(arg, "--headless"))
                {
                    headless = true;
                }
                else if (IsName(arg, "--agent") || IsName(arg, "--agent-mode"))
                {
                    agentMode = true;
                    headless = true;
                    jsonOutput = true;
                    noBanner = true;
                }
                else if (IsName(arg, "--help") || IsName(arg, "-h") || IsName(arg, "-?") || IsName(arg, "/?"))
                {
                    showHelp = true;
                }

                continue;
            }

            if (arg.StartsWith('-'))
            {
                error = $"error: unknown option '{arg}'.";
                return false;
            }
        }

        options = new EmudbgOptions(profile, exec, file, noBanner, showHelp, jsonOutput, headless, agentMode, maxCycles, null, exportRoot);
        return true;
    }

    /// <summary>
    /// Expands <see cref="ExecCommands"/> into command lines.
    /// </summary>
    /// <returns>The command lines to execute.</returns>
    public IReadOnlyList<string> GetExecCommandLines()
    {
        if (string.IsNullOrWhiteSpace(this.ExecCommands))
        {
            return [];
        }

        return this.ExecCommands
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Split(['\n', ';', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static bool TryTakeValue(string[] args, ref int index, string name, string? inline, out string? value, out string? error)
    {
        error = null;
        if (inline is not null)
        {
            value = inline;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"error: missing value for {name}.";
            return false;
        }

        string next = args[index + 1];
        if (!EmudbgOptionTable.IsNumericValue(name) && next.StartsWith('-'))
        {
            value = null;
            error = $"error: missing value for {name}.";
            return false;
        }

        index++;
        value = next;
        return true;
    }

    private static bool IsName(string arg, string name) =>
        arg.Equals(name, EmudbgOptionTable.Comparison);
}