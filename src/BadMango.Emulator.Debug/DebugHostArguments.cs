// <copyright file="DebugHostArguments.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug;

/// <summary>
/// Host flags consumed before the generic host and the REPL see the command line.
/// </summary>
internal static class DebugHostArguments
{
    /// <summary>
    /// Starts the console without loading the Avalonia debug UI.
    /// </summary>
    public const string HeadlessOption = "--headless";

    /// <summary>
    /// Writes each loaded assembly name to standard output before the process exits.
    /// </summary>
    public const string DumpAssembliesOption = "--dump-assemblies";

    /// <summary>
    /// The prefix printed before each loaded assembly name.
    /// </summary>
    public const string LoadedAssemblyPrefix = "LOADED:";

    /// <summary>
    /// Returns a value indicating whether <paramref name="args"/> contains <paramref name="option"/>.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="option">The flag to look for, compared case-insensitively.</param>
    /// <returns><see langword="true"/> when the flag is present.</returns>
    public static bool Has(string[] args, string option)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrEmpty(option);
        return args.Any(arg => arg.Equals(option, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <paramref name="args"/> with emudbg host options removed.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The arguments the generic host should see.</returns>
    public static string[] WithoutHostOptions(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var filtered = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (IsValueOption(arg, "--profile", "-p") ||
                IsValueOption(arg, "--exec", "-e") ||
                IsValueOption(arg, "--file", "-f") ||
                IsValueOption(arg, "--max-cycles", null) ||
                IsValueOption(arg, "--seed", null))
            {
                if (NeedsSeparateValue(arg) && i + 1 < args.Length)
                {
                    i++;
                }

                continue;
            }

            if (IsFlagOption(arg, "--no-banner") ||
                IsFlagOption(arg, "--json") ||
                IsFlagOption(arg, "-j") ||
                IsFlagOption(arg, "--agent") ||
                IsFlagOption(arg, "--agent-mode") ||
                IsFlagOption(arg, "--help") ||
                IsFlagOption(arg, "-h") ||
                IsFlagOption(arg, "-?") ||
                IsFlagOption(arg, "/?") ||
                IsFlagOption(arg, HeadlessOption) ||
                IsFlagOption(arg, DumpAssembliesOption))
            {
                continue;
            }

            filtered.Add(arg);
        }

        return [.. filtered];
    }

    /// <summary>
    /// Writes one <see cref="LoadedAssemblyPrefix"/> line for each assembly loaded in this process.
    /// </summary>
    public static void WriteLoadedAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal))
        {
            Console.WriteLine(LoadedAssemblyPrefix + assembly.GetName().Name);
        }
    }

    private static bool IsFlagOption(string arg, string option)
        => arg.Equals(option, StringComparison.OrdinalIgnoreCase);

    private static bool IsValueOption(string arg, string longName, string? shortName)
    {
        if (arg.Equals(longName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(shortName) && arg.Equals(shortName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (arg.StartsWith(longName + "=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(shortName) && arg.StartsWith(shortName + "=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsSeparateValue(string arg)
        => !arg.Contains('=', StringComparison.Ordinal);
}