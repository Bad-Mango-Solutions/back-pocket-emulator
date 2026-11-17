// <copyright file="DebugSessions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

using BadMango.Emulator.Debug.Abstractions;
using BadMango.Emulator.Debug.Infrastructure.Commands;

/// <summary>
/// Resolves the process-wide debug session for a command.
/// </summary>
internal static class DebugSessions
{
    /// <summary>
    /// Returns the injected session, or the session bound to <paramref name="context"/>.
    /// </summary>
    /// <param name="injected">The session supplied by dependency injection, if any.</param>
    /// <param name="context">The command's debug context.</param>
    /// <returns>The shared session.</returns>
    /// <exception cref="InvalidOperationException">No session is available.</exception>
    public static IDebugSession Require(IDebugSession? injected, IDebugContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (injected is not null)
        {
            return injected;
        }

        if (context is DebugContext concrete)
        {
            return concrete.GetSharedSession();
        }

        throw new InvalidOperationException("No debug session is available.");
    }
}