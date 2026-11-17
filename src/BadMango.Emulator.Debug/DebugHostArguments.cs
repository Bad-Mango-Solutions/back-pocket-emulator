// <copyright file="DebugHostArguments.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug;

/// <summary>
/// Host flags consumed before the generic host and the REPL see the command line.
/// </summary>
/// <remarks>
/// <see cref="HeadlessOption"/> is removed so later configuration parsing does not treat it as a
/// key or bind the following argument as its value. <see cref="DumpAssembliesOption"/> is a
/// diagnostic used by the headless smoke test.
/// </remarks>
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
    /// Returns <paramref name="args"/> with host flags removed, preserving every other argument.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The arguments the generic host should see.</returns>
    public static string[] WithoutHostOptions(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Where(static arg => !IsHostOption(arg)).ToArray();
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

    private static bool IsHostOption(string arg) =>
        arg.Equals(HeadlessOption, StringComparison.OrdinalIgnoreCase)
        || arg.Equals(DumpAssembliesOption, StringComparison.OrdinalIgnoreCase);
}