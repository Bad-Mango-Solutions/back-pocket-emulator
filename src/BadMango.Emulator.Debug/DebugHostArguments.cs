// <copyright file="DebugHostArguments.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug;

using BadMango.Emulator.Debug.Infrastructure;

/// <summary>
/// Host flags consumed before the generic host and the REPL see the command line.
/// </summary>
/// <remarks>
/// <para>
/// Emudbg options are removed so later configuration parsing does not treat them as keys or bind
/// the following argument as a value. <see cref="HeadlessOption"/> and <see cref="DumpAssembliesOption"/>
/// are host flags. <see cref="DumpAssembliesOption"/> is a diagnostic used by the headless smoke test:
/// it writes one loaded-assembly line after the REPL exits, which is how the test proves Avalonia
/// stayed unloaded.
/// </para>
/// <para>
/// The names come from <see cref="EmudbgOptionTable"/> and use that table's case-insensitive comparison,
/// the same comparison <see cref="EmudbgOptions"/> uses.
/// </para>
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
    /// <param name="option">The flag to look for, compared with <see cref="EmudbgOptionTable.Comparison"/>.</param>
    /// <returns><see langword="true"/> when the flag is present.</returns>
    public static bool Has(string[] args, string option)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrEmpty(option);
        return args.Any(arg => arg.Equals(option, EmudbgOptionTable.Comparison));
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
            if (EmudbgOptionTable.TryMatchValueOption(arg, out _, out string? inline))
            {
                if (inline is null && i + 1 < args.Length)
                {
                    i++;
                }

                continue;
            }

            if (EmudbgOptionTable.IsFlag(arg))
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
}