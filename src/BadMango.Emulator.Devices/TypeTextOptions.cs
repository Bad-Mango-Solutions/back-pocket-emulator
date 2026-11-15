// <copyright file="TypeTextOptions.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Options controlling queued text input.
/// </summary>
public record TypeTextOptions
{
    /// <summary>
    /// Gets or sets how long each key is held before release, in milliseconds.
    /// </summary>
    public int HoldMs { get; init; } = 50;

    /// <summary>
    /// Gets or sets the gap between keys, or <see langword="null"/> to derive it from <see cref="HoldMs"/>.
    /// </summary>
    public int? GapMs { get; init; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether typing waits for the strobe to clear before the next key.
    /// </summary>
    public bool WaitForConsumption { get; init; } = true;

    /// <summary>
    /// Gets or sets the poll interval while waiting for the strobe to clear.
    /// </summary>
    public int StrobeWaitPollMs { get; init; } = 1;

    /// <summary>
    /// Gets or sets extra delay after a carriage return, in milliseconds.
    /// </summary>
    public int ReturnDelayMs { get; init; } = 0;

    /// <summary>
    /// Gets or sets a value indicating whether letters are forced uppercase.
    /// </summary>
    public bool ForceUppercase { get; init; } = false;

    /// <summary>
    /// Gets or sets the policy when a character has no matching keystroke.
    /// </summary>
    public UnmappableCharacterPolicy Unmappable { get; init; } = UnmappableCharacterPolicy.Skip;

    /// <summary>
    /// Gets or sets the replacement character to use when <see cref="Unmappable"/> is <see cref="UnmappableCharacterPolicy.Replace"/>.
    /// </summary>
    public char ReplacementChar { get; init; } = '?';
}
