// <copyright file="EmudbgOptionTable.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>
/// The single table of emudbg startup options.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EmudbgOptions"/> and the host-argument filter both use this table.
/// Option names are compared with <see cref="Comparison"/> (<see cref="StringComparison.OrdinalIgnoreCase"/>),
/// so <c>--JSON</c> and <c>--json</c> are the same flag. Keeping one table stops the two
/// parsers from accepting different spellings.
/// </para>
/// <para>
/// Value options take the next argument, or an <c>=</c> suffix (<c>--exec=regs</c>).
/// Flag options do not take a value.
/// </para>
/// </remarks>
public static class EmudbgOptionTable
{
    /// <summary>
    /// Comparison used for every startup option name.
    /// </summary>
    public const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    private static readonly string[] ValueNames =
    [
        "--profile",
        "-p",
        "--exec",
        "-e",
        "--file",
        "-f",
        "--max-cycles",
        "--seed",
    ];

    private static readonly string[] FlagNames =
    [
        "--no-banner",
        "--json",
        "-j",
        "--headless",
        "--agent",
        "--agent-mode",
        "--help",
        "-h",
        "-?",
        "/?",
        "--dump-assemblies",
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="arg"/> is a flag that takes no value.
    /// </summary>
    /// <param name="arg">One process argument.</param>
    /// <returns><see langword="true"/> when the argument is a known flag.</returns>
    public static bool IsFlag(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        foreach (string name in FlagNames)
        {
            if (arg.Equals(name, Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="arg"/> is a value option, with or without <c>=</c>.
    /// </summary>
    /// <param name="arg">One process argument.</param>
    /// <returns><see langword="true"/> when the argument is a known value option.</returns>
    public static bool IsValueOption(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        return TryMatchValueOption(arg, out _, out _);
    }

    /// <summary>
    /// Matches a value option and reports whether the value is inline.
    /// </summary>
    /// <param name="arg">One process argument.</param>
    /// <param name="name">The canonical option name, including the leading dashes.</param>
    /// <param name="inlineValue">The text after <c>=</c>, or <see langword="null"/> when the value is a separate argument.</param>
    /// <returns><see langword="true"/> when <paramref name="arg"/> is a known value option.</returns>
    public static bool TryMatchValueOption(string arg, out string name, out string? inlineValue)
    {
        ArgumentNullException.ThrowIfNull(arg);
        foreach (string candidate in ValueNames)
        {
            if (arg.Equals(candidate, Comparison))
            {
                name = candidate;
                inlineValue = null;
                return true;
            }

            string prefix = candidate + "=";
            if (arg.StartsWith(prefix, Comparison))
            {
                name = candidate;
                inlineValue = arg[prefix.Length..];
                return true;
            }
        }

        name = string.Empty;
        inlineValue = null;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the option reads a number rather than free text.
    /// </summary>
    /// <param name="name">The canonical option name.</param>
    /// <returns><see langword="true"/> for <c>--max-cycles</c> and <c>--seed</c>.</returns>
    public static bool IsNumericValue(string name) =>
        name.Equals("--max-cycles", Comparison) || name.Equals("--seed", Comparison);
}