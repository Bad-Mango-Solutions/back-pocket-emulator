// <copyright file="HostKeystroke.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// A typed key press or host key event reduced to a shared key and modifier snapshot.
/// </summary>
/// <param name="Key">The shared keyboard key.</param>
/// <param name="Modifiers">The modifier snapshot active for the key.</param>
public readonly record struct HostKeystroke(KeyboardKey Key, KeyboardModifiers Modifiers);
