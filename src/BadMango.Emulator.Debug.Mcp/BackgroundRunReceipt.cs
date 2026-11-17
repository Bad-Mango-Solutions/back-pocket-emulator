// <copyright file="BackgroundRunReceipt.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Debug.Mcp;

using BadMango.Emulator.Debug.Abstractions;

/// <summary>
/// The payload for a background <c>run_until</c> start, poll, or stop.
/// </summary>
/// <param name="Action">The action that produced this receipt: start, poll, or stop.</param>
/// <param name="Running">Whether the run is still in progress.</param>
/// <param name="RunId">The background run identifier.</param>
/// <param name="Result">The completed run result, when the run has finished.</param>
public sealed record BackgroundRunReceipt(string Action, bool Running, string? RunId, RunResult? Result);