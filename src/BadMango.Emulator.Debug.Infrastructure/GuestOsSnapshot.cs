// <copyright file="GuestOsSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>The guest OS the detector last published, plus the boot stage.</summary>
/// <param name="Id">The stable id, such as <c>dos33</c> or <c>none</c>.</param>
/// <param name="DisplayName">The short name.</param>
/// <param name="VersionLabel">The version text. Empty when none is loaded.</param>
/// <param name="Reason">Why this result was reported.</param>
/// <param name="Stage">The boot stage name.</param>
public sealed record GuestOsSnapshot(string Id, string DisplayName, string VersionLabel, string Reason, string Stage);