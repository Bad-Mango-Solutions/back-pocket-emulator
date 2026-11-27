// <copyright file="VideoStateSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Represents the current video switch state.
/// </summary>
/// <param name="Mode">The active computed mode name.</param>
/// <param name="IsTextMode">Whether text mode is active.</param>
/// <param name="IsMixedMode">Whether mixed mode is active.</param>
/// <param name="IsPage2">Whether page 2 is selected.</param>
/// <param name="IsHiRes">Whether hi-res mode is active.</param>
/// <param name="Is80Column">Whether 80-column mode is active.</param>
/// <param name="IsDoubleHiRes">Whether double hi-res mode is active.</param>
/// <param name="IsVerticalBlanking">Whether vertical blanking is active.</param>
/// <param name="Annunciators">Current annunciator outputs.</param>
public sealed record VideoStateSnapshot(
    string Mode,
    bool IsTextMode,
    bool IsMixedMode,
    bool IsPage2,
    bool IsHiRes,
    bool Is80Column,
    bool IsDoubleHiRes,
    bool IsVerticalBlanking,
    IReadOnlyList<bool> Annunciators);