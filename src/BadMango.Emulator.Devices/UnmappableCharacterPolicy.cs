// <copyright file="UnmappableCharacterPolicy.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Policy used when a character cannot be mapped to a key press.
/// </summary>
public enum UnmappableCharacterPolicy
{
    /// <summary>
    /// Skip the unmappable character.
    /// </summary>
    Skip,

    /// <summary>
    /// Replace the unmappable character with a fallback character.
    /// </summary>
    Replace,

    /// <summary>
    /// Reject the entire text queue.
    /// </summary>
    Reject,
}
