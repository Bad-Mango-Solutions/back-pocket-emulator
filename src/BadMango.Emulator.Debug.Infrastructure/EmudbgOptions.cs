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
/// <param name="MaxCycles">Optional maximum cycle budget used by batch execution.</param>
/// <param name="Seed">Optional deterministic seed for scripted workflows.</param>
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
    int? Seed = null)
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
    public static EmudbgOptions Parse(string[] args)
    {
        string? profile = null;
        string? exec = null;
        string? file = null;
        bool noBanner = false;
        bool showHelp = false;
        bool jsonOutput = false;
        bool headless = false;
        bool agentMode = false;
        ulong? maxCycles = null;
        int? seed = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (TryReadValue(args, ref i, arg, "--profile", "-p", out string? profileValue))
            {
                profile = profileValue;
                continue;
            }

            if (TryReadValue(args, ref i, arg, "--exec", "-e", out string? execValue))
            {
                exec = execValue;
                continue;
            }

            if (TryReadValue(args, ref i, arg, "--file", "-f", out string? fileValue))
            {
                file = fileValue;
                continue;
            }

            if (TryReadValue(args, ref i, arg, "--max-cycles", null, out string? maxCyclesValue))
            {
                if (ulong.TryParse(maxCyclesValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedMaxCycles))
                {
                    maxCycles = parsedMaxCycles;
                }

                continue;
            }

            if (TryReadValue(args, ref i, arg, "--seed", null, out string? seedValue))
            {
                if (int.TryParse(seedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSeed))
                {
                    seed = parsedSeed;
                }

                continue;
            }

            switch (arg)
            {
                case "--no-banner":
                    noBanner = true;
                    break;
                case "--json":
                case "-j":
                    jsonOutput = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--agent":
                case "--agent-mode":
                    agentMode = true;
                    headless = true;
                    jsonOutput = true;
                    break;
                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    showHelp = true;
                    break;
            }
        }

        return new EmudbgOptions(profile, exec, file, noBanner, showHelp, jsonOutput, headless, agentMode, maxCycles, seed);
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

    private static bool TryReadValue(string[] args, ref int index, string arg, string longName, string? shortName, out string? value)
    {
        value = null;

        if (arg.StartsWith(longName + "=", StringComparison.Ordinal))
        {
            value = arg[(longName.Length + 1)..];
            return true;
        }

        if (!string.IsNullOrEmpty(shortName) && arg.StartsWith(shortName + "=", StringComparison.Ordinal))
        {
            value = arg[(shortName.Length + 1)..];
            return true;
        }

        if (arg.Equals(longName, StringComparison.Ordinal) || (!string.IsNullOrEmpty(shortName) && arg.Equals(shortName, StringComparison.Ordinal)))
        {
            if (index + 1 < args.Length)
            {
                value = args[++index];
            }

            return true;
        }

        return false;
    }
}