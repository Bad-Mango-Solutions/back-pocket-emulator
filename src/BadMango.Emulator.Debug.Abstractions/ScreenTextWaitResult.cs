// <copyright file="ScreenTextWaitResult.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json.Serialization;

/// <summary>
/// The outcome of waiting for text to appear on the emulated screen.
/// </summary>
/// <param name="Reason">Why the wait stopped.</param>
/// <param name="CyclesConsumed">Execution cycles consumed while waiting.</param>
/// <param name="FaultType">The fault exception type name when <paramref name="Reason"/> is <see cref="ScreenTextWaitReason.Faulted"/>.</param>
/// <param name="FaultMessage">The fault exception message when <paramref name="Reason"/> is <see cref="ScreenTextWaitReason.Faulted"/>.</param>
public sealed record ScreenTextWaitResult(
    ScreenTextWaitReason Reason,
    ulong CyclesConsumed,
    string? FaultType = null,
    string? FaultMessage = null)
{
    /// <summary>
    /// Gets the exception that stopped the wait when <see cref="Reason"/> is <see cref="ScreenTextWaitReason.Faulted"/>.
    /// </summary>
    /// <remarks>
    /// This is omitted from JSON. <see cref="FaultType"/> and <see cref="FaultMessage"/> are the serialized form.
    /// </remarks>
    [JsonIgnore]
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets a value indicating whether the screen matched the pattern.
    /// </summary>
    public bool Matched => this.Reason == ScreenTextWaitReason.Matched;

    /// <summary>
    /// Creates a matched result.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before the match.</param>
    /// <returns>A matched result.</returns>
    public static ScreenTextWaitResult Match(ulong cyclesConsumed) =>
        new(ScreenTextWaitReason.Matched, cyclesConsumed);

    /// <summary>
    /// Creates a cycle-budget result.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before the budget was exhausted.</param>
    /// <returns>A budget-exhausted result.</returns>
    public static ScreenTextWaitResult BudgetExhausted(ulong cyclesConsumed) =>
        new(ScreenTextWaitReason.BudgetExhausted, cyclesConsumed);

    /// <summary>
    /// Creates a wall-clock timeout result.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before the timeout.</param>
    /// <returns>A wall-clock timeout result.</returns>
    public static ScreenTextWaitResult WallClockTimeout(ulong cyclesConsumed) =>
        new(ScreenTextWaitReason.WallClockTimeout, cyclesConsumed);

    /// <summary>
    /// Creates a halted result.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before the CPU halted.</param>
    /// <returns>A halted result.</returns>
    public static ScreenTextWaitResult Halted(ulong cyclesConsumed) =>
        new(ScreenTextWaitReason.Halted, cyclesConsumed);

    /// <summary>
    /// Creates a cancelled result.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before cancellation.</param>
    /// <returns>A cancelled result.</returns>
    public static ScreenTextWaitResult Cancelled(ulong cyclesConsumed) =>
        new(ScreenTextWaitReason.Cancelled, cyclesConsumed);

    /// <summary>
    /// Creates a faulted result that keeps the exception for in-process callers.
    /// </summary>
    /// <param name="cyclesConsumed">Cycles consumed before the fault.</param>
    /// <param name="exception">The exception that stopped the wait.</param>
    /// <returns>A faulted result.</returns>
    public static ScreenTextWaitResult Fault(ulong cyclesConsumed, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(ScreenTextWaitReason.Faulted, cyclesConsumed, exception.GetType().FullName, exception.Message)
        {
            Exception = exception,
        };
    }
}