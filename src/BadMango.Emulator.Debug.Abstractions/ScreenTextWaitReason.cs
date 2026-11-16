// <copyright file="ScreenTextWaitReason.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

using System.Text.Json.Serialization;

/// <summary>
/// Describes why <see cref="IMachineIoSession.WaitForScreenText"/> stopped.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreenTextWaitReason>))]
public enum ScreenTextWaitReason
{
    /// <summary>
    /// The screen text matched the pattern.
    /// </summary>
    Matched,

    /// <summary>
    /// The cycle budget was exhausted before the pattern matched.
    /// </summary>
    BudgetExhausted,

    /// <summary>
    /// The wall-clock budget was exhausted before the pattern matched.
    /// </summary>
    WallClockTimeout,

    /// <summary>
    /// The CPU halted before the pattern matched.
    /// </summary>
    Halted,

    /// <summary>
    /// The wait was cancelled before the pattern matched.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A step or pattern match failed. <see cref="ScreenTextWaitResult.Exception"/> carries the failure.
    /// </summary>
    Faulted,
}