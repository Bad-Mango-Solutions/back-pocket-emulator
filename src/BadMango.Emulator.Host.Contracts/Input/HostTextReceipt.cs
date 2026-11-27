// <copyright file="HostTextReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host.Contracts;

/// <summary>
/// How many characters <see cref="IHostInput.InjectText"/> turned into key events.
/// </summary>
/// <param name="Accepted">Characters that became a key down and key up.</param>
/// <param name="Skipped">Characters that have no key in this contract's text map.</param>
public readonly record struct HostTextReceipt(int Accepted, int Skipped);