// <copyright file="TypeKeysReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// Contains the result of a typed-keys request.
/// </summary>
/// <param name="Accepted">The number of accepted characters.</param>
/// <param name="Skipped">Characters skipped by the keyboard input pipeline.</param>
/// <param name="Rejected">Whether the request was rejected entirely.</param>
public sealed record TypeKeysReceipt(int Accepted, IReadOnlyList<char> Skipped, bool Rejected);