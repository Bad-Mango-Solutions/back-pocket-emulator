// <copyright file="DebugMcpBudgets.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

/// <summary>
/// Cycle budgets shared by the lifecycle tools.
/// </summary>
public static class DebugMcpBudgets
{
    /// <summary>
    /// The default cycle budget for <c>step</c> and <c>run_until</c>.
    /// </summary>
    public const ulong DefaultMaxCycles = 1_000_000;

    /// <summary>
    /// Budgets above this ceiling are started in the background instead of blocking the stdio session.
    /// </summary>
    public const ulong SynchronousMaxCycles = 5_000_000;
}