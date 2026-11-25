// <copyright file="OsCallLogSnapshot.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Infrastructure;

/// <summary>The OS call log as the console and MCP tools return it.</summary>
/// <param name="Mode"><c>off</c>, <c>mli</c>, <c>dos</c>, or <c>all</c>.</param>
/// <param name="Calls">The recorded calls, oldest first.</param>
public sealed record OsCallLogSnapshot(string Mode, IReadOnlyList<OsCallLine> Calls);