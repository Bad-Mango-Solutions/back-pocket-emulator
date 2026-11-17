// <copyright file="DebugMcpOptionsValidator.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="DebugMcpOptions"/> at host start.
/// </summary>
internal sealed class DebugMcpOptionsValidator : IValidateOptions<DebugMcpOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DebugMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.SandboxRoots is null)
        {
            return ValidateOptionsResult.Fail("DebugMcp:SandboxRoots is required.");
        }

        var failures = new List<string>();
        var seen = new HashSet<string>(PathComparison());
        for (int index = 0; index < options.SandboxRoots.Count; index++)
        {
            string root = options.SandboxRoots[index];
            if (string.IsNullOrWhiteSpace(root))
            {
                failures.Add($"DebugMcp:SandboxRoots[{index}] is empty.");
                continue;
            }

            if (!Path.IsPathRooted(root))
            {
                failures.Add($"DebugMcp:SandboxRoots[{index}] must be an absolute path.");
                continue;
            }

            string full;
            try
            {
                full = SandboxPathGuard.Normalize(root);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                failures.Add($"DebugMcp:SandboxRoots[{index}] is not a valid path.");
                continue;
            }

            if (!seen.Add(full))
            {
                failures.Add($"DebugMcp:SandboxRoots[{index}] duplicates another sandbox root.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static StringComparer PathComparison()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}