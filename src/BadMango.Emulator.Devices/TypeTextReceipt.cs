// <copyright file="TypeTextReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Devices;

/// <summary>
/// Contains the result of a typed-text request.
/// </summary>
/// <param name="Accepted">The number of accepted characters.</param>
/// <param name="Skipped">Characters that were skipped because they were unmappable or otherwise ignored.</param>
/// <param name="Rejected">Whether the queue was rejected as a whole.</param>
public sealed record TypeTextReceipt(int Accepted, IReadOnlyList<char> Skipped, bool Rejected);