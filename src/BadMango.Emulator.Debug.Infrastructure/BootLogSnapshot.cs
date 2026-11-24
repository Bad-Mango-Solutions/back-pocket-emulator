// <copyright file="BootLogSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>The boot milestone log.</summary>
/// <param name="Stage">The boot stage name.</param>
/// <param name="BootSectorReentries">Times <c>$0801</c> ran again after the sector was classified.</param>
/// <param name="Milestones">The log, oldest first.</param>
public sealed record BootLogSnapshot(string Stage, int BootSectorReentries, IReadOnlyList<BootLogLine> Milestones);