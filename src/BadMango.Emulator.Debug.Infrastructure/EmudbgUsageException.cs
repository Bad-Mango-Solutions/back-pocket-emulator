// <copyright file="EmudbgUsageException.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>
/// The process arguments are not valid emudbg usage.
/// </summary>
public sealed class EmudbgUsageException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmudbgUsageException"/> class.
    /// </summary>
    /// <param name="message">The usage error.</param>
    public EmudbgUsageException(string message)
        : base(message)
    {
    }
}